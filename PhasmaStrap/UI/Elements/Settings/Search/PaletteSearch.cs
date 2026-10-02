using PhasmaStrap.Integrations;
using PhasmaStrap.Models;
using PhasmaStrap.UI.Elements.Settings.Pages;

using Wpf.Ui.Common;

namespace PhasmaStrap.UI.Elements.Settings.Search
{
    internal enum PaletteCategory
    {
        Setting,

        Page,

        Flag,

        Game,

        /// <summary>A recent or suggested search: opening it types the words into the search box.</summary>
        Query,
    }

    internal enum PaletteFilter
    {
        All,

        Settings,

        Flags,

        Games,
    }

    /// <summary>One row in the search popup: a setting, a page to jump to, one of your flags or a game you've played.</summary>
    internal sealed class PaletteItem
    {
        public PaletteCategory Category { get; init; }

        /// <summary>Heading the row is listed under.</summary>
        public string Group { get; set; } = "";

        public string Header { get; init; } = "";

        public string Subtitle { get; init; } = "";

        public string Description { get; init; } = "";

        /// <summary>Short value shown on the right of the row, like a flag value or time played.</summary>
        public string Badge { get; init; } = "";

        public SymbolRegular Icon { get; init; } = SymbolRegular.Settings24;

        public string KindLabel { get; init; } = "";

        public string OpenLabel { get; init; } = "Open";

        public int Score { get; set; }

        public SettingsSearchResult? Setting { get; init; }

        public string? FlagName { get; init; }

        public PlayTimeEntry? Game { get; init; }

        public Type? PageType { get; init; }

        public bool HasSubtitle => Subtitle.Length > 0;

        public bool HasDescription => Description.Length > 0;

        public bool HasBadge => Badge.Length > 0;
    }

    internal sealed class PaletteResults
    {
        public List<PaletteItem> Items { get; } = new();

        public int SettingCount { get; set; }

        public int FlagCount { get; set; }

        public int GameCount { get; set; }

        public int Total => SettingCount + FlagCount + GameCount;
    }

    internal static class PaletteSearch
    {
        private const int MaxFlags = 12;

        private const int MaxGames = 8;

        private static readonly (string Label, string Where, SymbolRegular Icon, Type Page)[] JumpTargets =
        {
            ("Games", "Your library and server browser", SymbolRegular.Games24, typeof(HomePage)),
            ("Social", "Friends, party, accounts and activity", SymbolRegular.People24, typeof(PeoplePage)),
            ("Performance", "Rendering, overlays and tuning", SymbolRegular.Gauge24, typeof(PerformancePage)),
            ("FastFlags", "Presets, the editor and per game flags", SymbolRegular.Flag24, typeof(FastFlagSettingsPage)),
            ("Flag editor", "FastFlags", SymbolRegular.Code24, typeof(FastFlagEditorPage)),
            ("Capture", "Screenshots, replay and storage", SymbolRegular.Camera24, typeof(CapturePage)),
            ("Mods", "Settings", SymbolRegular.PaintBrush24, typeof(ModsPage)),
            ("Appearance", "Settings", SymbolRegular.Color24, typeof(AppearancePage)),
            ("Hotkeys", "Settings", SymbolRegular.Key24, typeof(HotkeysPage)),
            ("Settings", "Everything about how PhasmaStrap runs", SymbolRegular.Settings24, typeof(SettingsPage)),
        };

        /// <summary>The top bar page a settings page lives under, used as the group heading.</summary>
        public static string AreaName(Type pageType)
        {
            Type host = SectionHosts.Resolve(pageType);

            if (host == typeof(PeoplePage)) return "Social";
            if (host == typeof(PerformancePage)) return "Performance";
            if (host == typeof(FastFlagSettingsPage)) return "FastFlags";
            if (host == typeof(CapturePage)) return "Capture";
            if (host == typeof(HomePage)) return "Games";
            return "Settings";
        }

