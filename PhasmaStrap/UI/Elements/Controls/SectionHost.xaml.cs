using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;

namespace PhasmaStrap.UI.Elements.Controls
{
    public class SectionItem : INotifyPropertyChanged
    {
        public string Label { get; set; } = "";

        private string _badge = "";

        /// <summary>Small count shown after the label (for example the number of captures). Empty hides it.</summary>
        public string Badge
        {
            get => _badge;
            set
            {
                value ??= "";
                if (_badge == value)
                    return;
                _badge = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Badge)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Heading this section is listed under in the side rail layout.</summary>
        public string Group { get; set; } = "";

        public Type? PageType { get; set; }

        /// <summary>
        /// When set, this page is not listed in the side menu. It is a tab on the page of that type instead.
        /// </summary>
        public Type? TabParent { get; set; }

        /// <summary>Text for this page when it is drawn as a tab. Empty uses the label.</summary>
        public string TabLabel { get; set; } = "";
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

        /// <summary>Name of the section a page is shown in (Performance, Settings). Pages read it to label their header.</summary>
        public static readonly DependencyProperty SectionNameProperty = DependencyProperty.RegisterAttached(
            "SectionName", typeof(string), typeof(SectionHost), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.Inherits));

        public static string GetSectionName(DependencyObject element) => (string)element.GetValue(SectionNameProperty);

        public static void SetSectionName(DependencyObject element, string value) => element.SetValue(SectionNameProperty, value);

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

        public static readonly DependencyProperty HeaderContentProperty = DependencyProperty.Register(
            nameof(HeaderContent), typeof(object), typeof(SectionHost), new PropertyMetadata(null, (d, e) => ((SectionHost)d).HeaderSlot.Content = e.NewValue));

        /// <summary>Optional content on the right of the tab bar (a status pill or a button). Tabs layout only.</summary>
        public object? HeaderContent
        {
            get => GetValue(HeaderContentProperty);
            set => SetValue(HeaderContentProperty, value);
        }

        /// <summary>The page type of the section being shown.</summary>
        public Type? CurrentSection { get; private set; }

        /// <summary>Raised after another section is shown.</summary>
        public event EventHandler? SectionChanged;

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

            SetSectionName(SectionFrame, Header);

            // The side menu lists the pages of a section, so neither layout draws its own tab bar or rail.
            Rail.Visibility = Visibility.Collapsed;
            TabBar.Visibility = Visibility.Collapsed;
            RailColumn.Width = new GridLength(0);
            Grid.SetRow(SectionFrame, 1);
            Grid.SetRowSpan(SectionFrame, 1);
            SectionFrame.Margin = new Thickness(4, 0, 4, 0);
            SectionFrame.LoadCompleted += (_, _) => Dispatcher.BeginInvoke(new Action(UpdateHostHeader), System.Windows.Threading.DispatcherPriority.ContextIdle);
            return;

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

        private static readonly Dictionary<string, string> Subtitles = new()
        {
            ["Friends"] = "See who is playing, join them, and choose which alerts you get.",
            ["Party"] = "Play together: start a party or join a friend's.",
            ["Accounts"] = "The Roblox accounts PhasmaStrap can launch with.",
            ["Activity"] = "What happened recently while you played.",
            ["Library"] = "Every clip and screenshot you captured.",
            ["Screenshots"] = "Take screenshots and manage the ones you have.",
            ["Instant Replay"] = "Keep the last moments of a game ready to save.",
            ["Storage"] = "Where captures are saved and how much room they take.",
            ["Roblox FFlags"] = "Presets and engine flags applied when Roblox starts.",
            ["Flag editor"] = "Every flag, edited by hand.",
            ["Per game flags"] = "Flag profiles that only apply to one game.",
            ["Asset Warp"] = "Swap game assets for your own files.",
            ["Deployment"] = "How PhasmaStrap launches and updates Roblox.",
            ["Roblox version"] = "Pick which Roblox version to run.",
            ["Channel and downloads"] = "Release channel and download behaviour.",
            ["Classic client"] = "Run an older client.",
            ["Mods"] = "Files that replace parts of Roblox.",
            ["Extensions"] = "Add-ons that run alongside Roblox.",
            ["Bootstrapper"] = "The window shown while Roblox starts.",
            ["Notifications"] = "Which messages PhasmaStrap shows you.",
            ["Hotkeys"] = "Keyboard shortcuts.",
            ["Shortcuts"] = "Desktop and Start menu shortcuts.",
            ["Account and backup"] = "Sign in and keep your settings safe.",
            ["Integrations"] = "Connect PhasmaStrap to other tools.",
            ["Game chat"] = "Chat options for in-game messages.",
            ["News"] = "What changed in each release.",
            ["Developer tools"] = "Tools for testing and debugging.",
            ["Diagnostics"] = "Checks and logs when something is wrong.",
            ["Low-end mode"] = "Settings that help weaker computers.",
            ["Boost options"] = "Extra tweaks that raise performance.",
            ["Stats HUD"] = "Numbers shown on screen while you play.",
            ["Crosshair"] = "A crosshair drawn over the game.",
            ["Stream-safe mode"] = "Hide private details while you stream.",
            ["Overlays"] = "Things drawn on top of the game.",
            ["Per game"] = "Give a game its own frame rate, resolution or flag profile.",
            ["Network"] = "See how your connection to Roblox is doing and tune how it behaves.",
            ["NVIDIA"] = "Driver settings for Roblox, applied to its own profile only.",
            ["Settings file"] = "Every key in the game's own settings file.",
            ["Developer"] = "Tools for people who build on Roblox.",
            ["Launcher window"] = "The window shown while Roblox starts.",
        };

