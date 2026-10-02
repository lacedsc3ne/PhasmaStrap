using System.Windows.Data;

namespace PhasmaStrap.UI.Converters
{
    /// <summary>
    /// One line about a custom program that launches with Roblox, like "Closes with Roblox · minimised" or
    /// "Only for Rivals · 2000 ms delay". Built from the program's own settings.
    /// Values, in order: AutoClose, AutoCloseOnGame, SpecifyGame, GameID, RunMinimized, RunAsAdmin, Delay.
    /// </summary>
    internal class CustomIntegrationSummaryConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            bool Flag(int index) => index < values.Length && values[index] is true;

            bool autoClose = Flag(0);
            bool closeOnGame = Flag(1);
            bool specifyGame = Flag(2);
            string gameId = values.Length > 3 ? values[3] as string ?? "" : "";
            bool minimised = Flag(4);
            bool admin = Flag(5);
            int delay = values.Length > 6 && values[6] is int ms ? ms : 0;

            var parts = new List<string>();

            if (specifyGame && gameId.Trim().Length > 0)
                parts.Add($"Only for {GameName(gameId.Trim())}");

            if (closeOnGame)
                parts.Add("Closes when you leave the game");
            else if (autoClose)
                parts.Add("Closes with Roblox");
            else
                parts.Add("Stays open after Roblox");

            if (minimised)
                parts.Add("minimised");

            if (admin)
                parts.Add("as administrator");

            if (delay > 0)
                parts.Add($"{delay} ms delay");

            return string.Join(" · ", parts);
        }

        private static string GameName(string gameId)
        {
            if (long.TryParse(gameId, out long placeId) && placeId > 0)
            {
                try
                {
                    string name = PlaceNames.NameOf(placeId).Trim();
                    if (name.Length > 0)
                        return name;
                }
                catch
                {
                }

                return $"place {placeId}";
            }

            return gameId;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
