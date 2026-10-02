namespace PhasmaStrap.Utility
{
    /// <summary>
    /// Small local choices on the Games pages: favourite games, private servers hidden from the list,
    /// and which saved account a game launches with. Kept in Games.json next to the other stores.
    /// </summary>
    public sealed class GamesStore
    {
        public sealed class FavouriteGame
        {
            public long UniverseId { get; set; }
            public long PlaceId { get; set; }
            public string Name { get; set; } = "";
            public string IconUrl { get; set; } = "";
            public DateTime AddedUtc { get; set; }
        }

        public sealed class Data
        {
            public List<FavouriteGame> Favourites { get; set; } = new();
            public List<long> HiddenPrivateServers { get; set; } = new();
            public Dictionary<long, long> AccountForGame { get; set; } = new();
        }

        private const string LOG_IDENT = "GamesStore";

        private static GamesStore? _shared;
        public static GamesStore Shared => _shared ??= new GamesStore(Path.Combine(Paths.Base, "Games.json"));

        /// <summary>Raised after any change, so other open pages can refresh.</summary>
        public static event EventHandler? Changed;

        private readonly string _path;
        private readonly object _lock = new();
        private Data _data = new();
        private DateTime _loadedStamp = DateTime.MinValue;

        public GamesStore(string path) => _path = path;

        private void Reload()
        {
            try
            {
                if (!File.Exists(_path))
                    return;

                DateTime stamp = File.GetLastWriteTimeUtc(_path);
                if (stamp == _loadedStamp)
                    return;

                _data = JsonSerializer.Deserialize<Data>(File.ReadAllText(_path)) ?? new Data();
                _loadedStamp = stamp;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not read {_path}: {ex.Message}");
            }
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

                string temp = _path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temp, _path, true);
                _loadedStamp = File.GetLastWriteTimeUtc(_path);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not write {_path}: {ex.Message}");
            }
        }

        private void Change(Action<Data> edit)
        {
            lock (_lock)
            {
                Reload();
                edit(_data);
                Save();
            }

            try { Changed?.Invoke(this, EventArgs.Empty); } catch { }
        }

        #region Favourites

        public List<FavouriteGame> Favourites()
        {
            lock (_lock)
            {
                Reload();
                return _data.Favourites.OrderByDescending(f => f.AddedUtc).Select(f => new FavouriteGame
                {
                    UniverseId = f.UniverseId, PlaceId = f.PlaceId, Name = f.Name, IconUrl = f.IconUrl, AddedUtc = f.AddedUtc,
                }).ToList();
            }
        }

        public bool IsFavourite(long universeId, long placeId)
        {
            lock (_lock)
            {
                Reload();
                return _data.Favourites.Any(f => Same(f, universeId, placeId));
            }
        }

        private static bool Same(FavouriteGame f, long universeId, long placeId) =>
            (universeId > 0 && f.UniverseId == universeId) || (universeId <= 0 && placeId > 0 && f.PlaceId == placeId);

        public void SetFavourite(long universeId, long placeId, string name, string iconUrl, bool favourite)
        {
            if (universeId <= 0 && placeId <= 0)
                return;

            Change(data =>
            {
                data.Favourites.RemoveAll(f => Same(f, universeId, placeId));

                if (favourite)
                    data.Favourites.Add(new FavouriteGame { UniverseId = universeId, PlaceId = placeId, Name = name, IconUrl = iconUrl, AddedUtc = DateTime.UtcNow });
            });
        }

        #endregion

        #region Hidden private servers

        public HashSet<long> HiddenPrivateServers()
        {
            lock (_lock)
            {
                Reload();
                return _data.HiddenPrivateServers.ToHashSet();
            }
        }

        public void SetPrivateServerHidden(long serverId, bool hidden)
        {
            if (serverId <= 0)
                return;

            Change(data =>
            {
                data.HiddenPrivateServers.RemoveAll(id => id == serverId);
                if (hidden)
                    data.HiddenPrivateServers.Add(serverId);
            });
        }

        public void ShowAllPrivateServers() => Change(data => data.HiddenPrivateServers.Clear());

        #endregion

        #region Account per game

        /// <summary>The saved account this game launches with, or 0 for whoever is signed in.</summary>
        public long AccountFor(long universeId)
        {
            lock (_lock)
            {
                Reload();
                return universeId > 0 && _data.AccountForGame.TryGetValue(universeId, out long userId) ? userId : 0;
            }
        }

        public void SetAccountFor(long universeId, long userId)
        {
            if (universeId <= 0)
                return;

            Change(data =>
            {
                if (userId <= 0)
                    data.AccountForGame.Remove(universeId);
                else
                    data.AccountForGame[universeId] = userId;
            });
        }

        #endregion
    }
}
