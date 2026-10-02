namespace PhasmaStrap.Utility
{
    public static class ThemeCycler
    {
        private const string LOG_IDENT = "ThemeCycler";

        public static readonly string[] Choices = { "Every launch", "Every hour", "Every day", "Every week" };

        private static TimeSpan Gap(string choice) => choice switch
        {
            "Every hour" => TimeSpan.FromHours(1),
            "Every day" => TimeSpan.FromDays(1),
            "Every week" => TimeSpan.FromDays(7),
            _ => TimeSpan.Zero,
        };

        public static List<string> Themes()
        {
            try
            {
                if (!Directory.Exists(Paths.CustomThemes))
                    return new List<string>();

                return Directory.GetDirectories(Paths.CustomThemes)
                    .Where(folder => File.Exists(Path.Combine(folder, "Theme.xml")))
                    .Select(folder => Path.GetFileName(folder))
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not list the custom themes: {ex.Message}");
                return new List<string>();
            }
        }

        public static void Advance()
        {
            var settings = App.Settings.Prop;

            if (!settings.ThemeCycleEnabled || settings.BootstrapperStyle != Enums.BootstrapperStyle.CustomDialog)
                return;

            if (DateTime.UtcNow - settings.ThemeCycleLastUtc < Gap(settings.ThemeCycleEvery))
                return;

            List<string> themes = Themes();

            if (themes.Count < 2)
                return;

            int current = themes.FindIndex(name => string.Equals(name, settings.SelectedCustomTheme, StringComparison.OrdinalIgnoreCase));
            string next = themes[(current + 1) % themes.Count];

            settings.SelectedCustomTheme = next;
            settings.ThemeCycleLastUtc = DateTime.UtcNow;
            App.Settings.Save();

            App.Logger.WriteLine(LOG_IDENT, $"Launch theme is now \"{next}\" ({themes.Count} in the rotation, {settings.ThemeCycleEvery.ToLowerInvariant()})");
        }
    }
}
