using System.Collections;
using System.Resources;

namespace PhasmaStrap.UI.Elements.Settings.Search
{
    /// <summary>
    /// Shareable links to one entry of the settings search index.
    /// <para>
    /// The key is the same string the search uses for recent results (<see cref="MainWindow.RecentKey"/>:
    /// kind | page | tab | section | group | header), base64url encoded. Tab, section, group and header are written as
    /// their resource name ("@Menu.Appearance.Theme") when they come from <see cref="Strings"/>, so a link copied in one
    /// language opens the same setting in another.
    /// </para>
    /// The app link is phasmastrap://settings/&lt;key&gt;. What gets copied is the web form,
    /// https://phasmastrap.com/s/&lt;key&gt;, which the site turns into the app link (see specs/extras.md).
    /// </summary>
    internal static class SettingLink
    {
        private const string LOG_IDENT = "SettingLink";

        public const string WebPrefix = "https://phasmastrap.com/s/";

        private const int MaxKeyLength = 2048;

        public static string AppLink(SettingsSearchEntry entry) => $"{WindowsRegistry.LinkScheme}://settings/{KeyOf(entry)}";

        public static string WebLink(SettingsSearchEntry entry) => WebPrefix + KeyOf(entry);

        #region Key

        public static string KeyOf(SettingsSearchEntry entry)
        {
            string text = string.Join("|",
                entry.Kind.ToString(),
                entry.PageType.Name,
                Neutral(entry.Tab),
                Neutral(entry.Section),
                Neutral(entry.Group),
                Neutral(entry.Header));

            return Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        /// <summary>The key inside an app link or web link, or null when <paramref name="link"/> is neither.</summary>
        public static string? KeyFromLink(string? link)
        {
            if (string.IsNullOrWhiteSpace(link))
                return null;

            string text = link.Trim().Trim('"');
            string scheme = WindowsRegistry.LinkScheme;

            string[] prefixes =
            {
                $"{scheme}://settings/",
                $"{scheme}:settings/",
                WebPrefix,
                "https://www.phasmastrap.com/s/",
                "http://phasmastrap.com/s/",
                "http://www.phasmastrap.com/s/",
            };

            string? rest = null;
            foreach (string prefix in prefixes)
            {
                if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    rest = text[prefix.Length..];
                    break;
                }
            }

            if (rest is null)
                return null;

            int cut = rest.IndexOfAny(new[] { '?', '#', '/' });
            if (cut >= 0)
                rest = rest[..cut];

            if (rest.Length == 0 || rest.Length > MaxKeyLength || !rest.All(IsKeyChar))
                return null;

            return rest;
        }

        private static bool IsKeyChar(char c) => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-' or '_';

        /// <summary>The index entry a key points at, or null when this version has nothing that matches.</summary>
        public static SettingsSearchEntry? Resolve(string key)
        {
            string? text = Decode(key);
            if (text is null)
                return null;

            string[] parts = text.Split('|', 6);
            if (parts.Length != 6)
                return null;

            for (int i = 2; i < parts.Length; i++)
                parts[i] = Localized(parts[i]);

            string wanted = string.Join("|", parts);
            IReadOnlyList<SettingsSearchEntry> entries = SettingsSearchIndex.Entries;

            SettingsSearchEntry? exact = entries.FirstOrDefault(e => MainWindow.RecentKey(e) == wanted);
            if (exact is not null)
                return exact;

            // The setting may have moved to another tab or group on the same page since the link was made.
            string kind = parts[0], page = parts[1], header = parts[5];
            return entries.FirstOrDefault(e => e.Kind.ToString() == kind && e.PageType.Name == page && e.Header == header)
                ?? entries.FirstOrDefault(e => e.PageType.Name == page && e.Header == header);
        }

        private static string? Decode(string key)
        {
            try
            {
                string base64 = key.Replace('-', '+').Replace('_', '/');
                base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
                return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            }
            catch (FormatException)
            {
                return null;
            }
        }

        #endregion

        #region Language neutral parts

        private static readonly Lazy<Dictionary<string, string>> ResourceNames = new(LoadResourceNames);

        private static string Neutral(string value)
        {
            if (value.Length == 0)
                return value;

            if (ResourceNames.Value.TryGetValue(value, out string? name))
                return "@" + name;

            // A plain text part that happens to start with @ is escaped as @@.
            return value.StartsWith('@') ? "@" + value : value;
        }

        private static string Localized(string part)
        {
            if (!part.StartsWith('@'))
                return part;

            if (part.StartsWith("@@"))
                return part[1..];

            string? value = null;
            try
            {
                value = Strings.ResourceManager.GetString(part[1..], Strings.Culture);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not look up {part}: {ex.Message}");
            }

            return value?.Trim() ?? part;
        }

        /// <summary>Displayed text to resource name, for the language PhasmaStrap is showing.</summary>
        private static Dictionary<string, string> LoadResourceNames()
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);

            var cultures = new List<CultureInfo>();
            for (CultureInfo? culture = Strings.Culture ?? CultureInfo.CurrentUICulture; culture is not null; culture = culture.Parent)
            {
                cultures.Add(culture);
                if (culture.Equals(CultureInfo.InvariantCulture))
                    break;
            }

            if (!cultures.Contains(CultureInfo.InvariantCulture))
                cultures.Add(CultureInfo.InvariantCulture);

            // Most specific first, so a translated text maps to the name it is shown for.
            foreach (CultureInfo culture in cultures)
            {
                ResourceSet? set;
                try
                {
                    set = Strings.ResourceManager.GetResourceSet(culture, true, false);
                }
                catch (Exception)
                {
                    continue;
                }

                if (set is null)
                    continue;

                foreach (DictionaryEntry item in set)
                {
                    if (item.Key is string name && item.Value is string text && text.Trim().Length > 0)
                        map.TryAdd(text.Trim(), name);
                }
            }

            return map;
        }

        #endregion

        #region Finding a row's entry

        /// <summary>
        /// The index entry for a setting row: same header on the nearest page the row sits in
        /// (<paramref name="pageTypes"/>, innermost first), else the same header anywhere.
        /// </summary>
        public static SettingsSearchEntry? FindEntry(string header, IReadOnlyList<Type> pageTypes)
        {
            header = (header ?? "").Trim();
            if (header.Length == 0)
                return null;

            IReadOnlyList<SettingsSearchEntry> entries = SettingsSearchIndex.Entries;

            List<SettingsSearchEntry> matches = entries.Where(e => e.Header == header).ToList();
            if (matches.Count == 0)
            {
                string normalized = SettingsSearchEntry.Normalize(header);
                if (normalized.Length > 0)
                    matches = entries.Where(e => e.NormalizedHeader == normalized).ToList();
            }

            if (matches.Count == 0)
                return null;

            foreach (Type page in pageTypes)
            {
                SettingsSearchEntry? onPage = matches
                    .Where(e => e.PageType == page || e.NestedPageType == page)
                    .OrderBy(Rank)
                    .FirstOrDefault();

                if (onPage is not null)
                    return onPage;
            }

            return matches.OrderBy(Rank).First();
        }

        private static int Rank(SettingsSearchEntry entry) => entry.Kind switch
        {
            SettingsSearchEntryKind.Option => 0,
            SettingsSearchEntryKind.Action => 1,
            SettingsSearchEntryKind.Group => 2,
            SettingsSearchEntryKind.Section => 3,
            _ => 4,
        };

        #endregion
    }
}
