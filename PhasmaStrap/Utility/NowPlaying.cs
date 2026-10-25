using PhasmaStrap.Integrations;

namespace PhasmaStrap.Utility
{
    internal static class NowPlaying
    {
        private const string LOG_IDENT = "NowPlaying";

        private static volatile string _name = "";
        private static long _nameForUniverse;
        private static DateTime _retryFetchAt = DateTime.MinValue;

        private static ActivityWatcher? _watcher;

        public static void Follow(ActivityWatcher watcher)
        {
            _watcher = watcher;
            watcher.OnGameLeave += (_, _) => { _name = ""; _nameForUniverse = 0; };
        }

        public static void Forget()
        {
            _watcher = null;
            _name = "";
            _nameForUniverse = 0;
        }

        public static string GameName()
        {
            var watcher = _watcher;

            if (watcher is null || !watcher.InGame)
                return "--";

            long universeId = watcher.Data.UniverseId;
            if (universeId <= 0)
                return watcher.Data.PlaceId > 0 ? $"Place {watcher.Data.PlaceId}" : "--";

            if (_nameForUniverse == universeId && _name.Length > 0)
                return _name;

            try
            {
                UniverseDetails? details = UniverseDetails.LoadFromCache(universeId);

                if (details?.Data?.Name is string cached && cached.Length > 0)
                {
                    _name = ServerRegion.Shorten(cached, 22);
                    _nameForUniverse = universeId;
                    return _name;
                }

                if (_nameForUniverse != universeId && DateTime.UtcNow >= _retryFetchAt)
                {
                    _nameForUniverse = universeId;
                    _name = "";
                    _ = FetchAsync(universeId);
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteException($"{LOG_IDENT}::GameName", ex);
            }

            return watcher.Data.PlaceId > 0 ? $"Place {watcher.Data.PlaceId}" : "--";
        }

        public static event EventHandler? NameResolved;

        private static async Task FetchAsync(long universeId)
        {
            try
            {
                await UniverseDetails.FetchSingle(universeId);

                if (UniverseDetails.LoadFromCache(universeId)?.Data?.Name is string name && name.Length > 0)
                {
                    if (_nameForUniverse != universeId)
                        return;

                    _name = ServerRegion.Shorten(name, 22);
                    NameResolved?.Invoke(null, EventArgs.Empty);
                    return;
                }

                App.Logger.WriteLine(LOG_IDENT, $"No name came back for universe {universeId}, trying again in 30 seconds");
            }
            catch (Exception ex)
            {
                App.Logger.WriteException($"{LOG_IDENT}::FetchAsync", ex);
            }

            if (_nameForUniverse == universeId)
            {
                _nameForUniverse = 0;
                _retryFetchAt = DateTime.UtcNow.AddSeconds(30);
            }
        }

        public static string SessionLength()
        {
            var watcher = _watcher;

            if (watcher is null || !watcher.InGame)
                return "--";

            TimeSpan played = DateTime.Now - watcher.Data.TimeJoined;

            if (played < TimeSpan.Zero)
                return "--";

            return played.TotalHours >= 1
                ? $"{(int)played.TotalHours}h {played.Minutes:00}m"
                : $"{played.Minutes}m {played.Seconds:00}s";
        }
    }
}
