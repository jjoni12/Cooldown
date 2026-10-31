using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace LimitBar
{
    static class Http
    {
        static readonly HttpClient Client = CreateClient();

        static HttpClient CreateClient()
        {
            // Without this, HttpClient drops a Cookie header set on the request (Cursor needs one).
            var client = new HttpClient(new HttpClientHandler { UseCookies = false }) { Timeout = TimeSpan.FromSeconds(20) };
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"LimitBar/{version.Major}.{version.Minor}.{version.Build}");
            return client;
        }

        static readonly TimeSpan DefaultRetryWait = TimeSpan.FromSeconds(5);
        static readonly TimeSpan MaxRetryWait = TimeSpan.FromSeconds(30);

        public static Task<(int Status, string Body)> GetAsync(string url, IDictionary<string, string> headers, CancellationToken ct) =>
            RequestAsync(HttpMethod.Get, url, headers, null, ct);

        public static Task<(int Status, string Body)> PostJsonAsync(string url, IDictionary<string, string> headers, string json, CancellationToken ct) =>
            RequestAsync(HttpMethod.Post, url, headers, json, ct);

        static async Task<(int Status, string Body)> RequestAsync(HttpMethod method, string url, IDictionary<string, string> headers, string json, CancellationToken ct)
        {
            var (status, body, retryAfter) = await SendAsync(method, url, headers, json, ct).ConfigureAwait(false);
            if (status != 429) return (status, body);

            var wait = retryAfter ?? DefaultRetryWait;
            if (wait < TimeSpan.FromSeconds(1)) wait = TimeSpan.FromSeconds(1);
            if (wait > MaxRetryWait) wait = MaxRetryWait;
            await Task.Delay(wait, ct).ConfigureAwait(false);
            (status, body, _) = await SendAsync(method, url, headers, json, ct).ConfigureAwait(false);
            return (status, body);
        }

        static async Task<(int Status, string Body, TimeSpan? RetryAfter)> SendAsync(HttpMethod method, string url, IDictionary<string, string> headers, string json, CancellationToken ct)
        {
            using (var request = new HttpRequestMessage(method, url))
            {
                foreach (var h in headers) request.Headers.TryAddWithoutValidation(h.Key, h.Value);
                if (json != null) request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                using (var response = await Client.SendAsync(request, ct).ConfigureAwait(false))
                {
                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var retry = response.Headers.RetryAfter;
                    var retryAfter = retry?.Delta ?? (retry?.Date.HasValue == true ? retry.Date.Value - DateTimeOffset.UtcNow : (TimeSpan?)null);
                    return ((int)response.StatusCode, body, retryAfter);
                }
            }
        }
    }
}
