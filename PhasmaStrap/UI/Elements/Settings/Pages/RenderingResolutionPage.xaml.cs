using System.Windows;

using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class RenderingResolutionPage
    {
        public RenderingResolutionPage()
        {
            DataContext = RenderingViewModel.Shared.Performance;
            InitializeComponent();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            RenderingViewModel.Shared.Performance.LoadResolutionIcons();
        }
    }
}