        public static PaletteItem FromSetting(SettingsSearchResult result) => new()
        {
            Category = PaletteCategory.Setting,
            Group = AreaName(result.Entry.PageType),
            Header = result.Header,
            Subtitle = result.Breadcrumb,
            Description = result.Description,
            Icon = result.Entry.Kind == SettingsSearchEntryKind.Option ? SymbolRegular.Options24 : SymbolRegular.ArrowRight24,
            KindLabel = result.KindLabel,
            OpenLabel = result.Entry.Kind == SettingsSearchEntryKind.Option ? "Go to setting" : "Open",
            Score = result.Score,
            Setting = result,
        };

        /// <summary>What shows before anything is typed: recently opened settings, then pages to jump to.</summary>
        /// <summary>Plain words to try, shown under the pages before anything is typed.</summary>
        public static readonly string[] TrySearches = { "ping", "crosshair", "vsync", "hotkey", "DFInt", "place ID" };

        public static List<PaletteItem> StartItems(IEnumerable<SettingsSearchResult> recents, IEnumerable<string>? recentQueries = null)
        {
            var items = new List<PaletteItem>();

            foreach (string query in (recentQueries ?? Array.Empty<string>()).Where(q => !string.IsNullOrWhiteSpace(q)).Take(4))
            {
                items.Add(new PaletteItem
                {
                    Category = PaletteCategory.Query,
                    Group = "Recent searches",
                    Header = query,
                    Icon = SymbolRegular.History24,
                    KindLabel = "Search",
                    OpenLabel = "Search again",
                });
            }

            foreach (SettingsSearchResult recent in recents.Take(5))
            {
                PaletteItem item = FromSetting(recent);
                item.Group = "Recent";
                items.Add(item);
            }

            foreach (var target in JumpTargets)
            {
                items.Add(new PaletteItem
                {
                    Category = PaletteCategory.Page,
                    Group = "Jump to",
                    Header = target.Label,
                    Subtitle = target.Where,
                    Icon = target.Icon,
                    KindLabel = "Page",
                    OpenLabel = $"Open {target.Label}",
                    PageType = target.Page,
                });
            }

            foreach (string word in TrySearches)
            {
                items.Add(new PaletteItem
                {
                    Category = PaletteCategory.Query,
                    Group = "Try",
                    Header = word,
                    Icon = SymbolRegular.Search24,
                    KindLabel = "Search",
                    OpenLabel = "Search for it",
                });
            }

            return items;
        }

        public static PaletteResults Search(string query, PaletteFilter filter, ICollection<string>? recentKeys = null, Func<SettingsSearchEntry, string>? keyOf = null)
        {
            var results = new PaletteResults();

            string normalized = SettingsSearchEntry.Normalize(query ?? "");
            if (normalized.Length == 0)
                return results;

            string[] tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            var settings = SettingsSearchEngine.Search(query!).Select(FromSetting).ToList();
            if (recentKeys is not null && keyOf is not null && recentKeys.Count > 0)
            {
                foreach (PaletteItem item in settings)
                {
                    if (recentKeys.Contains(keyOf(item.Setting!.Entry)))
                        item.Score += 35;
                }
            }

            List<PaletteItem> pages = MatchPages(tokens);
            List<PaletteItem> flags = MatchFlags(tokens);
            List<PaletteItem> games = MatchGames(tokens);

            results.SettingCount = settings.Count + pages.Count;
            results.FlagCount = flags.Count;
            results.GameCount = games.Count;

            var chosen = new List<PaletteItem>();
            if (filter is PaletteFilter.All or PaletteFilter.Settings)
            {
                chosen.AddRange(pages);
                chosen.AddRange(settings);
            }
            if (filter is PaletteFilter.All or PaletteFilter.Flags)
                chosen.AddRange(flags);
            if (filter is PaletteFilter.All or PaletteFilter.Games)
                chosen.AddRange(games);

            // Groups come out in the order of their best match, so the most likely answer is always on top.
            var best = chosen
                .GroupBy(i => i.Group)
                .ToDictionary(g => g.Key, g => g.Max(i => i.Score));

            results.Items.AddRange(chosen
                .OrderByDescending(i => best[i.Group])
                .ThenBy(i => i.Group, StringComparer.Ordinal)
                .ThenByDescending(i => i.Score)
                .ThenBy(i => i.Header.Length));

            return results;
        }

