using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PhasmaStrap.Models
{
    public class CustomIntegration : INotifyPropertyChanged
    {
        private string _name = "";
        private string _location = "";
        private string _launchArgs = "";
        private bool _autoClose = true;
        private bool _specifyGame = false;
        private string _gameId = "";
        private bool _runAsAdmin = false;
        private bool _runMinimized = false;
        private bool _autoCloseOnGame = false;
        private int _delay = 0;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Name
        {
            get => _name;
            set => Set(ref _name, value);
        }

        public string Location
        {
            get => _location;
            set => Set(ref _location, value);
        }

        public string LaunchArgs
        {
            get => _launchArgs;
            set => Set(ref _launchArgs, value);
        }

        public bool AutoClose
        {
            get => _autoClose;
            set => Set(ref _autoClose, value);
        }

        public bool SpecifyGame
        {
            get => _specifyGame;
            set => Set(ref _specifyGame, value);
        }

        public string GameID
        {
            get => _gameId;
            set => Set(ref _gameId, value);
        }

        public bool RunAsAdmin
        {
            get => _runAsAdmin;
            set => Set(ref _runAsAdmin, value);
        }

        public bool RunMinimized
        {
            get => _runMinimized;
            set => Set(ref _runMinimized, value);
        }

        public bool AutoCloseOnGame
        {
            get => _autoCloseOnGame;
            set => Set(ref _autoCloseOnGame, value);
        }

        public int Delay
        {
            get => _delay;
            set => Set(ref _delay, value);
        }

        private void Set<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return;

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
