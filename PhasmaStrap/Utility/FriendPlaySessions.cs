using PhasmaStrap.Integrations;

namespace PhasmaStrap.Utility
{
    /// <summary>
    /// Remembers, for this app session only, when each friend was first seen in the game they are playing now.
    /// Fed by every presence poll (FriendActivityMonitor and the Friends page), read for "Playing for 42 minutes".
    /// </summary>
    public static class FriendPlaySessions
    {
        private sealed class Seen
        {
            public string GameKey { get; init; } = "";
            public DateTime SinceUtc { get; init; }
            /// <summary>False when they were already playing the first time this app looked, so the start is only a lower bound.</summary>
            public bool Exact { get; init; }
        }

        private static readonly Dictionary<long, Seen> _seen = new();

        // Last time each friend was seen online or playing, kept on this PC for "Last seen 3h ago"
        private static Dictionary<long, DateTime>? _lastSeen;
        private static DateTime _lastSeenSaved = DateTime.MinValue;
        private static string LastSeenPath => Path.Combine(Paths.Base, "FriendLastSeen.json");
        private static readonly HashSet<long> _looked = new();
        private static readonly object _lock = new();

        private static string GameKey(FriendPresence p) =>
            p.UniverseId > 0 ? $"u{p.UniverseId}" : p.RootPlaceId > 0 ? $"r{p.RootPlaceId}" : p.PlaceId > 0 ? $"p{p.PlaceId}" : "?";

        /// <summary>Notes a fresh presence answer. Friends missing from it are left alone.</summary>
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

                    // Moving from one game to another is a real start; already playing on the first look is not
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
                // Forget anyone not seen for a year so the file stays small
                DateTime cutoff = DateTime.UtcNow.AddYears(-1);
                var keep = lastSeen.Where(pair => pair.Value >= cutoff).ToDictionary(pair => pair.Key, pair => pair.Value);

                Directory.CreateDirectory(Paths.Base);
                File.WriteAllText(LastSeenPath, JsonSerializer.Serialize(keep));
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("FriendPlaySessions", $"Could not save when friends were last seen: {ex.Message}");
            }
        }

        /// <summary>"Last seen 3h ago" for an offline friend, from this PC's own polls. Empty when never seen online.</summary>
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

        /// <summary>When the friend started the game they are in now, as far as this app knows. Null when not in a game or never seen.</summary>
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

        /// <summary>"Playing for 42 minutes", or "Playing for at least 5 minutes" when the start was not seen. Empty when unknown.</summary>
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
