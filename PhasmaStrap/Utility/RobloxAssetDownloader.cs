using PhasmaStrap.Integrations;

namespace PhasmaStrap.Utility
{
    public sealed record AssetDownloadResult(bool Success, byte[]? Bytes, string? Error);

    public static class RobloxAssetDownloader
    {
        private const int MaxAssetBytes = 200 * 1024 * 1024;

        public static async Task<AssetDownloadResult> DownloadAssetAsync(long assetId, CancellationToken token = default)
        {
            using var client = new HttpClient(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };

            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, $"https://assetdelivery.roblox.com/v1/asset/?id={assetId}");
                req.Headers.TryAddWithoutValidation("User-Agent", $"{App.ProjectName}/{App.Version}");

                string? cookie = RobloxCookie.Get();
                if (!string.IsNullOrEmpty(cookie))
                    req.Headers.TryAddWithoutValidation("Cookie", $".ROBLOSECURITY={cookie}");

                HttpResponseMessage res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token);

                for (int hop = 0; hop < 5 && (int)res.StatusCode >= 300 && (int)res.StatusCode < 400 && res.Headers.Location is not null; hop++)
                {
                    Uri next = res.Headers.Location.IsAbsoluteUri ? res.Headers.Location : new Uri(req.RequestUri!, res.Headers.Location);
                    res.Dispose();

                    if (next.Scheme != Uri.UriSchemeHttps)
                    {
                        App.Logger.WriteLine("RobloxAssetDownloader", $"Asset {assetId} redirected to a non secure address, stopping");
                        return new AssetDownloadResult(false, null, "Roblox sent the download to an address that is not secure.");
                    }

                    using var follow = new HttpRequestMessage(HttpMethod.Get, next);
                    follow.Headers.TryAddWithoutValidation("User-Agent", $"{App.ProjectName}/{App.Version}");
                    res = await client.SendAsync(follow, HttpCompletionOption.ResponseHeadersRead, token);
                }

                using HttpResponseMessage final = res;

                if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    return new AssetDownloadResult(false, null, "This asset needs an account that owns/can access it - sign into Roblox first.");

                if (res.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return new AssetDownloadResult(false, null, "No asset exists with that ID.");

                if (!res.IsSuccessStatusCode)
                    return new AssetDownloadResult(false, null, $"Roblox returned {(int)res.StatusCode} {res.StatusCode}.");

                byte[] bytes = await Http.ReadBytesBoundedAsync(res.Content, MaxAssetBytes, token);
                return bytes.Length > 0
                    ? new AssetDownloadResult(true, bytes, null)
                    : new AssetDownloadResult(false, null, "Roblox returned an empty response.");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("RobloxAssetDownloader", $"Failed to download asset {assetId}: {ex.Message}");
                return new AssetDownloadResult(false, null, ex.Message);
            }
        }
    }
}
