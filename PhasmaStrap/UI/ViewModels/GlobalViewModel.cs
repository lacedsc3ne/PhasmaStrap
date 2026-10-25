using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace PhasmaStrap.UI.ViewModels
{
    public static class GlobalViewModel
    {
        public static ICommand OpenWebpageCommand => new RelayCommand<string>(OpenWebpage);

        private static void OpenWebpage(string? location)
        {
            if (location is null)
                return;

            if (!Uri.TryCreate(location, UriKind.Absolute, out Uri? uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                App.Logger.WriteLine("GlobalViewModel::OpenWebpage", $"Refused to open {location}, only web links can be opened");
                return;
            }

            Utilities.ShellExecute(location);
        }
    }
}
