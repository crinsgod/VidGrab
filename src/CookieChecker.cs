using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace VidGrab
{
    internal sealed class CookieCheckResult
    {
        public string Status;
        public string Detail;
    }

    /// <summary>
    /// Best-effort validity check: local expiry first, then a per-platform
    /// "am I logged in?" request using the imported cookie header.
    /// </summary>
    internal static class CookieChecker
    {
        private const string UserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36";

        public static CookieCheckResult Check(string domain, List<CookieEntry> entries)
        {
            if (entries == null || entries.Count == 0)
                return new CookieCheckResult { Status = "无 Cookie", Detail = "" };

            int expired = 0, live = 0;
            bool hasSession = false;
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            foreach (var e in entries)
            {
                long exp;
                if (string.IsNullOrEmpty(e.Expires) || e.Expires == "0" ||
                    !long.TryParse(e.Expires, NumberStyles.Any, CultureInfo.InvariantCulture, out exp))
                {
                    hasSession = true;
                    continue;
                }
                if (exp <= now) expired++;
                else live++;
            }

            if (live == 0 && !hasSession)
                return new CookieCheckResult { Status = "已过期", Detail = "全部 Cookie 的过期时间都已到" };

            string cookieHeader = string.Join("; ", entries.Select(e => e.Name + "=" + e.Value));
            string d = domain.TrimStart('.').ToLowerInvariant();

            try
            {
                if (d.Contains("instagram")) return CheckInstagram(cookieHeader);
                if (d.Contains("youtube") || d.Contains("google")) return CheckYouTube(cookieHeader);
                if (d.Contains("x.com") || d.Contains("twitter")) return CheckX(cookieHeader);
                if (d.Contains("douyin")) return new CookieCheckResult
                {
                    Status = "本地通过",
                    Detail = "抖音直链下载不需要登录态，按过期时间判断即可"
                };
            }
            catch (Exception ex)
            {
                return new CookieCheckResult { Status = "无法判断", Detail = ex.GetBaseException().Message };
            }

            string note = live > 0 ? "未做过期以外的在线验证" : "仅会话 Cookie";
            return new CookieCheckResult { Status = "本地通过", Detail = note };
        }

        private static HttpClient NewHttp()
        {
            try { System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12; }
            catch { }
            var handler = new HttpClientHandler { AllowAutoRedirect = true, UseCookies = false };
            var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
            http.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
            return http;
        }

        /// <summary>.NET Framework chokes on charset="utf-8" (quoted); decode bytes ourselves.</summary>
        private static string Body(HttpResponseMessage resp)
        {
            byte[] bytes = resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            return Encoding.UTF8.GetString(bytes);
        }

        private static CookieCheckResult CheckInstagram(string cookieHeader)
        {
            using (var http = NewHttp())
            using (var req = new HttpRequestMessage(HttpMethod.Get,
                       "https://www.instagram.com/api/v1/accounts/current_user/?edit=true"))
            {
                req.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
                req.Headers.TryAddWithoutValidation("x-ig-app-id", "936619743392459");
                req.Headers.TryAddWithoutValidation("Referer", "https://www.instagram.com/");

                using (var resp = http.SendAsync(req).GetAwaiter().GetResult())
                {
                    string body = Body(resp);
                    if (resp.IsSuccessStatusCode && body.Contains("\"user\"") && body.Contains("username"))
                        return new CookieCheckResult { Status = "有效", Detail = "Instagram 登录态正常" };
                    if (body.Contains("\"user\":null") || body.Contains("\"message\":\"login_required\""))
                        return new CookieCheckResult { Status = "未登录/已失效", Detail = "Instagram 返回未登录，请重新导出该站点 Cookie" };
                    return new CookieCheckResult { Status = "未登录/已失效", Detail = "Instagram 返回 HTTP " + (int)resp.StatusCode };
                }
            }
        }

        private static CookieCheckResult CheckYouTube(string cookieHeader)
        {
            using (var http = NewHttp())
            using (var req = new HttpRequestMessage(HttpMethod.Get, "https://www.youtube.com/feed/subscriptions"))
            {
                req.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
                using (var resp = http.SendAsync(req).GetAwaiter().GetResult())
                {
                    string body = Body(resp);
                    if (resp.IsSuccessStatusCode && body.Contains("\"LOGGED_IN\":true"))
                        return new CookieCheckResult { Status = "有效", Detail = "YouTube 登录态正常" };
                    if (body.Contains("accounts.google.com") || body.Contains("\"LOGGED_IN\":false"))
                        return new CookieCheckResult { Status = "未登录/已失效", Detail = "YouTube 返回未登录，请重新导出该站点 Cookie" };
                    return new CookieCheckResult { Status = "无法判断", Detail = "YouTube 页面结构未匹配（可能是同意页）" };
                }
            }
        }

        private static CookieCheckResult CheckX(string cookieHeader)
        {
            using (var http = NewHttp())
            using (var req = new HttpRequestMessage(HttpMethod.Get, "https://x.com/home"))
            {
                req.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
                using (var resp = http.SendAsync(req).GetAwaiter().GetResult())
                {
                    string body = Body(resp);
                    string finalUrl = resp.RequestMessage != null && resp.RequestMessage.RequestUri != null
                        ? resp.RequestMessage.RequestUri.ToString() : "";

                    if (body.Contains("\"isLoggedIn\":true") &&
                        finalUrl.IndexOf("onboarding", StringComparison.OrdinalIgnoreCase) < 0 &&
                        finalUrl.IndexOf("mode=login", StringComparison.OrdinalIgnoreCase) < 0)
                        return new CookieCheckResult { Status = "有效", Detail = "X 登录态正常" };

                    if (finalUrl.IndexOf("onboarding", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        finalUrl.IndexOf("login", StringComparison.OrdinalIgnoreCase) >= 0)
                        return new CookieCheckResult { Status = "未登录/已失效", Detail = "X 跳转到了登录页，请重新导出该站点 Cookie" };

                    return new CookieCheckResult { Status = "无法判断", Detail = "X 页面未包含登录标记" };
                }
            }
        }
    }
}
