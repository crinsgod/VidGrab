using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace VidGrab
{
    internal sealed class CookieEntry
    {
        public string Domain;
        public bool IncludeSub;
        public string Path;
        public bool Secure;
        public string Expires;
        public string Name;
        public string Value;
    }

    internal static partial class CookieStore
    {
        /// <summary>One merged file holds every site's cookies; yt-dlp only sends matching domains.</summary>
        public static string MasterPath
        {
            get
            {
                string dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VidGrab");
                System.IO.Directory.CreateDirectory(dir);
                return System.IO.Path.Combine(dir, "cookies.txt");
            }
        }

        public static List<CookieEntry> Read(string file)
        {
            var list = new List<CookieEntry>();
            if (string.IsNullOrEmpty(file) || !File.Exists(file)) return list;

            foreach (string raw in File.ReadAllLines(file))
            {
                string line = raw;
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.StartsWith("#HttpOnly_"))
                    line = line.Substring("#HttpOnly_".Length);
                else if (line.StartsWith("#"))
                    continue;

                string[] p = line.Split('\t');
                if (p.Length < 7) continue;
                if (string.IsNullOrEmpty(p[5])) continue;

                list.Add(new CookieEntry
                {
                    Domain = p[0].Trim(),
                    IncludeSub = p[1].Trim().Equals("TRUE", StringComparison.OrdinalIgnoreCase),
                    Path = p[2].Trim(),
                    Secure = p[3].Trim().Equals("TRUE", StringComparison.OrdinalIgnoreCase),
                    Expires = p[4].Trim(),
                    Name = p[5],
                    Value = p[6]
                });
            }
            return list;
        }

        public static void Write(string file, List<CookieEntry> entries)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Netscape HTTP Cookie File");
            sb.AppendLine("# merged by VidGrab");
            foreach (var e in entries)
            {
                sb.AppendLine(string.Join("\t", new[]
                {
                    e.Domain,
                    e.IncludeSub ? "TRUE" : "FALSE",
                    string.IsNullOrEmpty(e.Path) ? "/" : e.Path,
                    e.Secure ? "TRUE" : "FALSE",
                    string.IsNullOrEmpty(e.Expires) ? "0" : e.Expires,
                    e.Name,
                    e.Value
                }));
            }
            File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
        }

        /// <summary>Merge a browser export into the master file; same domain/path/name is updated.</summary>
        public static int MergeFrom(string sourceFile)
        {
            var master = Read(MasterPath);
            var incoming = Read(sourceFile);
            if (incoming.Count == 0) return 0;

            foreach (var inc in incoming)
            {
                var existing = master.FirstOrDefault(e =>
                    string.Equals(e.Domain, inc.Domain, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(e.Path, inc.Path, StringComparison.OrdinalIgnoreCase) &&
                    e.Name == inc.Name);
                if (existing != null)
                {
                    existing.Value = inc.Value;
                    existing.Expires = inc.Expires;
                    existing.Secure = inc.Secure;
                    existing.IncludeSub = inc.IncludeSub;
                }
                else
                {
                    master.Add(inc);
                }
            }

            Write(MasterPath, master);
            return incoming.Count;
        }



        /// <summary>Group .x.com / x.com / www.x.com into one site row.</summary>
        public static string Normalize(string domain)
        {
            if (string.IsNullOrEmpty(domain)) return "";
            string d = domain.Trim().TrimStart('.').ToLowerInvariant();
            if (d.StartsWith("www.")) d = d.Substring(4);
            return d;
        }

        /// <summary>Site to cookie count, sorted by count.</summary>
        public static List<KeyValuePair<string, int>> Domains()
        {
            return Read(MasterPath)
                .GroupBy(e => Normalize(e.Domain), StringComparer.OrdinalIgnoreCase)
                .Select(g => new KeyValuePair<string, int>(g.Key, g.Count()))
                .OrderByDescending(kv => kv.Value)
                .ToList();
        }

        public static void RemoveDomain(string domain)
        {
            string key = Normalize(domain);
            var keep = Read(MasterPath)
                .Where(e => !string.Equals(Normalize(e.Domain), key, StringComparison.OrdinalIgnoreCase))
                .ToList();
            Write(MasterPath, keep);
        }

        public static void ClearAll()
        {
            try { if (File.Exists(MasterPath)) File.Delete(MasterPath); } catch { }
        }

        public static string Summary()
        {
            var domains = Domains();
            if (domains.Count == 0) return "未导入";
            var names = domains.Take(3).Select(d => d.Key.TrimStart('.')).ToList();
            string text = string.Join("、", names);
            if (domains.Count > 3) text += " 等";
            return domains.Count + " 个站点：" + text;
        }
    }
}
