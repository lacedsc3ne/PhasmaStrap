using System;
using System.Windows;
using System.Windows.Media;

using PhasmaStrap.UI.Elements.Controls;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class FastFlagSettingsPage : ISectionHostPage
    {
        public FastFlagSettingsPage()
        {
            InitializeComponent();

            Host.SectionChanged += (_, _) => UpdateHeader();
            UpdateHeader();
        }

        // The header button belongs to the Roblox FFlags tab, as in the mockups.
        private void UpdateHeader()
        {
            if (_editorHeaderButton is null)
                return;

            _editorHeaderButton.Visibility = Host.CurrentSection is null || Host.CurrentSection == typeof(FastFlagsPage) ? Visibility.Visible : Visibility.Collapsed;
        }

        private System.Windows.Controls.Button? _editorHeaderButton;

        private void EditorHeaderButton_Loaded(object sender, RoutedEventArgs e)
        {
            _editorHeaderButton = sender as System.Windows.Controls.Button;
            UpdateHeader();
        }

        private void EditorHeaderButton_Click(object sender, RoutedEventArgs e) => Host.Show(typeof(FastFlagEditorPage));

        public SectionHost SectionHost => Host;

        public static void SelectTab(DependencyObject from, string pageName)
        {
            Type? target = SectionFor(pageName);

            if (target is null)
                return;

            DependencyObject? node = from;

            while (node is not null)
            {
                if (node is SectionHost host)
                {
                    host.Show(target);
                    return;
                }

                node = (node is Visual ? VisualTreeHelper.GetParent(node) : null) ?? LogicalTreeHelper.GetParent(node);
            }
        }

        private static Type? SectionFor(string pageName)
        {
            if (pageName.Contains(nameof(FastFlagEditorPage), StringComparison.OrdinalIgnoreCase))
                return typeof(FastFlagEditorPage);

            if (pageName.Contains(nameof(FastFlagGamesPage), StringComparison.OrdinalIgnoreCase))
                return typeof(FastFlagGamesPage);

            if (pageName.Contains(nameof(AssetWarpPage), StringComparison.OrdinalIgnoreCase))
                return typeof(AssetWarpPage);

            if (pageName.Contains(nameof(FastFlagsPage), StringComparison.OrdinalIgnoreCase))
                return typeof(FastFlagsPage);

            return null;
        }
    }
}
