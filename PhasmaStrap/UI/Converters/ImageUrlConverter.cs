using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PhasmaStrap.UI.Converters
{
    /// <summary>
    /// Turns an image URL (or local path) into an image source, and an empty value into nothing,
    /// so tiles with no thumbnail yet just show their placeholder fill instead of a binding error.
    /// </summary>
    internal class ImageUrlConverter : IValueConverter
    {
        private static readonly Dictionary<string, WeakReference<ImageSource>> Cache = new(StringComparer.Ordinal);

        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string url || string.IsNullOrWhiteSpace(url))
                return null;

            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
                return null;

            int width = parameter is string size && int.TryParse(size, out int parsed) && parsed > 0 ? parsed : 0;
            string key = url + "|" + width.ToString(CultureInfo.InvariantCulture);

            lock (Cache)
            {
                if (Cache.TryGetValue(key, out WeakReference<ImageSource>? weak) && weak.TryGetTarget(out ImageSource? cached))
                    return cached;
            }

            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = uri;
                image.DecodePixelWidth = width;
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                image.EndInit();

                lock (Cache)
                    Cache[key] = new WeakReference<ImageSource>(image);

                return image;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
}
