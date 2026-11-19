using PhasmaStrap.Integrations;

namespace PhasmaStrap.Utility
{
    public static class FriendPlaySessions
    {
        private sealed class Seen
        {
            public string GameKey { get; init; } = "";
            public DateTime SinceUtc { get; init; }
            public bool Exact { get; init; }
        }

        private static readonly Dictionary<long, Seen> _seen = new();

        private static Dictionary<long, DateTime>? _lastSeen;
        private static DateTime _lastSeenSaved = DateTime.MinValue;
        private static string LastSeenPath => Path.Combine(Paths.Base, "FriendLastSeen.json");
        private static readonly HashSet<long> _looked = new();
        private static readonly object _lock = new();

        private static string GameKey(FriendPresence p) =>
            p.UniverseId > 0 ? $"u{p.UniverseId}" : p.RootPlaceId > 0 ? $"r{p.RootPlaceId}" : p.PlaceId > 0 ? $"p{p.PlaceId}" : "?";

        public static void Observe(IReadOnlyDictionary<long, FriendPresence> presence)
        {
            DateTime now = DateTime.UtcNow;

            lock (_lock)
            {
                Dictionary<long, DateTime> lastSeen = LoadLastSeen();

                foreach ((long userId, FriendPresence p) in presence)
                {
                    bool firstLook = _looked.Add(userId);

                    if (p.Type != FriendPresenceType.Offline)
                        lastSeen[userId] = now;

                    if (p.Type != FriendPresenceType.InGame)
                    {
                        _seen.Remove(userId);
                        continue;
                    }

                    string key = GameKey(p);

                    if (_seen.TryGetValue(userId, out Seen? was) && was.GameKey == key)
                        continue;

                    _seen[userId] = new Seen { GameKey = key, SinceUtc = now, Exact = !firstLook };
                }

                if (now - _lastSeenSaved > TimeSpan.FromMinutes(2))
                {
                    _lastSeenSaved = now;
                    SaveLastSeen(lastSeen);
                }
            }
        }

        private static Dictionary<long, DateTime> LoadLastSeen()
        {
            if (_lastSeen is not null)
                return _lastSeen;

            try
            {
                if (File.Exists(LastSeenPath))
                    _lastSeen = JsonSerializer.Deserialize<Dictionary<long, DateTime>>(File.ReadAllText(LastSeenPath));
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("FriendPlaySessions", $"Could not read when friends were last seen: {ex.Message}");
            }

            return _lastSeen ??= new Dictionary<long, DateTime>();
        }

        private static void SaveLastSeen(Dictionary<long, DateTime> lastSeen)
        {
            try
            {
                DateTime cutoff = DateTime.UtcNow.AddYears(-1);
                var keep = lastSeen.Where(pair => pair.Value >= cutoff).ToDictionary(pair => pair.Key, pair => pair.Value);

                Directory.CreateDirectory(Paths.Base);
                string temp = LastSeenPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(keep));
                File.Move(temp, LastSeenPath, true);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("FriendPlaySessions", $"Could not save when friends were last seen: {ex.Message}");
            }
        }

        public static string LastSeenText(long userId)
        {
            DateTime seen;

            lock (_lock)
            {
                if (!LoadLastSeen().TryGetValue(userId, out seen))
                    return "";
            }

            TimeSpan ago = DateTime.UtcNow - seen;
            if (ago < TimeSpan.Zero)
                ago = TimeSpan.Zero;

            if (ago.TotalMinutes < 5)
                return "Last seen just now";
            if (ago.TotalHours < 1)
                return $"Last seen {(int)ago.TotalMinutes}m ago";
            if (ago.TotalHours < 24)
                return $"Last seen {(int)ago.TotalHours}h ago";

            DateTime local = seen.ToLocalTime().Date;
            int days = (int)(DateTime.Now.Date - local).TotalDays;

            return days switch
            {
                1 => "Last seen yesterday",
                < 7 => $"Last seen {days} days ago",
                _ => local.Year == DateTime.Now.Year ? $"Last seen {local:d MMM}" : $"Last seen {local:d MMM yyyy}",
            };
        }

        public static (DateTime SinceUtc, bool Exact)? Since(long userId, FriendPresence? current)
        {
            if (current is null || current.Type != FriendPresenceType.InGame)
                return null;

            lock (_lock)
            {
                if (_seen.TryGetValue(userId, out Seen? seen) && seen.GameKey == GameKey(current))
                    return (seen.SinceUtc, seen.Exact);
            }

            return null;
        }

        public static string PlayingForText(long userId, FriendPresence? current)
        {
            (DateTime SinceUtc, bool Exact)? found = Since(userId, current);
            if (found is null)
                return "";

            DateTime since = found.Value.SinceUtc;
            bool exact = found.Value.Exact;

            TimeSpan length = DateTime.UtcNow - since;
            if (length < TimeSpan.Zero)
                length = TimeSpan.Zero;

            string amount;
            if (length.TotalMinutes < 1)
                amount = "";
            else if (length.TotalHours < 1)
                amount = (int)length.TotalMinutes == 1 ? "1 minute" : $"{(int)length.TotalMinutes} minutes";
            else
            {
                int hours = (int)length.TotalHours;
                string h = hours == 1 ? "1 hour" : $"{hours} hours";
                amount = length.Minutes == 0 ? h : $"{h} {length.Minutes} min";
            }

            if (amount.Length == 0)
                return exact ? "Just started playing" : "";

            return exact ? $"Playing for {amount}" : $"Playing for at least {amount}";
        }
    }
}
