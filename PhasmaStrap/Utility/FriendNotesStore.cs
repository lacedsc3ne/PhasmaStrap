namespace PhasmaStrap.Utility
{
    public sealed class FriendNotesStore
    {
        public sealed class Entry
        {
            public bool Favourite { get; set; }
            public string Note { get; set; } = "";

            /// <summary>Alert override for this friend: "" follows the usual settings, or one of the AlertMode values.</summary>
            public string Alerts { get; set; } = "";

            [System.Text.Json.Serialization.JsonIgnore]
            public bool IsEmpty => !Favourite && string.IsNullOrWhiteSpace(Note) && string.IsNullOrEmpty(Alerts);
        }

        public static Action<string>? Log;

        private static FriendNotesStore? _shared;
        public static FriendNotesStore Shared => _shared ??= new FriendNotesStore(Path.Combine(Paths.Base, "FriendNotes.json"));

        private readonly string _path;
        private readonly object _lock = new();
        private bool _dirty;
        private Dictionary<long, Entry> _entries = new();
        private DateTime _loadedStamp = DateTime.MinValue;
        private System.Threading.Timer? _saveTimer;

        public FriendNotesStore(string path)
        {
            _path = path;

            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                if (_dirty)
                    Flush();
            };
        }

        private void Reload()
        {
            try
            {
                if (!File.Exists(_path))
                {
                    if (_loadedStamp != DateTime.MinValue)
                        _entries = new();
                    _loadedStamp = DateTime.MinValue;
                    return;
                }

                DateTime stamp = File.GetLastWriteTimeUtc(_path);
                if (stamp == _loadedStamp)
                    return;

                _entries = System.Text.Json.JsonSerializer.Deserialize<Dictionary<long, Entry>>(File.ReadAllText(_path)) ?? new();
                _loadedStamp = stamp;
            }
            catch (Exception ex)
            {
                Log?.Invoke($"Could not read {_path}: {ex.Message}");
            }
        }

        public Entry Get(long userId)
        {
            lock (_lock)
            {
                Reload();
                return _entries.TryGetValue(userId, out Entry? entry)
                    ? new Entry { Favourite = entry.Favourite, Note = entry.Note, Alerts = entry.Alerts ?? "" }
                    : new Entry();
            }
        }

        public bool IsFavourite(long userId) => Get(userId).Favourite;

        public HashSet<long> Favourites()
        {
            lock (_lock)
            {
                Reload();
                return _entries.Where(pair => pair.Value.Favourite).Select(pair => pair.Key).ToHashSet();
            }
        }

        public void Set(long userId, bool favourite, string? note)
        {
            lock (_lock)
            {
                Reload();

                var entry = new Entry { Favourite = favourite, Note = (note ?? "").Trim() };

                // Keep this friend's alert choice when only the star or the note changes
                if (_entries.TryGetValue(userId, out Entry? existing))
                    entry.Alerts = existing.Alerts ?? "";

                if (entry.IsEmpty)
                    _entries.Remove(userId);
                else
                    _entries[userId] = entry;

                _dirty = true;

                _saveTimer ??= new System.Threading.Timer(_ => Flush(), null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
                _saveTimer.Change(400, System.Threading.Timeout.Infinite);
            }
        }

        /// <summary>The values an alert override can take. Anything else means "use the usual settings".</summary>
        public static class AlertMode
        {
            public const string Usual = "";
            public const string Online = "online";
            public const string Game = "game";
            public const string Never = "never";
        }

        public void SetAlerts(long userId, string? mode)
        {
            lock (_lock)
            {
                Reload();

                _entries.TryGetValue(userId, out Entry? existing);
                var entry = new Entry
                {
                    Favourite = existing?.Favourite ?? false,
                    Note = existing?.Note ?? "",
                    Alerts = mode is AlertMode.Online or AlertMode.Game or AlertMode.Never ? mode! : AlertMode.Usual,
                };

                if (entry.IsEmpty)
                    _entries.Remove(userId);
                else
                    _entries[userId] = entry;

                _dirty = true;

                _saveTimer ??= new System.Threading.Timer(_ => Flush(), null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
                _saveTimer.Change(400, System.Threading.Timeout.Infinite);
            }
        }

        /// <summary>Friends with their own alert choice, keyed by user ID.</summary>
        public Dictionary<long, string> AlertOverrides()
        {
            lock (_lock)
            {
                Reload();
                return _entries
                    .Where(pair => !string.IsNullOrEmpty(pair.Value.Alerts))
                    .ToDictionary(pair => pair.Key, pair => pair.Value.Alerts);
            }
        }

        public void Flush()
        {
            lock (_lock)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

                    string temp = _path + ".tmp";
                    File.WriteAllText(temp, System.Text.Json.JsonSerializer.Serialize(_entries, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                    File.Move(temp, _path, true);
                    _dirty = false;

                    _loadedStamp = File.GetLastWriteTimeUtc(_path);
                }
                catch (Exception ex)
                {
                    Log?.Invoke($"Could not write {_path}: {ex.Message}");
                }
            }
        }
    }
}
