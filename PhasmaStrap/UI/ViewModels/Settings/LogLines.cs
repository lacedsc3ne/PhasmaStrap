using System.Windows.Media;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    /// <summary>
    /// Splits PhasmaStrap and Roblox log lines into time, source and message, and guesses how serious each one is.
    /// PhasmaStrap writes "2026-10-02T14:01:58Z [Ident::Method] message".
    /// Roblox writes "2026-10-02T14:01:58.123Z,12.34,abcd,6 [FLog::Output] message".
    /// </summary>
    internal static class LogLines
    {
        private static readonly Regex Line = new(
            @"^(?<time>\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(?:\.\d+)?Z(?:,\S*)?\s+\[(?<source>[^\]]+)\]\s?(?<message>.*)$",
            RegexOptions.Compiled);

        private static readonly string[] ErrorWords = { "exception", "error", "failed", "failure", "fatal", "crash", "unhandled" };

        private static readonly string[] WarningWords = { "warn", "could not", "couldn't", "cannot", "can't", "timed out", "timeout", "retry", "retrying", "not found", "denied", "skipped" };

        private static readonly Brush[] SourcePalette =
        {
            Frozen("#8E8CF5"),
            Frozen("#3DDC97"),
            Frozen("#5AB0F5"),
            Frozen("#4FD1C5"),
            Frozen("#C38CF5"),
            Frozen("#E8A0BF"),
        };

        private static readonly Brush ErrorBrush = Frozen("#F4554B");
        private static readonly Brush WarningBrush = Frozen("#F5B841");
        private static readonly Brush HeaderBrush = Frozen("#9A9AA6");

        private static Brush Frozen(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }

        public static LogRow Parse(string line)
        {
            string trimmed = line.TrimEnd();

            if (trimmed.StartsWith("===", StringComparison.Ordinal))
            {
                return new LogRow
                {
                    Message = trimmed.Trim('=', ' '),
                    Level = LogRowLevel.Header,
                    SourceBrush = HeaderBrush,
                };
            }

            Match match = Line.Match(trimmed);

            if (!match.Success)
            {
                LogRowLevel plainLevel = LevelOf("", trimmed);
                return new LogRow
                {
                    Message = trimmed,
                    Level = plainLevel,
                    SourceBrush = BrushFor("", plainLevel),
                };
            }

            string time = match.Groups["time"].Value;
            if (DateTime.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime utc))
                time = utc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);

            string source = ShortSource(match.Groups["source"].Value);
            string message = match.Groups["message"].Value;
            LogRowLevel level = LevelOf(match.Groups["source"].Value, message);

            return new LogRow
            {
                Time = time,
                Source = source,
                Message = message,
                Level = level,
                SourceBrush = BrushFor(source, level),
            };
        }

        /// <summary>"Bootstrapper::Run" becomes "Bootstrapper", and Roblox's "FLog::Network" becomes "Network".</summary>
        private static string ShortSource(string source)
        {
            string[] parts = source.Split("::", StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0)
                return source;

            if (parts.Length > 1 && parts[0].EndsWith("Log", StringComparison.Ordinal))
                return parts[1];

            return parts[0];
        }

        private static LogRowLevel LevelOf(string source, string message)
        {
            if (source.Contains("Error", StringComparison.OrdinalIgnoreCase))
                return LogRowLevel.Error;

            if (source.Contains("Warn", StringComparison.OrdinalIgnoreCase))
                return LogRowLevel.Warning;

            foreach (string word in ErrorWords)
            {
                if (message.Contains(word, StringComparison.OrdinalIgnoreCase))
                    return LogRowLevel.Error;
            }

            foreach (string word in WarningWords)
            {
                if (message.Contains(word, StringComparison.OrdinalIgnoreCase))
                    return LogRowLevel.Warning;
            }

            return LogRowLevel.Info;
        }

        private static Brush BrushFor(string source, LogRowLevel level)
        {
            if (level == LogRowLevel.Error)
                return ErrorBrush;

            if (level == LogRowLevel.Warning)
                return WarningBrush;

            int hash = 0;
            foreach (char c in source)
                hash = unchecked(hash * 31 + c);

            return SourcePalette[(hash & 0x7FFFFFFF) % SourcePalette.Length];
        }
    }
}