        private bool _pageHasHeader;

        /// <summary>A page's own header card tells the host not to draw a second one.</summary>
        internal void NotifyPageHeader(PageHeader who)
        {
            if (ReferenceEquals(who, HostHeader))
                return;

            _pageHasHeader = true;
            HostHeader.Visibility = Visibility.Collapsed;
        }

        private void UpdateHostHeader()
        {
            if (SectionFrame.Content is not DependencyObject page)
                return;

            bool hasHeader = _pageHasHeader || HasHeader(page);
            string label = Sections.FirstOrDefault(s => s.PageType == CurrentSection)?.Label ?? "";

            if (hasHeader || label.Length == 0)
            {
                HostHeader.Visibility = Visibility.Collapsed;
                return;
            }

            // Older pages draw their own title and subtitle lines; the header card replaces them.
            string legacy = "";
            var titleStyle = TryFindResource("PhasmaPageTitle") as Style;
            var subtitleStyle = TryFindResource("PhasmaPageSubtitle") as Style;
            CollapseLegacy(page, titleStyle, subtitleStyle, ref legacy);

            HostHeader.Eyebrow = Header;
            HostHeader.Title = label;
            HostHeader.Subtitle = legacy.Length > 0 ? legacy : Subtitles.TryGetValue(label, out string? text) ? text : "";
            HostHeader.Visibility = Visibility.Visible;
        }

        private static void CollapseLegacy(DependencyObject parent, Style? title, Style? subtitle, ref string subtitleText)
        {
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is TextBlock block && block.Style is Style style)
                {
                    if (title is not null && ReferenceEquals(style, title))
                    {
                        block.Visibility = Visibility.Collapsed;

                        // The old title row also carried a small section chip ("Launching", "PhasmaStrap"). It goes with the title.
                        if (System.Windows.Media.VisualTreeHelper.GetParent(block) is StackPanel row && row.Orientation == Orientation.Horizontal
                            && row.Children.Cast<UIElement>().All(c => c is TextBlock || c is Border))
                            row.Visibility = Visibility.Collapsed;
                    }
                    else if (subtitle is not null && ReferenceEquals(style, subtitle))
                    {
                        if (subtitleText.Length == 0)
                            subtitleText = block.Text;
                        block.Visibility = Visibility.Collapsed;
                    }
                }

                CollapseLegacy(child, title, subtitle, ref subtitleText);
            }
        }

        private static bool HasHeader(DependencyObject parent)
        {
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is PageHeader || HasHeader(child))
                    return true;
            }

            return false;
        }

        /// <summary>The pages listed in the side menu: the ones that are not a tab of another page.</summary>
        public IEnumerable<SectionItem> NavSections => Sections.Where(s => s.TabParent is null);

        /// <summary>The page that owns the menu entry for the given page (the page itself unless it is a tab).</summary>
        public Type? NavKeyFor(Type? page) => Sections.FirstOrDefault(s => s.PageType == page)?.TabParent ?? page;

        /// <summary>The tabs to draw for the open page: its group, parent first. Empty when the page stands alone.</summary>
        public IReadOnlyList<SectionItem> TabsForCurrent()
        {
            Type? key = NavKeyFor(CurrentSection);
            if (key is null)
                return Array.Empty<SectionItem>();

            var group = Sections.Where(s => s.PageType == key || s.TabParent == key).OrderBy(s => s.TabParent is null ? 0 : 1).ToList();

            return group.Count > 1 ? group : Array.Empty<SectionItem>();
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

            _pageHasHeader = false;

            // A Frame does not pass inherited values on to its page, so give the page the section name itself.
            SetSectionName(page, Header);
            SectionFrame.Navigate(page);

            CurrentSection = section.PageType;
            SectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
