using System.Globalization;
using System.Windows.Data;

namespace PhasmaStrap.UI.Converters
{
    /// <summary>Shows text in capitals (group labels in the search palette).</summary>
    public class UpperCaseConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value?.ToString()?.ToUpperInvariant() ?? "";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
