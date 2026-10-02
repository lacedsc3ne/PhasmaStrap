using System.Net;
using System.Net.Http;

namespace PhasmaStrap.Utility
{
    public static class RobloxQuickSignIn
    {
        private const string LOG_IDENT = "RobloxQuickSignIn";

        private const string CreateUrl = "https://apis.roblox.com/auth-token-service/v1/login/create";
        private const string StatusUrl = "https://apis.roblox.com/auth-token-service/v1/login/status";
        private const string LoginUrl = "https://auth.roblox.com/v2/login";

        public sealed class Ticket
        {
            public string Code { get; init; } = "";
            public string PrivateKey { get; init; } = "";
            public DateTime ExpiresUtc { get; init; }
        }

        public sealed class Progress
        {
            public string Status { get; init; } = "";
            public string AccountName { get; init; } = "";

            public bool Validated => Status == "Validated";
            public bool Cancelled => Status == "Cancelled";
        }

        private static readonly HttpClient Plain = new();

        public static async Task<Ticket> CreateAsync(CancellationToken token)
        {
            using HttpResponseMessage response = await PostAsync(Plain, CreateUrl, "{}", token);
            response.EnsureSuccessStatusCode();

            using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            JsonElement root = document.RootElement;

            DateTime expires = root.TryGetProperty("expirationTime", out JsonElement when) && when.TryGetDateTime(out DateTime parsed)
                ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
                : DateTime.UtcNow.AddMinutes(4);

            App.Logger.WriteLine(LOG_IDENT, $"Roblox issued a sign-in code, good until {expires:T} UTC");

            return new Ticket
            {
                Code = root.GetProperty("code").GetString() ?? "",
                PrivateKey = root.GetProperty("privateKey").GetString() ?? "",
                ExpiresUtc = expires,
            };
        }

        public static async Task<Progress> StatusAsync(Ticket ticket, CancellationToken token)
        {
            string body = JsonSerializer.Serialize(new { code = ticket.Code, privateKey = ticket.PrivateKey });

            using HttpResponseMessage response = await PostAsync(Plain, StatusUrl, body, token);

            if (!response.IsSuccessStatusCode)
                return new Progress { Status = $"Roblox answered {(int)response.StatusCode}" };

            using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            JsonElement root = document.RootElement;

            return new Progress
            {
                Status = root.TryGetProperty("status", out JsonElement status) ? status.GetString() ?? "" : "",
                AccountName = root.TryGetProperty("accountName", out JsonElement name) && name.ValueKind == JsonValueKind.String ? name.GetString() ?? "" : "",
            };
        }

        public static async Task<string?> RedeemAsync(Ticket ticket, CancellationToken token)
        {
            using var handler = new HttpClientHandler { CookieContainer = new CookieContainer(), UseCookies = true };
            using var client = new HttpClient(handler);

            string body = JsonSerializer.Serialize(new { ctype = "AuthToken", cvalue = ticket.Code, password = ticket.PrivateKey });

            using HttpResponseMessage response = await PostAsync(client, LoginUrl, body, token);

            if (!response.IsSuccessStatusCode)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Roblox refused the sign-in ({(int)response.StatusCode})");
                return null;
            }

            string? cookie = handler.CookieContainer.GetCookies(new Uri("https://www.roblox.com"))[".ROBLOSECURITY"]?.Value;

            App.Logger.WriteLine(LOG_IDENT, string.IsNullOrEmpty(cookie) ? "Signed in, but Roblox sent no session" : "Signed in and received a session");

            return string.IsNullOrEmpty(cookie) ? null : cookie;
        }

        private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string url, string json, CancellationToken token)
        {
            HttpResponseMessage response = await client.SendAsync(Build(url, json, null), token);

            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.BadRequest
                && response.Headers.TryGetValues("x-csrf-token", out IEnumerable<string>? values))
            {
                string csrf = values.First();
                response.Dispose();
                response = await client.SendAsync(Build(url, json, csrf), token);
            }

            return response;
        }

        private static HttpRequestMessage Build(string url, string json, string? csrf)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };

            if (csrf is not null)
                request.Headers.TryAddWithoutValidation("x-csrf-token", csrf);

            return request;
        }
    }
}
