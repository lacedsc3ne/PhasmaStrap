namespace PhasmaStrap.Integrations
{
    public sealed record GameFacts(
        DateTime? Created,
        string Genre,
        string AvatarType,
        bool CopyingAllowed,
        long Price,
        bool PrivateServersAllowed,
        long PrivateServerPrice,
        string CreatorType,
        bool CreatorVerified,
        long Likes,
        long Dislikes);

    public sealed record AgeInfo(string Rating, string Descriptors);

    public sealed record GamePassInfo(long Id, string Name, long Price, bool OnSale, string IconUrl);

    public sealed record BadgeInfo(long Id, string Name, string Description, bool Enabled, long Awarded, double WinRate, string IconUrl);

    public sealed record MediaInfo(bool IsVideo, string Title, string ImageUrl);

    /// <summary>
    /// Extra read only facts for the game page: release details, votes, game passes, badges and media.
    /// Everything fails soft and returns empty results.
    /// </summary>
    public static class GameExtras
    {
        private const string LOG_IDENT = "GameExtras";

        public static async Task<GameFacts?> GetFactsAsync(long universeId, CancellationToken ct = default)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(await App.HttpClient.GetStringAsync($"https://games.roblox.com/v1/games?universeIds={universeId}", ct));
                if (!doc.RootElement.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0)
                    return null;

                JsonElement game = data[0];

                DateTime? created = null;
                if (game.TryGetProperty("created", out JsonElement c) && c.ValueKind == JsonValueKind.String
                    && DateTime.TryParse(c.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out DateTime parsed))
                    created = parsed;

                string creatorType = "";
                bool verified = false;
                if (game.TryGetProperty("creator", out JsonElement creator))
                {
                    creatorType = Text(creator, "type");
                    verified = creator.TryGetProperty("hasVerifiedBadge", out JsonElement v) && v.ValueKind == JsonValueKind.True;
                }

                long likes = 0, dislikes = 0;
                try
                {
                    using JsonDocument votes = JsonDocument.Parse(await App.HttpClient.GetStringAsync($"https://games.roblox.com/v1/games/votes?universeIds={universeId}", ct));
                    if (votes.RootElement.TryGetProperty("data", out JsonElement vd) && vd.ValueKind == JsonValueKind.Array && vd.GetArrayLength() > 0)
                    {
                        likes = Long(vd[0], "upVotes");
                        dislikes = Long(vd[0], "downVotes");
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Votes lookup failed: {ex.Message}");
                }

                return new GameFacts(
                    created,
                    Text(game, "genre"),
                    Text(game, "universeAvatarType"),
                    game.TryGetProperty("copyingAllowed", out JsonElement copy) && copy.ValueKind == JsonValueKind.True,
                    Long(game, "price"),
                    game.TryGetProperty("createVipServersAllowed", out JsonElement vip) && vip.ValueKind == JsonValueKind.True,
                    Long(game, "vipServerPrice"),
                    creatorType,
                    verified,
                    likes,
                    dislikes);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Facts lookup failed: {ex.Message}");
                return null;
            }
        }

        public static async Task<AgeInfo?> GetAgeAsync(long universeId, CancellationToken ct = default)
        {
            try
            {
                using var body = new StringContent($"{{\"universeId\":{universeId}}}", System.Text.Encoding.UTF8, "application/json");
                using HttpResponseMessage response = await App.HttpClient.PostAsync("https://apis.roblox.com/experience-guidelines-api/experience-guidelines/get-age-recommendation", body, ct);
                if (!response.IsSuccessStatusCode)
                    return null;

                using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                if (!doc.RootElement.TryGetProperty("ageRecommendationDetails", out JsonElement details))
                    return null;

                string rating = details.TryGetProperty("summary", out JsonElement s) && s.TryGetProperty("ageRecommendation", out JsonElement r) ? Text(r, "displayName") : "";
                var names = new List<string>();
                if (details.TryGetProperty("descriptorUsages", out JsonElement usages) && usages.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement usage in usages.EnumerateArray())
                        if (usage.TryGetProperty("descriptor", out JsonElement d) && Text(d, "displayName") is { Length: > 0 } name)
                            names.Add(name);

                return rating.Length == 0 ? null : new AgeInfo(rating, string.Join(", ", names));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Age rating lookup failed: {ex.Message}");
                return null;
            }
        }

        public static async Task<List<GamePassInfo>> GetPassesAsync(long universeId, CancellationToken ct = default)
        {
            var list = new List<GamePassInfo>();

            try
            {
                using JsonDocument doc = JsonDocument.Parse(await App.HttpClient.GetStringAsync($"https://apis.roblox.com/game-passes/v1/universes/{universeId}/game-passes?passView=Full&pageSize=100", ct));
                if (!doc.RootElement.TryGetProperty("gamePasses", out JsonElement data) || data.ValueKind != JsonValueKind.Array)
                    return list;

                foreach (JsonElement pass in data.EnumerateArray())
                {
                    long id = Long(pass, "id");
                    if (id <= 0)
                        continue;

                    bool forSale = pass.TryGetProperty("isForSale", out JsonElement s) && s.ValueKind == JsonValueKind.True;
                    string name = Text(pass, "displayName");
                    list.Add(new GamePassInfo(id, name.Length > 0 ? name : Text(pass, "name"), Long(pass, "price"), forSale, ""));
                }

                Dictionary<long, string> icons = await ThumbnailsAsync("game-passes", "gamePassIds", list.Select(x => x.Id), ct);
                for (int i = 0; i < list.Count; i++)
                    if (icons.TryGetValue(list[i].Id, out string? url))
                        list[i] = list[i] with { IconUrl = url };
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Game passes lookup failed: {ex.Message}");
            }

            return list;
        }

        public static async Task<List<BadgeInfo>> GetBadgesAsync(long universeId, CancellationToken ct = default)
        {
            var list = new List<BadgeInfo>();

            try
            {
                using JsonDocument doc = JsonDocument.Parse(await App.HttpClient.GetStringAsync($"https://badges.roblox.com/v1/universes/{universeId}/badges?limit=100&sortOrder=Asc", ct));
                if (!doc.RootElement.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Array)
                    return list;

                foreach (JsonElement badge in data.EnumerateArray())
                {
                    long id = Long(badge, "id");
                    if (id <= 0)
                        continue;

                    long awarded = 0;
                    double win = 0;
                    if (badge.TryGetProperty("statistics", out JsonElement stats))
                    {
                        awarded = Long(stats, "awardedCount");
                        if (stats.TryGetProperty("winRatePercentage", out JsonElement w) && w.ValueKind == JsonValueKind.Number)
                            win = w.GetDouble() * 100;
                    }

                    list.Add(new BadgeInfo(id, Text(badge, "displayName") is { Length: > 0 } shown ? shown : Text(badge, "name"), Text(badge, "displayDescription") is { Length: > 0 } desc ? desc : Text(badge, "description"),
                        !badge.TryGetProperty("enabled", out JsonElement en) || en.ValueKind != JsonValueKind.False, awarded, win, ""));
                }

                Dictionary<long, string> icons = await ThumbnailsAsync("badges/icons", "badgeIds", list.Select(x => x.Id), ct);
                for (int i = 0; i < list.Count; i++)
                    if (icons.TryGetValue(list[i].Id, out string? url))
                        list[i] = list[i] with { IconUrl = url };
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Badges lookup failed: {ex.Message}");
            }

            return list;
        }

        public static async Task<List<MediaInfo>> GetMediaAsync(long universeId, CancellationToken ct = default)
        {
            var list = new List<MediaInfo>();

            try
            {
                using JsonDocument doc = JsonDocument.Parse(await App.HttpClient.GetStringAsync($"https://games.roblox.com/v2/games/{universeId}/media", ct));
                if (!doc.RootElement.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Array)
                    return list;

                var images = new List<(int Index, long Id)>();
                foreach (JsonElement item in data.EnumerateArray())
                {
                    if (item.TryGetProperty("approved", out JsonElement ok) && ok.ValueKind == JsonValueKind.False)
                        continue;

                    string type = Text(item, "assetType");
                    long imageId = Long(item, "imageId");
                    bool video = type.Contains("Video", StringComparison.OrdinalIgnoreCase);

                    list.Add(new MediaInfo(video, video ? Text(item, "videoTitle") : "", ""));
                    if (imageId > 0)
                        images.Add((list.Count - 1, imageId));
                }

                Dictionary<long, string> urls = await ThumbnailsAsync("assets", "assetIds", images.Select(x => x.Id), ct, "768x432");
                foreach ((int index, long id) in images)
                    if (urls.TryGetValue(id, out string? url))
                        list[index] = list[index] with { ImageUrl = url };

                list.RemoveAll(x => x.ImageUrl.Length == 0);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Media lookup failed: {ex.Message}");
            }

            return list;
        }

        private static async Task<Dictionary<long, string>> ThumbnailsAsync(string path, string key, IEnumerable<long> ids, CancellationToken ct, string size = "150x150")
        {
            var map = new Dictionary<long, string>();
            List<long> wanted = ids.Where(x => x > 0).Distinct().Take(100).ToList();
            if (wanted.Count == 0)
                return map;

            try
            {
                using JsonDocument doc = JsonDocument.Parse(await App.HttpClient.GetStringAsync($"https://thumbnails.roblox.com/v1/{path}?{key}={string.Join(",", wanted)}&size={size}&format=Png&isCircular=false", ct));
                if (doc.RootElement.TryGetProperty("data", out JsonElement data) && data.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement item in data.EnumerateArray())
                    {
                        long id = Long(item, "targetId");
                        string url = Text(item, "imageUrl");
                        if (id > 0 && url.Length > 0)
                            map[id] = url;
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Thumbnail lookup failed: {ex.Message}");
            }

            return map;
        }

        private static long Long(JsonElement element, string name) =>
            element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number) ? number : 0;

        private static string Text(JsonElement element, string name) =>
            element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    }
}
