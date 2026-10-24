using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Web.Script.Serialization;

namespace VidGrab
{
    internal sealed class Settings
    {
        public string SaveDir { get; set; }
        public bool MergeMp4 { get; set; }
        public string Quality { get; set; }
        public string CookiePath { get; set; }
        public bool UseCookie { get; set; }
        public string Proxy { get; set; }
        public int Threads { get; set; }
        public bool DownloadSubs { get; set; }

        private static string FilePath
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VidGrab");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "settings.json");
            }
        }

        public static Settings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var s = new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(FilePath, System.Text.Encoding.UTF8));
                    if (s != null) return s;
                }
            }
            catch { }
            return new Settings
            {
                SaveDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                MergeMp4 = true,
                Quality = "最高",
                UseCookie = false,
                Proxy = "",
                Threads = 4,
                DownloadSubs = false
            };
        }

        public void Save()
        {
            try { File.WriteAllText(FilePath, new JavaScriptSerializer().Serialize(this), new System.Text.UTF8Encoding(false)); }
            catch { }
        }
    }

    /// <summary>Extracts the embedded yt-dlp/ffmpeg binaries on first launch.</summary>
    internal static class Binaries
    {
        public static string BinDir
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VidGrab", "bin");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string YtDlp { get { return Path.Combine(BinDir, "yt-dlp.exe"); } }
        public static string Ffmpeg { get { return Path.Combine(BinDir, "ffmpeg.exe"); } }

        public static void Ensure()
        {
            ExtractIfMissing("ytdlp.bin", YtDlp);
            ExtractIfMissing("ffmpeg.bin", Ffmpeg);
        }

        private static void ExtractIfMissing(string resource, string target)
        {
            if (File.Exists(target) && new FileInfo(target).Length > 1024 * 1024) return;

            using (Stream raw = typeof(Binaries).Assembly.GetManifestResourceStream(resource))
            {
                if (raw == null) throw new InvalidOperationException("缺少内置组件: " + resource);
                string tmp = target + ".tmp";
                using (var deflate = new DeflateStream(raw, CompressionMode.Decompress))
                using (var file = File.Create(tmp))
                {
                    deflate.CopyTo(file);
                }
                if (File.Exists(target)) File.Delete(target);
                File.Move(tmp, target);
            }
        }
    }

    internal sealed class FormatItem
    {
        public string Title;
        public string FormatId;
        public string Ext;
        public string Codec;
        public string SizeText;
        public string Note;
        public bool IsDouyin;
        public string DirectUrl;
        public long ApproxBytes;
        public int Width;
        public int Height;
    }

    internal sealed class MediaInfo
    {
        public string Url;
        public string Title;
        public string Author;
        public string Duration;
        public List<FormatItem> Formats = new List<FormatItem>();
    }

    internal sealed class DownloadTask
    {
        public string Url;
        public string Platform;
        public string Title;
        public MediaInfo Info;
        public FormatItem Format;
        public string State = "待下载";
        public string Message = "";
        public int Progress;
        public int RowIndex = -1;
    }

    /// <summary>
    /// Netscape-format cookies.txt imported by the user. Only a path is kept; the file
    /// stays on disk so yt-dlp can read it for every supported site.
    /// </summary>
    internal static partial class CookieStore
    {
        public static string Path = "";
        public static bool Enabled;

        public static void Configure(string path, bool enabled)
        {
            Path = path ?? "";
            Enabled = enabled && !string.IsNullOrEmpty(Path) && File.Exists(Path);
        }

        /// <summary>yt-dlp argument fragment, or empty when cookies are off.</summary>
        public static string YtDlpArgs()
        {
            if (!Enabled || !File.Exists(Path)) return "";
            return "--cookies \"" + Path + "\" ";
        }

        /// <summary>Cookie header for the given URL, from the imported cookie file.</summary>
        public static string HeaderFor(string url)
        {
            if (!Enabled || !File.Exists(Path)) return "";
            try
            {
                Uri uri;
                if (!Uri.TryCreate(url, UriKind.Absolute, out uri)) return "";
                string host = uri.Host;
                var pairs = new List<string>();

                foreach (string line in File.ReadAllLines(Path))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                    string[] parts = line.Split('\t');
                    if (parts.Length < 7) continue;

                    string domain = parts[0].Trim();
                    string name = parts[5];
                    string value = parts[6];
                    if (string.IsNullOrEmpty(name)) continue;

                    bool match = domain.StartsWith(".")
                        ? host.EndsWith(domain, StringComparison.OrdinalIgnoreCase)
                        : host.Equals(domain, StringComparison.OrdinalIgnoreCase);
                    if (match) pairs.Add(name + "=" + value);
                }

                return pairs.Count == 0 ? "" : string.Join("; ", pairs);
            }
            catch { return ""; }
        }
    }
}
