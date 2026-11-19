using System.Windows;

using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class NotificationsPage
    {
        private readonly NotificationsViewModel _viewModel;

        private MainWindow? _mainWindow;

        public NotificationsPage()
        {
            _viewModel = new NotificationsViewModel();
            DataContext = _viewModel;
            InitializeComponent();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            _viewModel.Attach();
            RefreshDoNotDisturb();

            if (_mainWindow is null && Window.GetWindow(this) is MainWindow window)
            {
                _mainWindow = window;
                _mainWindow.DoNotDisturbToggle.Click += DoNotDisturbToggle_Click;
            }
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            _viewModel.Detach();

            if (_mainWindow is not null)
            {
                _mainWindow.DoNotDisturbToggle.Click -= DoNotDisturbToggle_Click;
                _mainWindow = null;
            }
        }

        private void DoNotDisturbToggle_Click(object sender, RoutedEventArgs e) => RefreshDoNotDisturb();

        private void RefreshDoNotDisturb()
        {
            _viewModel.OnPropertyChanged(nameof(NotificationsViewModel.DoNotDisturbEnabled));
            _viewModel.OnPropertyChanged(nameof(NotificationsViewModel.SummaryText));
        }
    }
}
