using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace VidGrab
{
    internal static class YtDlp
    {
        public static string ProxyUrl = "";

        public static async Task<MediaInfo> ProbeAsync(string url)
        {
            var psi = new ProcessStartInfo();
            psi.FileName = Binaries.YtDlp;
            psi.Arguments = "-J --no-warnings --no-playlist " + ProxyArgs() + CookieStore.YtDlpArgs() + Quote(url);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;

            string stdout, stderr;
            using (var p = new Process { StartInfo = psi })
            {
                p.Start();
                var readOut = p.StandardOutput.ReadToEndAsync();
                var readErr = p.StandardError.ReadToEndAsync();
                await Task.Run(() => p.WaitForExit());
                stdout = await readOut;
                stderr = await readErr;
                if (p.ExitCode != 0)
                    throw new Exception("yt-dlp 解析失败：" + Trim(stderr));
            }

            if (string.IsNullOrWhiteSpace(stdout)) throw new Exception("yt-dlp 没有返回数据：" + Trim(stderr));

            var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var root = js.Deserialize<Dictionary<string, object>>(stdout);
            if (root == null) throw new Exception("无法解析 yt-dlp 返回的 JSON");

            var info = new MediaInfo
            {
                Url = url,
                Title = Str(root, "title"),
                Author = Str(root, "uploader"),
                Duration = FmtDuration(Dbl(root, "duration"))
            };

            double durationSec = Dbl(root, "duration");
            var formats = new List<FormatItem>();
            object fobj;
            if (root.TryGetValue("formats", out fobj))
            {
                var best = new Dictionary<int, Tuple<double, FormatItem>>();
                foreach (object o in AsObjects(fobj))
                {
                    var d = o as Dictionary<string, object>;
                    if (d == null) continue;
                    string vcodec = Str(d, "vcodec");
                    if (vcodec == "none") continue;
                    int height = (int)Dbl(d, "height");
                    if (height <= 0) continue;

                    string ext = Str(d, "ext");
                    string fid = Str(d, "format_id");
                    string acodec = Str(d, "acodec");
                    long size = (long)Dbl(d, "filesize");
                    if (size <= 0) size = (long)Dbl(d, "filesize_approx");
                    double tbr = Dbl(d, "tbr");
                    int fps = (int)Dbl(d, "fps");
                    int width = (int)Dbl(d, "width");

                    var item = new FormatItem
                    {
                        Width = width,
                        Height = height,
                        Title = (width > 0 ? width + "×" + height : height + "p") + (fps >= 50 ? " 60fps" : ""),
                        FormatId = fid + (acodec == "none" ? "+bestaudio/best" : ""),
                        Ext = ext,
                        Codec = ShortCodec(vcodec),
                        SizeText = size > 0 ? FmtSize(size) : "",
                        Note = Str(d, "format_note"),
                        ApproxBytes = size
                    };

                    if (size <= 0 && tbr > 0 && durationSec > 0)
                    {
                        long estimate = (long)(tbr * 1000 / 8 * durationSec);
                        if (estimate > 0)
                        {
                            item.SizeText = "~" + FmtSize(estimate);
                            item.ApproxBytes = estimate;
                        }
                    }

                    // Compatibility first: H.264 plays everywhere, then AV1, then VP9.
                    double score = tbr;
                    if (vcodec.StartsWith("avc") || vcodec.StartsWith("h264")) score += 100000;
                    else if (vcodec.StartsWith("av01")) score += 2000;
                    else if (vcodec.StartsWith("vp")) score += 1000;
                    if (acodec != "none") score += 200;

                    Tuple<double, FormatItem> existing;
                    if (!best.TryGetValue(height, out existing) || score > existing.Item1)
                        best[height] = Tuple.Create(score, item);
                }

                formats = best.Values.Select(v => v.Item2).OrderByDescending(v => v.Height).ToList();
            }

            if (formats.Count == 0 && Str(root, "url").Length > 0)
            {
                formats.Add(new FormatItem
                {
                    Title = "原始",
                    FormatId = "best",
                    Ext = Str(root, "ext"),
                    Codec = ShortCodec(Str(root, "vcodec")),
                    SizeText = "",
                    Note = "最佳可用"
                });
            }

            formats.Add(new FormatItem
            {
                Title = "仅音频",
                FormatId = "bestaudio/best",
                Ext = "m4a",
                Codec = "audio",
                SizeText = "",
                Note = "只下载声音"
            });

            info.Formats = formats;
            return info;
        }

        public static async Task DownloadAsync(MediaInfo info, FormatItem fmt, string dir, bool mergeMp4,
            bool downloadSubs, Action<int, string> progress, Action<string> status, CancellationToken token)
        {
            string template = System.IO.Path.Combine(dir, "%(title).80B [%(id)s][%(format_id)s].%(ext)s");
            string args = "--no-playlist --newline " + CookieStore.YtDlpArgs() +
                          "--ffmpeg-location " + Quote(Binaries.BinDir) + " " +
                          "-f " + Quote(fmt.FormatId) + " " +
                          (mergeMp4 ? "--merge-output-format mp4 " : "") +
                          "-o " + Quote(template) + " " + Quote(info.Url);

            var psi = new ProcessStartInfo();
            psi.FileName = Binaries.YtDlp;
            psi.Arguments = args;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;

            await Task.Run(() =>
            {
                var errors = new StringBuilder();
                using (var p = new Process { StartInfo = psi })
                {
                    p.OutputDataReceived += (s, e) =>
                    {
                        if (e.Data == null) return;
                        string line = e.Data;
                        var m = Regex.Match(line,
                            @"\[download\]\s+([\d\.]+)%\s+of\s+~?\s*([\d\.]+\s*\w+)(?:\s+at\s+([\d\.]+\s*\w+/s))?(?:\s+ETA\s+([\d:]+))?");
                        if (m.Success)
                        {
                            int pct = (int)double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                            string text = m.Groups[1].Value + "% · " + m.Groups[2].Value.Trim();
                            if (m.Groups[3].Success) text += " · " + m.Groups[3].Value.Trim();
                            if (m.Groups[4].Success) text += " · ETA " + m.Groups[4].Value.Trim();
                            progress(pct, "下载中 " + text);
                        }
                        else if (line.IndexOf("has already been downloaded", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            status("同名文件已存在，跳过（换清晰度会生成新文件）");
                        }
                        else if (line.IndexOf("[Merger]", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            status("正在合并音视频…");
                        }
                    };
                    p.ErrorDataReceived += (s, e) =>
                    {
                        if (e.Data != null) lock (errors) errors.AppendLine(e.Data);
                    };
                    p.Start();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();

                    while (!p.WaitForExit(300))
                    {
                        if (token.IsCancellationRequested)
                        {
                            try { p.Kill(); } catch { }
                            break;
                        }
                    }
                    if (token.IsCancellationRequested) throw new OperationCanceledException();
                    if (p.ExitCode != 0)
                    {
                        string tail;
                        lock (errors)
                        {
                            var lines = errors.ToString().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                            tail = lines.Length > 0 ? lines[lines.Length - 1] : "未知错误";
                        }
                        throw new Exception("yt-dlp 退出码 " + p.ExitCode + "：" + tail);
                    }
                }
            });
        }

        private static string Quote(string s) { return "\"" + s + "\""; }

        private static string ProxyArgs()
        {
            return string.IsNullOrWhiteSpace(ProxyUrl) ? "" : "--proxy " + Quote(ProxyUrl) + " ";
        }

        /// <summary>JavaScriptSerializer turns JSON arrays into ArrayList, not object[].</summary>
        private static System.Collections.IEnumerable AsObjects(object o)
        {
            if (o == null) return new object[0];
            var arr = o as object[];
            if (arr != null) return arr;
            var list = o as System.Collections.IEnumerable;
            if (list != null && !(o is string)) return list.Cast<object>();
            return new object[0];
        }

        private static string ShortCodec(string vcodec)
        {
            if (string.IsNullOrEmpty(vcodec) || vcodec == "none") return "";
            string v = vcodec.Split('.')[0];
            if (v.StartsWith("avc") || v.StartsWith("h264")) return "H.264";
            if (v.StartsWith("hev") || v.StartsWith("hvc") || v.StartsWith("h265")) return "H.265";
            if (v.StartsWith("vp9") || v.StartsWith("vp09")) return "VP9";
            if (v.StartsWith("av01")) return "AV1";
            return v;
        }

        private static string Str(Dictionary<string, object> d, string k)
        {
            object v; return d.TryGetValue(k, out v) && v != null ? Convert.ToString(v) : "";
        }

        private static double Dbl(Dictionary<string, object> d, string k)
        {
            object v; if (!d.TryGetValue(k, out v) || v == null) return 0;
            double r;
            return double.TryParse(Convert.ToString(v), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out r) ? r : 0;
        }

        private static string FmtDuration(double seconds)
        {
            if (seconds <= 0) return "";
            var t = TimeSpan.FromSeconds(seconds);
            return t.Hours > 0
                ? string.Format("{0}:{1:00}:{2:00}", t.Hours, t.Minutes, t.Seconds)
                : string.Format("{0}:{1:00}", t.Minutes, t.Seconds);
        }

        private static string FmtSize(long bytes)
        {
            double b = bytes;
            if (b >= 1024.0 * 1024 * 1024) return (b / 1024 / 1024 / 1024).ToString("0.00") + " GB";
            if (b >= 1024 * 1024) return (b / 1024 / 1024).ToString("0.0") + " MB";
            if (b >= 1024) return (b / 1024).ToString("0") + " KB";
            return bytes + " B";
        }

        private static string Trim(string s)
        {
            if (string.IsNullOrEmpty(s)) return "无输出";
            var lines = s.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return lines.Length == 0 ? s : lines[lines.Length - 1].Trim();
        }
    }
}
