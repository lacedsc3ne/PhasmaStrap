using System.Windows;
using PhasmaStrap.UI.Elements.Dialogs;
using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class ChannelPage
    {
        public ChannelPage()
        {
            DataContext = new ChannelViewModel();
            InitializeComponent();
        }

        /// <summary>The tabs at the top scroll the page to their group of cards.</summary>
        private void Tab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: FrameworkElement card })
                return;

            card.BringIntoView(new Rect(0, 0, Math.Max(1, card.ActualWidth), Math.Max(card.ActualHeight, 600)));
        }

        private void BrowseChannelsButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ChannelListsDialog { Owner = Window.GetWindow(this) };

            dialog.ShowDialog();

            if (!string.IsNullOrEmpty(dialog.Result) && DataContext is ChannelViewModel viewModel)
                viewModel.RobloxChannel = dialog.Result;
        }
    }
}
