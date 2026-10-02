namespace PhasmaStrap.Utility
{
    /// <summary>
    /// Remembers when each friend first showed up on your friends list, per Roblox account, so the Friends page can say
    /// "friends since". Friends who were already there the first time we looked have no known date: Roblox doesn't
    /// say when a friendship started, so for those the date can only come from PhasmaStrap's server.
    /// </summary>
    public sealed class FriendHistoryStore
    {
        public sealed class AccountHistory
        {
            /// <summary>When this account's friends list was first read. Friends seen then have no known start.</summary>
            public DateTime StartedUtc { get; set; }

            /// <summary>Friend user ID to when they first appeared. DateTime.MinValue means "already there when we started".</summary>
            public Dictionary<long, DateTime> FirstSeen { get; set; } = new();
        }

        public static Action<string>? Log;

        private static FriendHistoryStore? _shared;
        public static FriendHistoryStore Shared => _shared ??= new FriendHistoryStore(Path.Combine(Paths.Base, "FriendHistory.json"));

        private readonly string _path;
        private readonly object _lock = new();
        private Dictionary<long, AccountHistory>? _accounts;

        public FriendHistoryStore(string path)
        {
            _path = path;
        }

        private Dictionary<long, AccountHistory> Accounts()
        {
            if (_accounts is not null)
                return _accounts;

            try
            {
                _accounts = File.Exists(_path)
                    ? JsonSerializer.Deserialize<Dictionary<long, AccountHistory>>(File.ReadAllText(_path)) ?? new()
                    : new();
            }
            catch (Exception ex)
            {
                Log?.Invoke($"Could not read {_path}: {ex.Message}");
                _accounts = new();
            }

            return _accounts;
        }

        /// <summary>
        /// Notes the current friends list of <paramref name="accountId"/>. Returns the friends that are new since the
        /// last look (never the ones seen on the very first look), with the time they were first seen.
        /// </summary>
        public List<(long UserId, DateTime FirstSeenUtc)> Observe(long accountId, IEnumerable<long> friendIds)
        {
            var added = new List<(long, DateTime)>();

            if (accountId <= 0)
                return added;

            lock (_lock)
            {
                // Read fresh, in case another PhasmaStrap process wrote since
                _accounts = null;
                Dictionary<long, AccountHistory> accounts = Accounts();
                DateTime now = DateTime.UtcNow;
                bool first = !accounts.TryGetValue(accountId, out AccountHistory? history);

                if (history is null)
                {
                    history = new AccountHistory { StartedUtc = now };
                    accounts[accountId] = history;
                }

                bool changed = first;

                foreach (long id in friendIds.Distinct())
                {
                    if (id <= 0 || history.FirstSeen.ContainsKey(id))
                        continue;

                    history.FirstSeen[id] = first ? DateTime.MinValue : now;
                    changed = true;

                    if (!first)
                        added.Add((id, now));
                }

                if (changed)
                    Save(accounts);
            }

            return added;
        }

        /// <summary>When this friend first appeared on the list, or null when they were already there when we started.</summary>
        public DateTime? KnownSince(long accountId, long friendId)
        {
            lock (_lock)
            {
                if (!Accounts().TryGetValue(accountId, out AccountHistory? history))
                    return null;

                return history.FirstSeen.TryGetValue(friendId, out DateTime seen) && seen > DateTime.MinValue ? seen : null;
            }
        }

        private void Save(Dictionary<long, AccountHistory> accounts)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                string temp = _path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(accounts));
                File.Move(temp, _path, true);
            }
            catch (Exception ex)
            {
                Log?.Invoke($"Could not write {_path}: {ex.Message}");
            }
        }
    }
}
