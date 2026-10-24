using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace VidGrab
{
    /// <summary>
    /// Direct (non-yt-dlp) downloader: HTTP range chunks in parallel, resumable
    /// through a .part + state file, per-chunk retry, and a single-stream fallback
    /// when the server ignores Range.
    /// </summary>
    internal static class DirectDownloader
    {
        public static string ProxyUrl = "";
        public static int Threads = 4;

        private const int ChunkSize = 1024 * 1024;      // 1 MB, same order as lux
        private const int MaxRetries = 3;

        private sealed class PartState
        {
            public long Length;
            public int ChunkSize;
            public List<int> Done = new List<int>();
        }

        public static async Task DownloadAsync(string url, string targetPath, string referer, string userAgent,
            Action<int, string> progress, Action<string> status, CancellationToken token)
        {
            string partPath = targetPath + ".part";
            string statePath = targetPath + ".part.json";

            using (var http = NewHttp())
            {
                var probe = await ProbeAsync(http, url, referer, userAgent, token);
                bool rangeOk = probe.RangeOk;
                long length = probe.Length;

                if (!rangeOk || length <= 0 || Threads <= 1)
                {
                    await SingleStreamAsync(http, url, targetPath, partPath, statePath, referer, userAgent,
                        rangeOk, length, progress, status, token);
                }
                else
                {
                    await MultiThreadAsync(http, url, targetPath, partPath, statePath, referer, userAgent,
                        length, progress, status, token);
                }
            }
        }

        private static HttpClient NewHttp()
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; }
            catch { }

            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                UseCookies = false,
                AutomaticDecompression = DecompressionMethods.None
            };
            if (!string.IsNullOrWhiteSpace(ProxyUrl))
            {
                try { handler.Proxy = new WebProxy(ProxyUrl); handler.UseProxy = true; } catch { }
            }

            var http = new HttpClient(handler) { Timeout = TimeSpan.FromHours(3) };
            http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");
            return http;
        }

        private sealed class ProbeResult
        {
            public bool RangeOk;
            public long Length;
        }

        private static async Task<ProbeResult> ProbeAsync(HttpClient http, string url, string referer, string userAgent,
            CancellationToken token)
        {
            var result = new ProbeResult();
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    req.Headers.TryAddWithoutValidation("Range", "bytes=0-0");
                    AddCommonHeaders(req, referer, userAgent);

                    using (var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token))
                    {
                        if ((int)resp.StatusCode == 206)
                        {
                            var range = resp.Content.Headers.ContentRange;
                            if (range != null && range.Length.HasValue)
                            {
                                result.Length = range.Length.Value;
                                result.RangeOk = true;
                                return result;
                            }
                        }
                        if (resp.Content.Headers.ContentLength.HasValue)
                            result.Length = resp.Content.Headers.ContentLength.Value;
                        return result;
                    }
                }
            }
            catch
            {
                return result;
            }
        }

        private static void AddCommonHeaders(HttpRequestMessage req, string referer, string userAgent)
        {
            if (!string.IsNullOrEmpty(userAgent)) req.Headers.TryAddWithoutValidation("User-Agent", userAgent);
            if (!string.IsNullOrEmpty(referer)) req.Headers.TryAddWithoutValidation("Referer", referer);
        }

        private static async Task MultiThreadAsync(HttpClient http, string url, string targetPath,
            string partPath, string statePath, string referer, string userAgent,
            long length, Action<int, string> progress, Action<string> status, CancellationToken token)
        {
            var state = LoadState(statePath, length);
            if (state == null)
            {
                TryDelete(partPath);
                state = new PartState { Length = length, ChunkSize = ChunkSize };
            }

            if (!File.Exists(partPath) || new FileInfo(partPath).Length != length)
            {
                using (var fs = File.Create(partPath)) fs.SetLength(length);
                state.Done.Clear();
            }

            int totalChunks = (int)((length + state.ChunkSize - 1) / state.ChunkSize);
            var done = new HashSet<int>(state.Done);
            long completedBytes = done.Sum(i => ChunkLength(state, i, length));

            if (status != null)
                status(string.Format("多线程下载：{0} 线程 · {1}/{2} 块已存在", Threads, done.Count, totalChunks));

            var gate = new SemaphoreSlim(Threads);
            var tasks = new List<Task>();
            var errors = new List<Exception>();
            var sync = new object();
            var sw = Stopwatch.StartNew();
            long lastReport = 0;

            for (int i = 0; i < totalChunks; i++)
            {
                if (done.Contains(i)) continue;

                int index = i;
                await gate.WaitAsync(token);
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        long start = (long)index * state.ChunkSize;
                        long end = Math.Min(start + state.ChunkSize, length) - 1;
                        long bytes = await DownloadChunkAsync(http, url, partPath, referer, userAgent,
                            start, end, token);

                        lock (sync)
                        {
                            completedBytes += bytes;
                            lock (state) state.Done.Add(index);
                            SaveState(statePath, state);

                            long now = sw.ElapsedMilliseconds;
                            if (now - lastReport > 250 || completedBytes >= length)
                            {
                                lastReport = now;
                                double speed = completedBytes / 1048576.0 / Math.Max(sw.Elapsed.TotalSeconds, 0.001);
                                int pct = (int)(completedBytes * 100 / length);
                                if (progress != null)
                                    progress(pct, string.Format("下载中 {0}% · {1:0.0}/{2:0.0} MB · {3:0.0} MB/s",
                                        pct, completedBytes / 1048576.0, length / 1048576.0, speed));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (sync) errors.Add(ex);
                    }
                    finally
                    {
                        gate.Release();
                    }
                }, token));
            }

            try { await Task.WhenAll(tasks); }
            catch { }

            if (token.IsCancellationRequested) throw new OperationCanceledException();
            if (errors.Count > 0) throw errors[0];
            if (state.Done.Count < totalChunks) throw new Exception("部分分片下载失败，已保留临时文件可续传");

            TryDelete(statePath);
            if (File.Exists(targetPath)) File.Delete(targetPath);
            File.Move(partPath, targetPath);
            if (progress != null) progress(100, "下载完成");
        }

        private static long ChunkLength(PartState state, int index, long length)
        {
            long start = (long)index * state.ChunkSize;
            return Math.Min(state.ChunkSize, length - start);
        }

        private static async Task<long> DownloadChunkAsync(HttpClient http, string url, string partPath,
            string referer, string userAgent, long start, long end, CancellationToken token)
        {
            Exception last = null;
            for (int attempt = 0; attempt < MaxRetries; attempt++)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                    {
                        req.Headers.TryAddWithoutValidation("Range", string.Format("bytes={0}-{1}", start, end));
                        AddCommonHeaders(req, referer, userAgent);

                        using (var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token))
                        {
                            if ((int)resp.StatusCode != 206)
                                throw new Exception("服务器未返回分片（HTTP " + (int)resp.StatusCode + "）");

                            byte[] buffer = await resp.Content.ReadAsByteArrayAsync();
                            using (var fs = new FileStream(partPath, FileMode.Open, FileAccess.Write,
                                       FileShare.ReadWrite, 4096, FileOptions.RandomAccess))
                            {
                                fs.Seek(start, SeekOrigin.Begin);
                                await fs.WriteAsync(buffer, 0, buffer.Length, token);
                            }
                            return buffer.Length;
                        }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    last = ex;
                }

                if (attempt < MaxRetries - 1)
                    await Task.Delay(400 * (attempt + 1), token);
            }
            throw last ?? new Exception("分片下载失败");
        }

        private static async Task SingleStreamAsync(HttpClient http, string url, string targetPath,
            string partPath, string statePath, string referer, string userAgent,
            bool rangeOk, long length, Action<int, string> progress, Action<string> status, CancellationToken token)
        {
            long existing = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;
            if (!rangeOk && existing > 0) { TryDelete(partPath); existing = 0; }
            if (length > 0 && existing >= length && length > 0)
            {
                TryDelete(statePath);
                if (File.Exists(targetPath)) File.Delete(targetPath);
                File.Move(partPath, targetPath);
                return;
            }

            if (status != null) status(rangeOk ? "单线程下载（支持续传）" : "单线程下载（服务器不支持分片）");

            using (var req = new HttpRequestMessage(HttpMethod.Get, url))
            {
                if (rangeOk && existing > 0)
                    req.Headers.TryAddWithoutValidation("Range", "bytes=" + existing + "-");
                AddCommonHeaders(req, referer, userAgent);

                using (var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token))
                {
                    if (!resp.IsSuccessStatusCode)
                        throw new Exception("下载失败：HTTP " + (int)resp.StatusCode);

                    using (var src = await resp.Content.ReadAsStreamAsync())
                    using (var dst = new FileStream(partPath, FileMode.Append, FileAccess.Write, FileShare.Read))
                    {
                        var buffer = new byte[81920];
                        long read = existing;
                        var sw = Stopwatch.StartNew();
                        long lastTick = 0;

                        while (true)
                        {
                            token.ThrowIfCancellationRequested();
                            int n = await src.ReadAsync(buffer, 0, buffer.Length, token);
                            if (n <= 0) break;
                            await dst.WriteAsync(buffer, 0, n, token);
                            read += n;

                            long now = sw.ElapsedMilliseconds;
                            if (now - lastTick > 300)
                            {
                                lastTick = now;
                                double speed = n / 1048576.0 / Math.Max((now - (lastTick - 300)) / 1000.0, 0.001);
                                int pct = length > 0 ? (int)(read * 100 / length) : 0;
                                if (progress != null)
                                    progress(pct, string.Format("下载中 {0}% · {1:0.0} MB · {2:0.0} MB/s",
                                        pct, read / 1048576.0, speed));
                            }
                        }
                    }
                }
            }

            TryDelete(statePath);
            if (File.Exists(targetPath)) File.Delete(targetPath);
            File.Move(partPath, targetPath);
            if (progress != null) progress(100, "下载完成");
        }

        private static PartState LoadState(string statePath, long length)
        {
            try
            {
                if (!File.Exists(statePath)) return null;
                var state = new JavaScriptSerializer().Deserialize<PartState>(File.ReadAllText(statePath));
                if (state == null || state.Length != length || state.ChunkSize <= 0) return null;
                return state;
            }
            catch { return null; }
        }

        private static void SaveState(string statePath, PartState state)
        {
            try
            {
                lock (state)
                {
                    File.WriteAllText(statePath, new JavaScriptSerializer().Serialize(state));
                }
            }
            catch { }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
