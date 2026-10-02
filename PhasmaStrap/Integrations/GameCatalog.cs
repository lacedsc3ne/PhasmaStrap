namespace PhasmaStrap.Integrations
{
    public sealed record GameDetails(
        long UniverseId,
        long RootPlaceId,
        string Name,
        string Description,
        string Creator,
        string Genre,
        long Playing,
        long Visits,
        long Favorites,
        int MaxPlayers,
        DateTime? Updated);

    public sealed record CatalogGame(long UniverseId, long RootPlaceId, string Name, long Playing);

    public sealed record CatalogSort(string Name, List<CatalogGame> Games);

    /// <summary>
    /// Read only lookups behind the Games catalog and game page: details, likes, banner art,
    /// place icons and the front page sorts. Every call fails soft and logs instead of throwing.
    /// </summary>
    public static class GameCatalog
    {
        private const string LOG_IDENT = "GameCatalog";

        public static async Task<List<GameDetails>> GetDetailsAsync(IEnumerable<long> universeIds, CancellationToken ct = default)
        {
            var result = new List<GameDetails>();
            List<long> ids = universeIds.Where(x => x > 0).Distinct().Take(50).ToList();
            if (ids.Count == 0)
                return result;

            try
            {
                using JsonDocument doc = JsonDocument.Parse(await App.HttpClient.GetStringAsync($"https://games.roblox.com/v1/games?universeIds={string.Join(",", ids)}", ct));
                if (!doc.RootElement.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Array)
                    return result;

                foreach (JsonElement game in data.EnumerateArray())
                {
                    long id = Long(game, "id");
                    if (id <= 0)
                        continue;

                    DateTime? updated = null;
                    if (game.TryGetProperty("updated", out JsonElement u) && u.ValueKind == JsonValueKind.String && DateTime.TryParse(u.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out DateTime parsed))
                        updated = parsed;

                    string creator = game.TryGetProperty("creator", out JsonElement c) && c.TryGetProperty("name", out JsonElement cn) ? cn.GetString() ?? "" : "";

                    result.Add(new GameDetails(
                        id,
                        Long(game, "rootPlaceId"),
                        Text(game, "name"),
                        Text(game, "description"),
                        creator,
                        Text(game, "genre"),
                        Long(game, "playing"),
                        Long(game, "visits"),
                        Long(game, "favoritedCount"),
                        (int)Long(game, "maxPlayers"),
                        updated));
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Details lookup failed: {ex.Message}");
            }

            return result;
        }

        public static async Task<GameDetails?> GetDetailsAsync(long universeId, CancellationToken ct = default) =>
            (await GetDetailsAsync(new[] { universeId }, ct)).FirstOrDefault();

        /// <summary>Share of votes that are likes, 0 to 100, or null when nobody has voted.</summary>
        public static async Task<int?> GetLikedPercentAsync(long universeId, CancellationToken ct = default)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(await App.HttpClient.GetStringAsync($"https://games.roblox.com/v1/games/votes?universeIds={universeId}", ct));
                if (doc.RootElement.TryGetProperty("data", out JsonElement data) && data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0)
                {
                    long up = Long(data[0], "upVotes");
                    long down = Long(data[0], "downVotes");
                    if (up + down > 0)
                        return (int)Math.Round(up * 100.0 / (up + down));
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Votes lookup failed: {ex.Message}");
            }

            return null;
        }

        public static async Task<string?> GetBannerUrlAsync(long universeId, CancellationToken ct = default)
        {
            try
            {
                string url = $"https://thumbnails.roblox.com/v1/games/multiget/thumbnails?universeIds={universeId}&countPerUniverse=1&defaults=true&size=768x432&format=Png&isCircular=false";
                using JsonDocument doc = JsonDocument.Parse(await App.HttpClient.GetStringAsync(url, ct));
                if (doc.RootElement.TryGetProperty("data", out JsonElement data) && data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0
                    && data[0].TryGetProperty("thumbnails", out JsonElement thumbs) && thumbs.ValueKind == JsonValueKind.Array && thumbs.GetArrayLength() > 0)
                {
                    string image = Text(thumbs[0], "imageUrl");
                    return image.Length > 0 ? image : null;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Banner lookup failed: {ex.Message}");
            }

            return null;
        }

        public static async Task<Dictionary<long, string>> GetPlaceIconsAsync(IEnumerable<long> placeIds, CancellationToken ct = default)
        {
            var map = new Dictionary<long, string>();
            List<long> ids = placeIds.Where(x => x > 0).Distinct().Take(100).ToList();
            if (ids.Count == 0)
                return map;

            try
            {
                string url = $"https://thumbnails.roblox.com/v1/places/gameicons?placeIds={string.Join(",", ids)}&returnPolicy=PlaceHolder&size=150x150&format=Png&isCircular=false";
                using JsonDocument doc = JsonDocument.Parse(await App.HttpClient.GetStringAsync(url, ct));
                if (doc.RootElement.TryGetProperty("data", out JsonElement data) && data.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement item in data.EnumerateArray())
                    {
                        long id = Long(item, "targetId");
                        string image = Text(item, "imageUrl");
                        if (id > 0 && image.Length > 0)
                            map[id] = image;
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Place icon lookup failed: {ex.Message}");
            }

            return map;
        }

        /// <summary>The front page rows Roblox shows on its Charts / Home page (Trending, Popular and so on).</summary>
        public static async Task<List<CatalogSort>> GetSortsAsync(CancellationToken ct = default)
        {
            var sorts = new List<CatalogSort>();

            try
            {
                string url = $"https://apis.roblox.com/explore-api/v1/get-sorts?sessionId={Guid.NewGuid()}&device=computer&country=all";
                using JsonDocument doc = JsonDocument.Parse(await App.HttpClient.GetStringAsync(url, ct));

                if (!doc.RootElement.TryGetProperty("sorts", out JsonElement list) || list.ValueKind != JsonValueKind.Array)
                    return sorts;

                foreach (JsonElement sort in list.EnumerateArray())
                {
                    if (!sort.TryGetProperty("games", out JsonElement games) || games.ValueKind != JsonValueKind.Array)
                        continue;

                    string name = Text(sort, "sortDisplayName");
                    if (name.Length == 0)
                        name = Text(sort, "sortId");

                    var row = new List<CatalogGame>();
                    foreach (JsonElement game in games.EnumerateArray())
                    {
                        long universeId = Long(game, "universeId");
                        if (universeId <= 0 || row.Any(g => g.UniverseId == universeId))
                            continue;

                        row.Add(new CatalogGame(universeId, Long(game, "rootPlaceId"), Text(game, "name"), Long(game, "playerCount")));
                        if (row.Count >= 24)
                            break;
                    }

                    if (row.Count > 0 && name.Length > 0)
                        sorts.Add(new CatalogSort(name, row));

                    if (sorts.Count >= 6)
                        break;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Front page sorts failed: {ex.Message}");
            }

            return sorts;
        }

        public static string Compact(long value)
        {
            if (value < 0)
                return "?";
            if (value < 1000)
                return value.ToString(CultureInfo.InvariantCulture);
            if (value < 1_000_000)
                return (value / 1000.0).ToString(value < 10_000 ? "0.#" : "0", CultureInfo.InvariantCulture) + "K";
            if (value < 1_000_000_000)
                return (value / 1_000_000.0).ToString(value < 10_000_000 ? "0.#" : "0", CultureInfo.InvariantCulture) + "M";
            return (value / 1_000_000_000.0).ToString("0.#", CultureInfo.InvariantCulture) + "B";
        }

        private static long Long(JsonElement element, string name) =>
            element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number) ? number : 0;

        private static string Text(JsonElement element, string name) =>
            element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    }
}
