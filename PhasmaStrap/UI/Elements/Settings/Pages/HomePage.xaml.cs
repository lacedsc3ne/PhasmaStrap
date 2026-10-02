using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

using PhasmaStrap.Enums;
using PhasmaStrap.Integrations;
using PhasmaStrap.UI.Elements.Settings.Search;
using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class HomePage : ISearchToolHost
    {
        private readonly HomeViewModel _viewModel;

        private GameViewModel? _watchedGame;

        public HomePage()
        {
            _viewModel = new HomeViewModel();
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            DataContext = _viewModel;
            InitializeComponent();

            Loaded += (_, _) => ApplyHomeBackground();
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(HomeViewModel.Game))
                return;

            if (_watchedGame is not null)
                _watchedGame.OpenPageRequested -= Game_OpenPageRequested;

            _watchedGame = _viewModel.Game;

            if (_watchedGame is not null)
                _watchedGame.OpenPageRequested += Game_OpenPageRequested;
        }

        private void Game_OpenPageRequested(object? sender, Type pageType)
        {
            if (Window.GetWindow(this) is MainWindow window)
                window.Navigate(pageType);
        }

        private void LaunchRoblox_Click(object sender, RoutedEventArgs e) => HomeViewModel.LaunchUri("roblox://");

        private void ShowLibrary_Click(object sender, RoutedEventArgs e)
        {
            LibraryTab.IsChecked = true;
            _viewModel.ShowTab(HomeViewModel.TabLibrary);
        }

        void ISearchToolHost.ShowToolFor(SettingsSearchEntry entry)
        {
            if (entry.NestedPageType == typeof(PrivateServersPage))
            {
                PrivateTab.IsChecked = true;
                _viewModel.ShowTab(HomeViewModel.SectionPrivateServers);
            }
            else if (entry.NestedPageType == typeof(HistoryPage))
            {
                HistoryTab.IsChecked = true;
                _viewModel.ShowTab(HomeViewModel.SectionHistory);
            }
        }

        private void ApplyHomeBackground()
        {
            var prop = App.Settings.Prop;

            if (!prop.HomepageBackgroundEnabled || prop.HomepageBackgroundMode == HomepageBackgroundMode.None)
            {
                Background = Brushes.Transparent;
                return;
            }

            try
            {
                Brush brush = HomepageBackgroundRenderer.BuildBrushFromSettings();
                brush.Freeze();
                Background = brush;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("HomePage", $"Could not apply the Home page background: {ex.Message}");
                Background = Brushes.Transparent;
            }
        }
    }
}
