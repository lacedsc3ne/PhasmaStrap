using System.Windows;
using System.Windows.Input;
using PhasmaStrap.Integrations;
using CommunityToolkit.Mvvm.Input;

namespace PhasmaStrap.UI.ViewModels.ContextMenu
{
    internal class ServerInformationViewModel : NotifyPropertyChangedViewModel
    {
        private readonly ActivityWatcher _activityWatcher;

        public string InstanceId => _activityWatcher.Data.JobId;

        public string ServerType => _activityWatcher.Data.ServerType.ToTranslatedString();

        public string ServerLocation { get; private set; } = Strings.Common_Loading;

        public Visibility ServerLocationVisibility => App.Settings.Prop.ShowServerDetails ? Visibility.Visible : Visibility.Collapsed;

        public ICommand CopyInstanceIdCommand => new RelayCommand(CopyInstanceId);

        public ServerInformationViewModel(Watcher watcher)
        {
            _activityWatcher = watcher.ActivityWatcher!;

            if (ServerLocationVisibility == Visibility.Visible)
                QueryServerLocation();
        }

        public async void QueryServerLocation()
        {
            const string LOG_IDENT = "ServerInformationViewModel::QueryServerLocation";

            string? location = null;

            try
            {
                if (_activityWatcher.Data.MachineAddressValid)
                    location = await _activityWatcher.Data.QueryServerLocation();
                else
                    App.Logger.WriteLine(LOG_IDENT, $"Skipped, server address is not usable ({_activityWatcher.Data.MachineAddress})");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not get the server location for {_activityWatcher.Data.MachineAddress}: {ex.Message}");
            }

            if (String.IsNullOrEmpty(location))
                ServerLocation = Strings.Common_NotAvailable;
            else
                ServerLocation = location;

            OnPropertyChanged(nameof(ServerLocation));
        }

        private void CopyInstanceId()
        {
            try
            {
                Clipboard.SetDataObject(InstanceId);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("ServerInformationViewModel::CopyInstanceId", $"Could not copy {InstanceId}: {ex.Message}");
            }
        }
    }
}
