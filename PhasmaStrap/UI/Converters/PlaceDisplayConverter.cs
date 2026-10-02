using System.Windows.Data;

namespace PhasmaStrap.UI.Converters
{
    /// <summary>
    /// Shows a place ID as "Game name · 1234567" when the game is in play time history, otherwise just the ID.
    /// </summary>
    internal class PlaceDisplayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string id = value?.ToString() ?? "";
            if (!long.TryParse(id, out long placeId) || placeId <= 0)
                return id;

            try
            {
                PlayTimeEntry? entry = Integrations.PlayTimeStore.GetAll().FirstOrDefault(e => e.PlaceId == placeId);
                if (entry is { Name.Length: > 0 })
                    return $"{entry.DisplayName} · {id}";
            }
            catch
            {
            }

            return id;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
}
