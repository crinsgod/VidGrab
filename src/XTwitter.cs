using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace VidGrab
{
    /// <summary>
    /// X/Twitter: yt-dlp's guest API returns a tombstone for sensitive or video-only tweets,
    /// so we read the public fxtwitter JSON, which exposes every mp4 transcode directly.
    /// </summary>
    internal static class XTwitter
    {
        private const string UserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36";

        public static async Task<MediaInfo> ExtractAsync(string url, Action<string> status)
        {
            string id = ExtractStatusId(url);
            if (id == null) throw new Exception("无法从链接中识别推文 ID");

            if (status != null) status("正在通过 X 公开接口解析…");

            string json;
            using (var http = NewHttp())
            {
                try
                {
                    json = await http.GetStringAsync("https://api.fxtwitter.com/i/status/" + id);
                }
                catch (Exception ex)
                {
                    throw new Exception("X 接口请求失败：" + ex.GetBaseException().Message);
                }
            }

            var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var root = js.Deserialize<Dictionary<string, object>>(json);
            if (root == null) throw new Exception("X 接口没有返回数据");

            int code = (int)Dbl(root, "code");
            if (code != 200)
            {
                string msg = Str(root, "message");
                throw new Exception("X 解析失败（" + code + "）：" + (string.IsNullOrEmpty(msg) ? "推文不可用" : msg));
            }

            var tweet = root.ContainsKey("tweet") ? root["tweet"] as Dictionary<string, object> : null;
            if (tweet == null) throw new Exception("X 接口没有返回推文内容");

            string text = Str(tweet, "text");
            if (string.IsNullOrWhiteSpace(text)) text = "X 视频";
            var author = tweet.ContainsKey("author") ? tweet["author"] as Dictionary<string, object> : null;

            var info = new MediaInfo
            {
                Url = url,
                Title = text.Trim(),
                Author = author == null ? "" : Str(author, "screen_name"),
                Duration = ""
            };

            var media = tweet.ContainsKey("media") ? tweet["media"] as Dictionary<string, object> : null;
            if (media == null) throw new Exception("这条推文没有视频（可能是图片、纯文字或已被删除）");

            var videos = media.ContainsKey("videos") ? media["videos"] : null;
            var formats = new List<FormatItem>();
            var seen = new HashSet<string>();

            foreach (object vo in AsObjects(videos))
            {
                var video = vo as Dictionary<string, object>;
                if (video == null) continue;

                double duration = Dbl(video, "duration");
                if (duration > 0 && string.IsNullOrEmpty(info.Duration))
                    info.Duration = FmtDuration(duration);

                var list = video.ContainsKey("formats") ? video["formats"] : null;
                foreach (object fo in AsObjects(list))
                {
                    var f = fo as Dictionary<string, object>;
                    if (f == null) continue;
                    string container = Str(f, "container");
                    if (!string.Equals(container, "mp4", StringComparison.OrdinalIgnoreCase)) continue;
                    string direct = Str(f, "url");
                    if (string.IsNullOrEmpty(direct) || seen.Contains(direct)) continue;
                    seen.Add(direct);

                    int w = 0, h = 0;
                    var m = Regex.Match(direct, @"/(\d+)x(\d+)/");
                    if (m.Success)
                    {
                        w = int.Parse(m.Groups[1].Value);
                        h = int.Parse(m.Groups[2].Value);
                    }

                    double bitrate = Dbl(f, "bitrate");
                    long approx = bitrate > 0 && duration > 0 ? (long)(bitrate / 8.0 * duration) : 0;

                    formats.Add(new FormatItem
                    {
                        Title = w > 0 && h > 0 ? w + "×" + h : "原始",
                        Ext = "mp4",
                        Codec = "H.264",
                        SizeText = approx > 0 ? "~" + FmtSize(approx) : "",
                        Note = "X · 直链" + (bitrate > 0 ? " · " + (bitrate / 1000).ToString("0") + " kbps" : ""),
                        IsDouyin = false,
                        DirectUrl = direct,
                        Width = w,
                        Height = h,
                        ApproxBytes = approx
                    });
                }
            }

            if (formats.Count == 0) throw new Exception("这条推文没有可下载的 mp4 视频");

            formats.Sort((a, b) => (b.Width * (long)b.Height).CompareTo(a.Width * (long)a.Height));
            info.Formats = formats;
            return info;
        }

        public static async Task DownloadAsync(MediaInfo info, FormatItem fmt, string dir,
            Action<int, string> progress, CancellationToken token)
        {
            if (string.IsNullOrEmpty(fmt.DirectUrl)) throw new Exception("没有可用的 X 直链");

            string name = Sanitize(string.IsNullOrEmpty(info.Title) ? "x-video" : info.Title);
            string path = UniquePath(Path.Combine(dir, name + ".mp4"));

            await DirectDownloader.DownloadAsync(fmt.DirectUrl, path, "https://x.com/", UserAgent,
                progress, null, token);
        }

        private static string ExtractStatusId(string url)
        {
            var m = Regex.Match(url, @"/(?:status|statuses)/(\d+)");
            return m.Success ? m.Groups[1].Value : null;
        }

        private static HttpClient NewHttp()
        {
            try { System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12; }
            catch { }
            var handler = new HttpClientHandler { AllowAutoRedirect = true };
            var http = new HttpClient(handler) { Timeout = TimeSpan.FromHours(2) };
            http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
            return http;
        }

        private static System.Collections.IEnumerable AsObjects(object o)
        {
            if (o == null) return new object[0];
            var arr = o as object[];
            if (arr != null) return arr;
            var list = o as System.Collections.IEnumerable;
            if (list != null && !(o is string)) return System.Linq.Enumerable.Cast<object>(System.Linq.Enumerable.OfType<object>(list));
            return new object[0];
        }

        private static string Str(Dictionary<string, object> d, string k)
        {
            object v;
            return d != null && d.TryGetValue(k, out v) && v != null ? Convert.ToString(v) : "";
        }

        private static double Dbl(Dictionary<string, object> d, string k)
        {
            object v;
            if (d == null || !d.TryGetValue(k, out v) || v == null) return 0;
            double r;
            return double.TryParse(Convert.ToString(v), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out r) ? r : 0;
        }

        private static string FmtDuration(double seconds)
        {
            if (seconds <= 0) return "";
            var t = TimeSpan.FromSeconds(seconds);
            return t.Hours > 0 ? string.Format("{0}:{1:00}:{2:00}", t.Hours, t.Minutes, t.Seconds)
                               : string.Format("{0}:{1:00}", t.Minutes, t.Seconds);
        }

        private static string FmtSize(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024) return (bytes / 1024.0 / 1024 / 1024).ToString("0.00") + " GB";
            if (bytes >= 1024 * 1024) return (bytes / 1024.0 / 1024).ToString("0.0") + " MB";
            return (bytes / 1024.0).ToString("0") + " KB";
        }

        private static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name)) return "video";
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            name = name.Replace("\r", " ").Replace("\n", " ").Trim().TrimEnd('.');
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
