namespace PhasmaStrap.UI.Elements.Settings.Search
{
    internal sealed class SettingsSearchResult
    {
        public SettingsSearchEntry Entry { get; }

        public int Score { get; }

        public string Header => Entry.Header;

        public string Breadcrumb => Entry.Breadcrumb;

        public string Description => Entry.Description;

        public bool HasDescription => Entry.Description.Length > 0;

        public string KindLabel => Entry.Kind switch
        {
            SettingsSearchEntryKind.Option => "Setting",
            SettingsSearchEntryKind.Group => "Group",
            SettingsSearchEntryKind.Tab => "Tab",
            SettingsSearchEntryKind.Section => "Section",
            SettingsSearchEntryKind.Action => "Action",
            _ => "",
        };

        public SettingsSearchResult(SettingsSearchEntry entry, int score)
        {
            Entry = entry;
            Score = score;
        }
    }

    internal static class SettingsSearchEngine
    {
        public const int DefaultMaxResults = 40;

        // Plain words people type, and the words settings actually use for the same thing.
        private static readonly Dictionary<string, string[]> Synonyms = new(StringComparer.Ordinal)
        {
            ["fps"] = new[] { "frame rate", "framerate", "frames per second", "frame" },
            ["framerate"] = new[] { "frame rate", "fps" },
            ["ping"] = new[] { "latency", "network", "server", "datacenter" },
            ["latency"] = new[] { "ping", "low latency", "input" },
            ["lag"] = new[] { "latency", "ping", "stutter", "performance", "low end" },
            ["laggy"] = new[] { "latency", "ping", "stutter", "performance", "low end" },
            ["slow"] = new[] { "performance", "low end", "boost", "stutter" },
            ["stutter"] = new[] { "frame pacing", "stutter", "frame time" },
            ["blurry"] = new[] { "resolution", "anti aliasing", "sharpen", "texture", "quality" },
            ["blur"] = new[] { "resolution", "anti aliasing", "sharpen", "texture" },
            ["sharp"] = new[] { "sharpen", "anti aliasing", "resolution", "texture" },
            ["vsync"] = new[] { "vertical sync", "frame rate", "frame rate limit" },
            ["hud"] = new[] { "overlay", "stats" },
            ["overlay"] = new[] { "hud", "crosshair", "stats" },
            ["record"] = new[] { "replay", "clip", "capture" },
            ["recording"] = new[] { "replay", "clip", "capture" },
            ["clip"] = new[] { "replay", "capture" },
            ["video"] = new[] { "replay", "clip", "capture" },
            ["picture"] = new[] { "screenshot", "image", "background" },
            ["screenshot"] = new[] { "capture", "picture" },
            ["sound"] = new[] { "audio", "volume", "death sound" },
            ["audio"] = new[] { "sound", "volume" },
            ["volume"] = new[] { "audio", "sound" },
            ["mouse"] = new[] { "cursor", "sensitivity" },
            ["cursor"] = new[] { "mouse" },
            ["discord"] = new[] { "rich presence", "rpc" },
            ["rpc"] = new[] { "rich presence", "discord" },
            ["graphics"] = new[] { "rendering", "quality", "texture", "lighting" },
            ["gpu"] = new[] { "graphics card", "nvidia", "rendering" },
            ["ram"] = new[] { "memory" },
            ["memory"] = new[] { "ram" },
            ["account"] = new[] { "login", "sign in", "cookie" },
            ["login"] = new[] { "account", "sign in" },
            ["theme"] = new[] { "appearance", "colour", "color", "accent" },
            ["color"] = new[] { "colour", "accent", "theme" },
            ["colour"] = new[] { "color", "accent", "theme" },
            ["update"] = new[] { "version", "channel", "news" },
            ["key"] = new[] { "hotkey", "shortcut", "keybind" },
            ["keybind"] = new[] { "hotkey", "shortcut" },
            ["shortcut"] = new[] { "hotkey", "desktop" },
            ["friend"] = new[] { "friends", "social", "party" },
            ["server"] = new[] { "matchmaker", "region", "datacenter", "private server" },
            ["region"] = new[] { "datacenter", "server location" },
            ["stream"] = new[] { "stream safe", "obs" },
            ["flags"] = new[] { "fastflag", "fflag" },
            ["fflag"] = new[] { "fastflag", "flag" },
        };

