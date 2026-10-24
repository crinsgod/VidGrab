using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace VidGrab
{
    /// <summary>
    /// Douyin: the public parse endpoint gives us the metadata and the official no-watermark
    /// play id, then we download straight from aweme.snssdk.com. No login, no cookies,
    /// no browser automation.
    /// </summary>
    internal static class Douyin
    {
        private const string ParseApi = "https://downloader-api.bhwa233.com/api/parse?url=";

        private const string UserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36";

        public static async Task<MediaInfo> ExtractAsync(string url, Action<string> status)
        {
            if (status != null) status("正在解析抖音链接…");

            string json;
            using (var http = NewHttp())
            {
                json = await http.GetStringAsync(ParseApi + Uri.EscapeDataString(url));
            }

            var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var root = js.Deserialize<Dictionary<string, object>>(json);
            if (root == null) throw new Exception("抖音解析服务没有返回数据");

            object successObj;
            if (root.TryGetValue("success", out successObj) && successObj is bool && !(bool)successObj)
            {
                string msg = "解析失败";
                object errObj;
                if (root.TryGetValue("error", out errObj))
                {
                    var err = errObj as Dictionary<string, object>;
                    if (err != null && err.ContainsKey("message")) msg = Convert.ToString(err["message"]);
                }
                throw new Exception("抖音解析失败：" + msg);
            }

            var data = root.ContainsKey("data") ? root["data"] as Dictionary<string, object> : null;
            if (data == null) throw new Exception("抖音解析服务返回的数据不完整");

            string title = Str(data, "title");
            if (string.IsNullOrWhiteSpace(title)) title = Str(data, "desc");
            if (string.IsNullOrWhiteSpace(title)) title = "抖音视频";

            var info = new MediaInfo { Url = url, Title = title, Author = "", Duration = "" };

            string origin = Str(data, "originDownloadVideoUrl");
            string proxied = Str(data, "downloadVideoUrl");

            string videoId = ExtractVideoId(origin);
            if (!string.IsNullOrEmpty(videoId))
            {
                info.Formats.Add(new FormatItem
                {
                    Title = "720p",
                    Ext = "mp4",
                    Codec = "H.264",
                    SizeText = "",
                    Note = "抖音 · 无水印直链",
                    IsDouyin = true,
                    DirectUrl = "https://aweme.snssdk.com/aweme/v1/play/?video_id=" + videoId + "&ratio=720p&line=0"
                });
            }

            if (!string.IsNullOrEmpty(proxied))
            {
                info.Formats.Add(new FormatItem
                {
                    Title = "原画",
                    Ext = "mp4",
                    Codec = "H.264",
                    SizeText = "",
                    Note = "抖音 · 解析服务中转",
                    IsDouyin = true,
                    DirectUrl = proxied
                });
            }

            if (info.Formats.Count == 0) throw new Exception("没有找到抖音视频地址");
            return info;
        }

        public static async Task DownloadAsync(MediaInfo info, FormatItem fmt, string dir,
            Action<int, string> progress, CancellationToken token)
        {
            if (string.IsNullOrEmpty(fmt.DirectUrl)) throw new Exception("没有可用的抖音直链");

            string name = Sanitize(string.IsNullOrEmpty(info.Title) ? "douyin" : info.Title);
            string path = UniquePath(Path.Combine(dir, name + ".mp4"));

            using (var http = NewHttp())
            using (var req = new HttpRequestMessage(HttpMethod.Get, fmt.DirectUrl))
            {
                req.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
                req.Headers.TryAddWithoutValidation("Referer", "https://www.douyin.com/");

                using (var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token))
                {
                    resp.EnsureSuccessStatusCode();
                    long total = resp.Content.Headers.ContentLength ?? -1;
                    using (var src = await resp.Content.ReadAsStreamAsync())
                    using (var dst = File.Create(path))
                    {
                        var buf = new byte[81920];
                        long read = 0;
                        var sw = Stopwatch.StartNew();
                        long lastBytes = 0;
                        long lastTick = 0;

                        while (true)
                        {
                            token.ThrowIfCancellationRequested();
                            int n = await src.ReadAsync(buf, 0, buf.Length, token);
                            if (n <= 0) break;
                            await dst.WriteAsync(buf, 0, n, token);
                            read += n;

                            long now = sw.ElapsedMilliseconds;
                            if (now - lastTick > 300)
                            {
                                double speed = (read - lastBytes) / 1048576.0 / ((now - lastTick) / 1000.0);
                                lastBytes = read; lastTick = now;
                                int pct = total > 0 ? (int)(read * 100 / total) : 0;
                                string text = total > 0
                                    ? string.Format("下载中 {0}% · {1:0.0}/{2:0.0} MB · {3:0.0} MB/s", pct, read / 1048576.0, total / 1048576.0, speed)
                                    : string.Format("下载中 {0:0.0} MB · {1:0.0} MB/s", read / 1048576.0, speed);
                                progress(pct, text);
                            }
                        }
                    }
                }
            }
        }

        private static HttpClient NewHttp()
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
            };
            var http = new HttpClient(handler) { Timeout = TimeSpan.FromHours(2) };
            http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");
            return http;
        }

        private static string ExtractVideoId(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            var m = System.Text.RegularExpressions.Regex.Match(url, "video_id=([^&]+)");
            return m.Success ? m.Groups[1].Value : null;
        }

        private static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) && v != null ? Convert.ToString(v) : "";
        }

        private static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name)) return "video";
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            name = name.Trim().TrimEnd('.');
            if (name.Length > 80) name = name.Substring(0, 80).Trim();
            return string.IsNullOrEmpty(name) ? "video" : name;
        }

        private static string UniquePath(string path)
        {
            if (!File.Exists(path)) return path;
            string dir = Path.GetDirectoryName(path);
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            for (int i = 2; i < 1000; i++)
            {
                string candidate = Path.Combine(dir, name + " (" + i + ")" + ext);
                if (!File.Exists(candidate)) return candidate;
            }
            return path;
        }
    }
}
