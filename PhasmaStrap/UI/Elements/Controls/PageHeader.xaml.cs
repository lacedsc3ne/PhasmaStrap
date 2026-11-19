using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace PhasmaStrap.UI.Elements.Controls
{
    [ContentProperty(nameof(Actions))]
    public partial class PageHeader : UserControl
    {
        public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
            nameof(Title), typeof(string), typeof(PageHeader), new PropertyMetadata("", (d, _) => ((PageHeader)d).Refresh()));

        public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
            nameof(Subtitle), typeof(string), typeof(PageHeader), new PropertyMetadata("", (d, _) => ((PageHeader)d).Refresh()));

        public static readonly DependencyProperty EyebrowProperty = DependencyProperty.Register(
            nameof(Eyebrow), typeof(string), typeof(PageHeader), new PropertyMetadata("", (d, _) => ((PageHeader)d).Refresh()));

        public static readonly DependencyProperty ActionsProperty = DependencyProperty.Register(
            nameof(Actions), typeof(object), typeof(PageHeader), new PropertyMetadata(null, (d, e) => ((PageHeader)d).ActionsPresenter.Content = e.NewValue));

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public string Subtitle
        {
            get => (string)GetValue(SubtitleProperty);
            set => SetValue(SubtitleProperty, value);
        }

        public string Eyebrow
        {
            get => (string)GetValue(EyebrowProperty);
            set => SetValue(EyebrowProperty, value);
        }

        public object? Actions
        {
            get => GetValue(ActionsProperty);
            set => SetValue(ActionsProperty, value);
        }

        public PageHeader()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                Refresh();

                DependencyObject? up = this;
                while (up is not null && up is not SectionHost)
                    up = System.Windows.Media.VisualTreeHelper.GetParent(up) ?? LogicalTreeHelper.GetParent(up);

                var host = up as SectionHost;
                host?.NotifyPageHeader(this);
                BuildTabs(host);
            };
        }

        private SectionHost? _tabHost;

        private void BuildTabs(SectionHost? host)
        {
            if (_tabHost is not null)
                _tabHost.SectionChanged -= OnHostSectionChanged;

            _tabHost = host;
            if (host is not null)
                host.SectionChanged += OnHostSectionChanged;

            RebuildTabs();
        }

        private void OnHostSectionChanged(object? sender, EventArgs e) => RebuildTabs();

        private void RebuildTabs()
        {
            TabStrip.Children.Clear();

            IReadOnlyList<SectionItem> tabs = _tabHost?.TabsForCurrent() ?? Array.Empty<SectionItem>();
            if (tabs.Count == 0)
            {
                TabLine.Visibility = Visibility.Collapsed;
                Card.Margin = new Thickness(0, 0, 0, 22);
                return;
            }

            Card.Margin = new Thickness(0, 0, 0, 12);
            TabLine.Visibility = Visibility.Visible;

            foreach (SectionItem tab in tabs)
            {
                var button = new System.Windows.Controls.RadioButton
                {
                    Content = tab.TabLabel.Length > 0 ? tab.TabLabel : tab.Label,
                    GroupName = "PageTabs" + GetHashCode(),
                    IsChecked = tab.PageType == _tabHost!.CurrentSection,
                    Style = (Style)FindResource("PhasmaTab"),
                    Tag = tab.PageType,
                };
                button.Click += (_, _) =>
                {
                    if (button.Tag is Type page)
                        _tabHost?.Show(page);
                };
                TabStrip.Children.Add(button);
            }
        }

        private void Refresh()
        {
            if (TitleText is null)
                return;

            TitleText.Text = (Title ?? "").ToUpperInvariant();

            SubtitleText.Text = Subtitle;
            SubtitleText.Visibility = string.IsNullOrEmpty(Subtitle) ? Visibility.Collapsed : Visibility.Visible;

            string eyebrow = string.IsNullOrEmpty(Eyebrow) ? SectionHost.GetSectionName(this) : Eyebrow;
            EyebrowText.Text = (eyebrow ?? "").ToUpperInvariant();
            ((FrameworkElement)EyebrowText.Parent).Visibility = string.IsNullOrEmpty(eyebrow) ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
