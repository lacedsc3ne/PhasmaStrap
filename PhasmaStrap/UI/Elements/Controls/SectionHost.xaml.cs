using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;

namespace PhasmaStrap.UI.Elements.Controls
{
    public class SectionItem
    {
        public string Label { get; set; } = "";

        /// <summary>Heading this section is listed under in the side rail layout.</summary>
        public string Group { get; set; } = "";

        public Type? PageType { get; set; }
    }

    public enum SectionHostLayout
    {
        /// <summary>A title and a row of tabs above the page.</summary>
        Tabs,

        /// <summary>A side menu with optional group headings, used by Settings.</summary>
        Rail
    }

    [ContentProperty(nameof(Sections))]
    public partial class SectionHost : UserControl
    {
        private readonly Dictionary<Type, Page> _cache = new();

        private bool _layoutApplied;

        public ObservableCollection<SectionItem> Sections { get; } = new();

        public static readonly DependencyProperty LayoutProperty = DependencyProperty.Register(
            nameof(Layout), typeof(SectionHostLayout), typeof(SectionHost), new PropertyMetadata(SectionHostLayout.Tabs));

        public SectionHostLayout Layout
        {
            get => (SectionHostLayout)GetValue(LayoutProperty);
            set => SetValue(LayoutProperty, value);
        }

        public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
            nameof(Header), typeof(string), typeof(SectionHost), new PropertyMetadata(""));

        /// <summary>Page title shown beside the tabs, or above the side menu.</summary>
        public string Header
        {
            get => (string)GetValue(HeaderProperty);
            set => SetValue(HeaderProperty, value);
        }

        public SectionHost()
        {
            InitializeComponent();
            RailItems.ItemsSource = Sections;
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (!_layoutApplied)
            {
                ApplyLayout();
                _layoutApplied = true;
            }

            if (RailItems.SelectedIndex < 0 && Sections.Count > 0)
                RailItems.SelectedIndex = 0;
        }

        private void ApplyLayout()
        {
            bool single = Sections.Count <= 1;

            if (Layout == SectionHostLayout.Rail)
            {
                TabBar.Visibility = Visibility.Collapsed;
                Rail.Visibility = Visibility.Visible;
                RailColumn.Width = GridLength.Auto;
                RailTitle.Text = Header;
                RailTitle.Visibility = string.IsNullOrEmpty(Header) ? Visibility.Collapsed : Visibility.Visible;

                RailItems.ItemsPanel = (ItemsPanelTemplate)Resources["VerticalPanel"];
                RailItems.ItemContainerStyle = (Style)Resources["SectionRailItemStyle"];

                if (Sections.Any(s => !string.IsNullOrEmpty(s.Group)))
                {
                    ICollectionView view = CollectionViewSource.GetDefaultView(Sections);
                    if (view.GroupDescriptions.Count == 0)
                        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(SectionItem.Group)));
                    RailItems.GroupStyle.Clear();
                    RailItems.GroupStyle.Add((GroupStyle)Resources["RailGroupStyle"]);
                }

                Grid.SetRow(SectionFrame, 0);
                Grid.SetRowSpan(SectionFrame, 2);
                SectionFrame.Margin = new Thickness(18, 14, 4, 0);
                return;
            }

            // Tabs: move the list out of the rail and lay it out as a row of tabs.
            Rail.Visibility = Visibility.Collapsed;
            RailColumn.Width = new GridLength(0);

            RailSlot.Content = null;
            TabSlot.Content = RailItems;

            RailItems.ItemsPanel = (ItemsPanelTemplate)Resources["HorizontalPanel"];
            RailItems.ItemContainerStyle = (Style)Resources["SectionTabItemStyle"];
            ScrollViewer.SetVerticalScrollBarVisibility(RailItems, ScrollBarVisibility.Disabled);
            ScrollViewer.SetHorizontalScrollBarVisibility(RailItems, ScrollBarVisibility.Disabled);

            TabTitle.Text = Header;
            TabTitle.Visibility = string.IsNullOrEmpty(Header) ? Visibility.Collapsed : Visibility.Visible;
            TabBar.Visibility = single && string.IsNullOrEmpty(Header) ? Visibility.Collapsed : Visibility.Visible;
            RailItems.Visibility = single ? Visibility.Collapsed : Visibility.Visible;

            Grid.SetRow(SectionFrame, 1);
            Grid.SetRowSpan(SectionFrame, 1);
            SectionFrame.Margin = new Thickness(22, 0, 4, 0);
        }

        public void Show(Type pageType)
        {
            for (int i = 0; i < Sections.Count; i++)
            {
                if (Sections[i].PageType != pageType)
                    continue;

                RailItems.SelectedItem = Sections[i];
                return;
            }
        }

        private void RailItems_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (RailItems.SelectedItem is not SectionItem section || section.PageType is null)
                return;

            if (!_cache.TryGetValue(section.PageType, out Page? page))
            {
                object? created = Activator.CreateInstance(section.PageType);
                if (created is not Page made)
                    return;

                page = made;
                _cache[section.PageType] = page;
            }

            SectionFrame.Navigate(page);
        }
    }
}
