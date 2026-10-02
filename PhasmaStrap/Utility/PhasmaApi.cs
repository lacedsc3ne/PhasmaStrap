using System.Net.Http;

namespace PhasmaStrap.Utility
{
    /// <summary>
    /// Small JSON client for api.phasmastrap.com. Every call fails soft: on any error it logs and returns null,
    /// so pages can simply hide what the server didn't answer.
    /// </summary>
    public static class PhasmaApi
    {
        private const string LOG_IDENT = "PhasmaApi";

        private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

        private static readonly Dictionary<string, (DateTime At, string Body)> _cache = new();
        private static readonly object _cacheLock = new();

        public static Task<T?> GetAsync<T>(string path, TimeSpan? cacheFor = null, bool signedInOnly = false) where T : class
            => SendAsync<T>(HttpMethod.Get, path, null, cacheFor, signedInOnly);

        public static Task<T?> PostAsync<T>(string path, object? body = null, bool signedInOnly = true) where T : class
            => SendAsync<T>(HttpMethod.Post, path, body, null, signedInOnly);

        /// <summary>For calls whose answer doesn't matter beyond success.</summary>
        public static async Task<bool> PostAsync(string path, object? body = null, bool signedInOnly = true)
            => await SendAsync<JsonElementBox>(HttpMethod.Post, path, body, null, signedInOnly, allowEmpty: true) is not null;

        private sealed class JsonElementBox { }

        private static async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body, TimeSpan? cacheFor, bool signedInOnly, bool allowEmpty = false) where T : class
        {
            if (signedInOnly && !PhasmaAccount.SignedIn)
                return null;

            string key = method + " " + path;

            if (cacheFor is not null)
            {
                lock (_cacheLock)
                {
                    if (_cache.TryGetValue(key, out var hit) && DateTime.UtcNow - hit.At < cacheFor.Value)
                        return Parse<T>(hit.Body, allowEmpty);
                }
            }

            try
            {
                using var request = new HttpRequestMessage(method, $"{App.ServerBase}{path}");
                PhasmaAccount.Authorize(request);

                if (body is not null)
                    request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using HttpResponseMessage response = await App.HttpClient.SendAsync(request, timeout.Token);
                string text = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"{key} answered {(int)response.StatusCode}");
                    return null;
                }

                if (cacheFor is not null)
                {
                    lock (_cacheLock)
                        _cache[key] = (DateTime.UtcNow, text);
                }

                return Parse<T>(text, allowEmpty);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"{key} failed: {ex.Message}");
                return null;
            }
        }

        private static T? Parse<T>(string text, bool allowEmpty) where T : class
        {
            if (string.IsNullOrWhiteSpace(text))
                return allowEmpty ? Activator.CreateInstance(typeof(T), true) as T : null;

            try
            {
                return JsonSerializer.Deserialize<T>(text, Options);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not read the answer: {ex.Message}");
                return allowEmpty ? Activator.CreateInstance(typeof(T), true) as T : null;
            }
        }
    }
}
