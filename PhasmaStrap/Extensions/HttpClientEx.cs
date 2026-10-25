using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PhasmaStrap.Extensions
{
    internal static class HttpClientEx
    {
        public static async Task<HttpResponseMessage> GetWithRetriesAsync(this HttpClient client, string url, int retries, CancellationToken token)
        {
            for (int i = 1; i <= retries; i++)
            {
                try
                {
                    return await client.GetAsync(url, token);
                }
                catch (TaskCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    App.Logger.WriteException("HttpClientEx::GetWithRetriesAsync", ex);

                    if (i == retries)
                        throw;
                }
            }

            throw new ArgumentOutOfRangeException(nameof(retries), retries, "Retries must be at least 1");
        }

        public static async Task<HttpResponseMessage> PostWithRetriesAsync(this HttpClient client, string url, HttpContent? content, int retries, CancellationToken token)
        {
            for (int i = 1; i <= retries; i++)
            {
                try
                {
                    return await client.PostAsync(url, content, token);
                }
                catch (TaskCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    App.Logger.WriteException("HttpClientEx::PostWithRetriesAsync", ex);

                    if (i == retries)
                        throw;
                }
            }

            throw new ArgumentOutOfRangeException(nameof(retries), retries, "Retries must be at least 1");
        }

        public static async Task<T?> GetFromJsonWithRetriesAsync<T>(this HttpClient client, string url, int retries, CancellationToken token) where T : class
        {
            using HttpResponseMessage response = await GetWithRetriesAsync(client, url, retries, token);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(token);
            return await JsonSerializer.DeserializeAsync<T>(stream, cancellationToken: token);
        }

        public static async Task<T?> PostFromJsonWithRetriesAsync<T>(this HttpClient client, string url, HttpContent? content, int retries, CancellationToken token) where T : class
        {
            using HttpResponseMessage response = await PostWithRetriesAsync(client, url, content, retries, token);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(token);
            return await JsonSerializer.DeserializeAsync<T>(stream, cancellationToken: token);
        }
    }
}
