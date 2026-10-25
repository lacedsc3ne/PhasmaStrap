namespace PhasmaStrap.Models.Entities
{
    public class UniverseDetails
    {
        private const int CacheLimit = 500;

        private static readonly Dictionary<long, UniverseDetails> _cache = new();

        public GameDetailResponse Data { get; set; } = null!;

        public ThumbnailResponse Thumbnail { get; set; } = null!;

        public static UniverseDetails? LoadFromCache(long id)
        {
            lock (_cache)
                return _cache.TryGetValue(id, out UniverseDetails? details) ? details : null;
        }

        public static Task FetchSingle(long id) => FetchBulk(id.ToString());

        public static async Task FetchBulk(string ids)
        {
            var gameDetailResponse = await Http.GetJson<ApiArrayResponse<GameDetailResponse>>($"https://games.roblox.com/v1/games?universeIds={ids}");

            if (!gameDetailResponse.Data.Any())
                return;

            var universeThumbnailResponse = await Http.GetJson<ApiArrayResponse<ThumbnailResponse>>($"https://thumbnails.roblox.com/v1/games/icons?universeIds={ids}&returnPolicy=PlaceHolder&size=128x128&format=Png&isCircular=false");

            if (!universeThumbnailResponse.Data.Any())
                throw new InvalidHTTPResponseException("Roblox API for Game Thumbnails returned invalid data");

            foreach (string strId in ids.Split(','))
            {
                if (!long.TryParse(strId, out long id))
                    continue;

                GameDetailResponse? data = gameDetailResponse.Data.FirstOrDefault(x => x.Id == id);
                ThumbnailResponse? thumbnail = universeThumbnailResponse.Data.FirstOrDefault(x => x.TargetId == id);

                if (data is null || thumbnail is null)
                {
                    App.Logger.WriteLine("UniverseDetails::FetchBulk", $"Roblox returned no details or icon for universe {id}");
                    continue;
                }

                lock (_cache)
                {
                    if (_cache.Count >= CacheLimit && !_cache.ContainsKey(id))
                        _cache.Clear();

                    _cache[id] = new UniverseDetails
                    {
                        Data = data,
                        Thumbnail = thumbnail
                    };
                }
            }
        }
    }
}