        /// <summary>The other words a query was widened to, for the "also matching" hint.</summary>
        public static IReadOnlyList<string> AlsoMatching(string query)
        {
            var words = new List<string>();
            foreach (string token in SettingsSearchEntry.Normalize(query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (Synonyms.TryGetValue(token, out string[]? alternatives))
                    words.AddRange(alternatives.Take(3));
            }

            return words.Distinct(StringComparer.Ordinal).Take(4).ToList();
        }

        public static List<SettingsSearchResult> Search(string query, int maxResults = DefaultMaxResults)
        {
            var results = new List<SettingsSearchResult>();

            string normalized = SettingsSearchEntry.Normalize(query ?? "");
            if (normalized.Length == 0)
                return results;

            string[] tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            foreach (SettingsSearchEntry entry in SettingsSearchIndex.Entries)
            {
                int total = 0;
                bool allMatched = true;

                foreach (string token in tokens)
                {
                    int score = ScoreWithSynonyms(entry, token);
                    if (score == 0)
                    {
                        allMatched = false;
                        break;
                    }

                    total += score;
                }

                if (!allMatched)
                    continue;

                if (entry.NormalizedHeader == normalized)
                    total += 80;
                else if (entry.NormalizedHeader.StartsWith(normalized, StringComparison.Ordinal))
                    total += 40;
                else if (tokens.Length > 1 && entry.NormalizedHeader.Contains(normalized, StringComparison.Ordinal))
                    total += 25;

                total += entry.Kind switch
                {
                    SettingsSearchEntryKind.Option => 6,
                    SettingsSearchEntryKind.Group => 4,
                    SettingsSearchEntryKind.Tab => 3,
                    SettingsSearchEntryKind.Section => 2,
                    _ => 0,
                };

                results.Add(new SettingsSearchResult(entry, total));
            }

            results.Sort((a, b) =>
            {
                int byScore = b.Score.CompareTo(a.Score);
                if (byScore != 0)
                    return byScore;

                int byLength = a.Entry.Header.Length.CompareTo(b.Entry.Header.Length);
                if (byLength != 0)
                    return byLength;

                return string.Compare(a.Entry.Breadcrumb, b.Entry.Breadcrumb, StringComparison.Ordinal);
            });

            if (results.Count > maxResults)
                results.RemoveRange(maxResults, results.Count - maxResults);

            return results;
        }

        private static int ScoreWithSynonyms(SettingsSearchEntry entry, string token)
        {
            int direct = ScoreToken(entry, token);
            if (direct >= 85 || !Synonyms.TryGetValue(token, out string[]? alternatives))
                return direct;

            int best = direct;
            foreach (string alternative in alternatives)
            {
                int score = alternative.Contains(' ') ? ScorePhrase(entry, alternative) : ScoreToken(entry, alternative);

                // A synonym hit ranks a little below the word the person actually typed.
                best = Math.Max(best, score * 4 / 5);
            }

            return best;
        }

        private static int ScorePhrase(SettingsSearchEntry entry, string phrase)
        {
            if (entry.NormalizedHeader.Contains(phrase, StringComparison.Ordinal))
                return 70;

            if (entry.NormalizedBreadcrumb.Contains(phrase, StringComparison.Ordinal))
                return 35;

            if (entry.NormalizedDescription.Contains(phrase, StringComparison.Ordinal))
                return 22;

            return 0;
        }

        private static int ScoreToken(SettingsSearchEntry entry, string token)
        {
            int best = 0;

            if (entry.NormalizedHeader == token)
                best = Math.Max(best, 120);
            else if (entry.NormalizedHeader.StartsWith(token, StringComparison.Ordinal))
                best = Math.Max(best, 100);
            else if (StartsAnyWord(entry.HeaderWords, token))
                best = Math.Max(best, 85);
            else if (entry.NormalizedHeader.Contains(token, StringComparison.Ordinal))
                best = Math.Max(best, 65);

            if (best >= 85)
                return best;

            if (StartsAnyWord(entry.BreadcrumbWords, token))
                best = Math.Max(best, 40);
            else if (entry.NormalizedBreadcrumb.Contains(token, StringComparison.Ordinal))
                best = Math.Max(best, 30);

            if (entry.NormalizedDescription.Length > 0 && entry.NormalizedDescription.Contains(token, StringComparison.Ordinal))
                best = Math.Max(best, 22);

            if (best == 0 && token.Length >= 3 && IsSubsequence(token, entry.NormalizedHeader))
                best = 12;

            return best;
        }

        private static bool StartsAnyWord(string[] words, string token)
        {
            foreach (string word in words)
            {
                if (word.StartsWith(token, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static bool IsSubsequence(string needle, string haystack)
        {
            int n = 0;
            foreach (char c in haystack)
            {
                if (c == needle[n])
                {
                    n++;
                    if (n == needle.Length)
                        return true;
                }
            }

            return false;
        }
    }
}
