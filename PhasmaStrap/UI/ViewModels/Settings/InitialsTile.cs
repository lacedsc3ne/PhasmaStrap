using System.Windows;
using System.Windows.Media;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    /// <summary>
    /// A two letter tile for things that have no icon of their own (extensions, custom programs):
    /// the initials of the name on a colour picked from the name, so the same name always gets the same tile.
    /// </summary>
    public static class InitialsTile
    {
        private static readonly (string From, string To)[] Palette =
        {
            ("#F4554B", "#C0392B"),
            ("#4A90D9", "#2C5F9E"),
            ("#8E8CF5", "#5D5BC4"),
            ("#3DBE8B", "#23845E"),
            ("#F5A341", "#C27618"),
            ("#C06FD8", "#8A3FA3"),
            ("#4FB8C9", "#2A8394"),
            ("#8A8F98", "#5E636B"),
        };

        private static readonly Dictionary<string, Brush> Cache = new(StringComparer.OrdinalIgnoreCase);

        public static string Initials(string? name)
        {
            string[] words = (name ?? "")
                .Split(new[] { ' ', '-', '_', '.', '(', ')' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => char.IsLetterOrDigit(w[0]))
                .ToArray();

            if (words.Length == 0)
                return "?";

            if (words.Length > 1)
                return $"{char.ToUpperInvariant(words[0][0])}{char.ToUpperInvariant(words[1][0])}";

            string word = words[0];
            char first = char.ToUpperInvariant(word[0]);

            // One word: its first letter and the next consonant, so "Rojo" becomes RJ.
            char second = word.Skip(1).FirstOrDefault(c => char.IsLetter(c) && "aeiouAEIOU".IndexOf(c) < 0);
            if (second == default)
                second = word.Length > 1 ? word[1] : default;

            return second == default ? first.ToString() : $"{first}{char.ToUpperInvariant(second)}";
        }

        public static Brush BrushFor(string? name)
        {
            string key = name ?? "";

            lock (Cache)
            {
                if (Cache.TryGetValue(key, out Brush? cached))
                    return cached;

                int hash = 0;
                foreach (char c in key.ToLowerInvariant())
                    hash = unchecked(hash * 31 + c);

                var (from, to) = Palette[(hash & 0x7FFFFFFF) % Palette.Length];

                var brush = new LinearGradientBrush(
                    (Color)ColorConverter.ConvertFromString(from),
                    (Color)ColorConverter.ConvertFromString(to),
                    new Point(0, 0),
                    new Point(1, 1));
                brush.Freeze();

                Cache[key] = brush;
                return brush;
            }
        }
    }
}
