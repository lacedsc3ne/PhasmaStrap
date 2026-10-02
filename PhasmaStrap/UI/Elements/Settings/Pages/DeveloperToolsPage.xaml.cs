using System.Windows.Controls;

using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class DeveloperToolsPage : Search.ISearchToolHost
    {
        public DeveloperToolsPage()
        {
            DataContext = new DeveloperToolsViewModel();
            InitializeComponent();
        }

        private void Page_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            ((DeveloperToolsViewModel)DataContext).Attach();
        }

        private void Page_Unloaded(object sender, System.Windows.RoutedEventArgs e)
        {
            ((DeveloperToolsViewModel)DataContext).Detach();
        }

        // Diagnostics used to be one of the tools here. It has its own place in the settings rail now.
        void Search.ISearchToolHost.ShowToolFor(Search.SettingsSearchEntry entry)
        {
            var tools = ((DeveloperToolsViewModel)DataContext).Tools;

            foreach (DeveloperToolItem item in tools)
            {
                if (string.Equals(item.Label, entry.Section, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(item.Label, entry.Header, StringComparison.OrdinalIgnoreCase))
                {
                    ShowTool(item.Key);
                    return;
                }
            }
        }

        public void ShowTool(string key)
        {
            foreach (object entry in ToolRail.Items)
            {
                if (entry is DeveloperToolItem item && string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    ToolRail.SelectedItem = item;
                    return;
                }
            }
        }

        private void ToolRail_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
        }
    }
}
