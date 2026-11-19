using System.Globalization;
using System.Windows.Data;

namespace PhasmaStrap.UI.Converters
{
    public class UpperCaseConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value?.ToString()?.ToUpperInvariant() ?? "";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
