using System.Windows;

using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class RenderingFrameRatePage
    {
        public RenderingFrameRatePage()
        {
            DataContext = RenderingViewModel.Shared.Performance;
            InitializeComponent();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            // The watcher writes a new summary each time you leave a game, so read it again whenever the page shows.
            RenderingViewModel.Shared.Performance.LoadLastSession();
        }
    }
}