        private static List<PaletteItem> MatchPages(string[] tokens)
        {
            var items = new List<PaletteItem>();

            foreach (var target in JumpTargets)
            {
                string name = SettingsSearchEntry.Normalize(target.Label);
                if (!tokens.All(t => name.Contains(t, StringComparison.Ordinal)))
                    continue;

                items.Add(new PaletteItem
                {
                    Category = PaletteCategory.Page,
                    Group = "Pages",
                    Header = target.Label,
                    Subtitle = target.Where,
                    Icon = target.Icon,
                    KindLabel = "Page",
                    OpenLabel = $"Open {target.Label}",
                    PageType = target.Page,
                    Score = name.StartsWith(string.Join(' ', tokens), StringComparison.Ordinal) ? 150 : 90,
                });
            }

            return items;
        }

        private static List<PaletteItem> MatchFlags(string[] tokens)
        {
            var items = new List<PaletteItem>();

            Dictionary<string, object> flags;
            try
            {
                flags = App.FastFlags.Prop;
            }
            catch
            {
                return items;
            }

            // Flag names have no spaces, so "prefer vulkan" should still find FFlagDebugGraphicsPreferVulkan.
            string joined = string.Concat(tokens);

            foreach (var pair in flags)
            {
                string lower = pair.Key.ToLowerInvariant();

                int score;
                if (lower.Contains(joined, StringComparison.Ordinal))
                    score = lower.EndsWith(joined, StringComparison.Ordinal) ? 75 : 60;
                else if (tokens.All(t => lower.Contains(t, StringComparison.Ordinal)))
                    score = 45;
                else
                    continue;

                string value = pair.Value?.ToString() ?? "";

                items.Add(new PaletteItem
                {
                    Category = PaletteCategory.Flag,
                    Group = "Your FastFlags",
                    Header = pair.Key,
                    Subtitle = "FastFlags  ›  Editor  ›  Your flags",
                    Description = $"Set to {Shorten(value, 60)} for every game.",
                    Badge = Shorten(value, 14),
                    Icon = SymbolRegular.Flag24,
                    KindLabel = "Flag",
                    OpenLabel = "Show in the editor",
                    FlagName = pair.Key,
                    Score = score,
                });
            }

            return items.OrderByDescending(i => i.Score).ThenBy(i => i.Header.Length).Take(MaxFlags).ToList();
        }

        private static List<PaletteItem> MatchGames(string[] tokens)
        {
            var items = new List<PaletteItem>();

            IReadOnlyList<PlayTimeEntry> played;
            try
            {
                played = PlayTimeStore.GetAll();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("PaletteSearch", $"Could not read play time: {ex.Message}");
                return items;
            }

            string phrase = string.Join(' ', tokens);

            foreach (PlayTimeEntry entry in played)
            {
                string name = SettingsSearchEntry.Normalize(entry.DisplayName);
                if (name.Length == 0 || !tokens.All(t => name.Contains(t, StringComparison.Ordinal)))
                    continue;

                int score = name.StartsWith(phrase, StringComparison.Ordinal) ? 80 : 55;

                items.Add(new PaletteItem
                {
                    Category = PaletteCategory.Game,
                    Group = "Games you've played",
                    Header = entry.DisplayName,
                    Subtitle = entry.LastPlayed == default ? "Played on this PC" : $"Last played {entry.LastPlayedText.ToLowerInvariant()}",
                    Description = $"You've played for {entry.TotalTimeText} in total.",
                    Badge = entry.TotalTimeText,
                    Icon = SymbolRegular.Games24,
                    KindLabel = "Game",
                    OpenLabel = "Open game",
                    Game = entry,
                    Score = score,
                });
            }

            return items.OrderByDescending(i => i.Score).Take(MaxGames).ToList();
        }

        private static string Shorten(string value, int max) => value.Length <= max ? value : value[..(max - 1)] + "…";
    }
}
