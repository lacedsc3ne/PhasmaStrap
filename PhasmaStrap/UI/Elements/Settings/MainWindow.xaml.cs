using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

using Wpf.Ui.Common;
using Wpf.Ui.Controls.Interfaces;
using Wpf.Ui.Controls.Navigation;
using Wpf.Ui.Mvvm.Contracts;

using PhasmaStrap.UI.ViewModels.Settings;

using NavigationItem = Wpf.Ui.Controls.NavigationItem;

namespace PhasmaStrap.UI.Elements.Settings
{
    public partial class MainWindow : INavigationWindow
    {
        private Models.Persistable.WindowState _state => App.State.Prop.SettingsWindow;

        private System.Windows.Forms.NotifyIcon? _trayIcon;

        private bool _exitRequested = false;

        private bool _closed;

        public MainWindow(bool showAlreadyRunningWarning)
        {
            var viewModel = new MainWindowViewModel();

            viewModel.RequestSaveNoticeEvent += (_, _) => SettingsSavedSnackbar.Show();
            viewModel.RequestCloseWindowEvent += (_, _) => Close();

            DataContext = viewModel;

            InitializeComponent();

            RootNavigation.Frame ??= RootFrame;

            App.Logger.WriteLine("MainWindow", "Initializing settings window");

            if (showAlreadyRunningWarning)
                ShowAlreadyRunningSnackbar();

            LoadState();

            if (App.Settings.Prop.ControllerNavigationEnabled)
                ControllerService.Initialize();

            InitializeSettingsSearch();
            InitializeAccountButton();
            InitializeTopBar();

            PhasmaStrap.UI.WheelRouter.Attach(this);
        }

        #region Top bar

        private string _savedSettingsSnapshot = "";

        private string? _savedSettingsHash;

        private string? _hashBeforeFieldSave;

        private readonly System.Windows.Threading.DispatcherTimer _unsavedTimer = new() { Interval = TimeSpan.FromSeconds(1) };

        private int _seenNotifications;

        private void InitializeTopBar()
        {
            var viewModel = (MainWindowViewModel)DataContext;
            viewModel.RequestSaveNoticeEvent += (_, _) => TakeSettingsSnapshot();
            App.Settings.FieldsSaved += Settings_FieldsSaved;
            Closed += (_, _) => App.Settings.FieldsSaved -= Settings_FieldsSaved;

            TakeSettingsSnapshot();
            _unsavedTimer.Tick += (_, _) => RefreshUnsavedBar();
            _unsavedTimer.Start();

            RootFrame.Navigated += (_, _) =>
            {
                RefreshSettingsButton();
                ApplySectionAccent();
                UpdateSubNav();
            };

            _seenNotifications = PhasmaStrap.UI.NotificationCenter.History.Count;
            PhasmaStrap.UI.NotificationCenter.HistoryChanged += NotificationCenter_HistoryChanged;
            RefreshNotificationsBadge();
        }

        private static string SerializeSettings()
        {
            try
            {
                return JsonSerializer.Serialize(App.Settings.Prop);
            }
            catch (Exception)
            {
                return "";
            }
        }

        private readonly object _snapshotLock = new();

        private void TakeSettingsSnapshot()
        {
            lock (_snapshotLock)
            {
                _savedSettingsSnapshot = SerializeSettings();
                _savedSettingsHash = App.Settings.LastFileHash;
            }
        }

        private bool FileChangedElsewhere()
        {
            lock (_snapshotLock)
            {
                string? now = App.Settings.LastFileHash;
                return !string.Equals(now, _savedSettingsHash, StringComparison.Ordinal) && !string.Equals(now, _hashBeforeFieldSave, StringComparison.Ordinal);
            }
        }

        private void Settings_FieldsSaved(string[] names, string newHash)
        {
            lock (_snapshotLock)
            {
                try
                {
                    var saved = System.Text.Json.Nodes.JsonNode.Parse(_savedSettingsSnapshot) as System.Text.Json.Nodes.JsonObject;
                    var current = JsonSerializer.SerializeToNode(App.Settings.Prop) as System.Text.Json.Nodes.JsonObject;

                    if (saved is null || current is null)
                        return;

                    foreach (string name in names)
                    {
                        if (current.TryGetPropertyValue(name, out System.Text.Json.Nodes.JsonNode? value))
                            saved[name] = value is null ? null : System.Text.Json.Nodes.JsonNode.Parse(value.ToJsonString());
                    }

                    _savedSettingsSnapshot = saved.ToJsonString();
                    _hashBeforeFieldSave = _savedSettingsHash;
                    _savedSettingsHash = newHash;
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine("MainWindow::FieldsSaved", $"Could not update the saved settings baseline: {ex.Message}");
                }
            }
        }

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // The side menu has a fixed width, so there is nothing to shrink as the window narrows.
            // the brand lives in the side menu now
            AccountTextPanel.Visibility = Visibility.Visible;
        }

        private bool HasUnsavedChanges()
        {
            if (App.FastFlags.Changed || App.FlagProfiles.Changed || App.PendingSettingTasks.Any(x => x.Value.Changed))
                return true;

            string now = SerializeSettings();

            lock (_snapshotLock)
                return now.Length > 0 && _savedSettingsSnapshot.Length > 0 && now != _savedSettingsSnapshot;
        }

        private bool HasUnsavedChangesOnClose()
        {
            if (FileChangedElsewhere())
                TakeSettingsSnapshot();

            try
            {
                return HasUnsavedChanges();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("MainWindow::Closing", $"Could not check for unsaved changes: {ex.Message}");
                return false;
            }
        }

        private void RefreshUnsavedBar()
        {
            if (!IsVisible || WindowState == System.Windows.WindowState.Minimized)
                return;

            // Pages that save on their own (deferred saves) move the file on; treat that as the new baseline.
            if (FileChangedElsewhere())
                TakeSettingsSnapshot();

            bool dirty;
            try
            {
                dirty = HasUnsavedChanges();
            }
            catch (Exception)
            {
                dirty = false;
            }

            Visibility wanted = dirty ? Visibility.Visible : Visibility.Collapsed;
            if (UnsavedBar.Visibility == wanted)
                return;

            UnsavedBar.Visibility = wanted;
            if (dirty)
                UnsavedBar.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        }

        private void RefreshSettingsButton()
        {
            bool onSettings = RootFrame.Content is Pages.SettingsPage;

            if (onSettings)
                SettingsButton.SetResourceReference(BackgroundProperty, "PhasmaSelectedBrush");
            else
                SettingsButton.ClearValue(BackgroundProperty);

            SettingsButtonIcon.Filled = onSettings;
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e) => RootNavigation.Navigate("settings");

        /// <summary>
        /// Each section has its own accent colour (Games red, Social blue, Performance green and so on). Turning section
        /// colours off in Appearance goes back to the single accent the user picked.
        /// </summary>
        private PhasmaStrap.UI.ViewModels.Settings.HomeViewModel? _watchedHome;
        private UI.Elements.Controls.SectionHost? _watchedHost;

        private static readonly (string Tag, string Label)[] GamesLinks =
        {
            ("home", "Home"), ("library", "Library"), ("serverbrowser", "Server browser"), ("privateservers", "Private servers"), ("history", "History"),
        };

        /// <summary>Lists the pages of the open section under its menu entry and keeps the highlighted one in step with the page.</summary>
        private void UpdateSubNav()
        {
            if (_watchedHome is not null)
            {
                _watchedHome.PropertyChanged -= WatchedHome_PropertyChanged;
                _watchedHome = null;
            }

            if (_watchedHost is not null)
            {
                _watchedHost.SectionChanged -= WatchedHost_SectionChanged;
                _watchedHost = null;
            }

            var entries = new List<(object Tag, string Label, string Group)>();

            if (RootFrame.Content is Pages.HomePage home && home.DataContext is PhasmaStrap.UI.ViewModels.Settings.HomeViewModel vm)
            {
                foreach ((string tag, string label) in GamesLinks)
                    entries.Add((tag, label, ""));

                _watchedHome = vm;
                vm.PropertyChanged += WatchedHome_PropertyChanged;
            }
            else if (RootFrame.Content is UI.Elements.Controls.ISectionHostPage page)
            {
                foreach (UI.Elements.Controls.SectionItem section in page.SectionHost.NavSections)
                    if (section.PageType is not null)
                        entries.Add((section.PageType, section.Label, ""));

                _watchedHost = page.SectionHost;
                _watchedHost.SectionChanged += WatchedHost_SectionChanged;
            }

            // Every menu entry gives its space back, then the open one reserves room for its links.
            FrameworkElement? open = null;
            int openIndex = -1;
            var all = new List<FrameworkElement>();
            foreach (object item in RootNavigation.Items)
                if (item is FrameworkElement fe) all.Add(fe);
            int mainCount = all.Count;
            foreach (object item in RootNavigation.Footer)
                if (item is FrameworkElement fe) all.Add(fe);

            for (int n = 0; n < all.Count; n++)
            {
                all[n].Margin = new Thickness(0);
                if (all[n] is NavigationItem nav && nav.PageType == RootFrame.Content?.GetType())
                {
                    open = all[n];
                    openIndex = n;
                }
            }

            SubNavItems.Children.Clear();
            if (open is null || entries.Count == 0)
            {
                SubNav.Visibility = Visibility.Collapsed;
                return;
            }

            string lastGroup = "";
            double height = 0;
            foreach ((object tag, string label, string group) in entries)
            {
                if (group.Length > 0 && group != lastGroup)
                {
                    SubNavItems.Children.Add(new TextBlock
                    {
                        Text = group,
                        FontSize = 10.5,
                        FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(10, 8, 0, 3),
                        Foreground = (System.Windows.Media.Brush)FindResource("TextFillColorTertiaryBrush"),
                        Opacity = 0.8,
                    });
                    height += 24;
                    lastGroup = group;
                }

                var link = new System.Windows.Controls.RadioButton
                {
                    Content = label,
                    Tag = tag,
                    GroupName = "SubNav",
                    Style = (Style)FindResource("SubLink"),
                };
                link.Click += SubLink_Click;
                SubNavItems.Children.Add(link);
                height += 28;
            }

            open.Margin = new Thickness(0, 0, 0, height + 4);
            double top = (openIndex + 1) * 40 + (openIndex >= mainCount ? 2 : 0);
            SubNav.Margin = new Thickness(18, top, 0, 0);
            SubNav.Visibility = Visibility.Visible;

            SyncSubNav();
        }

        private void WatchedHome_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PhasmaStrap.UI.ViewModels.Settings.HomeViewModel.Tab))
                SyncSubNav();
        }

        private void WatchedHost_SectionChanged(object? sender, EventArgs e) => SyncSubNav();

        private void SyncSubNav()
        {
            object? current = null;

            if (_watchedHome is not null)
                current = _watchedHome.Tab;
            else if (_watchedHost is not null)
                current = _watchedHost.NavKeyFor(_watchedHost.CurrentSection);

            foreach (UIElement child in SubNavItems.Children)
            {
                if (child is System.Windows.Controls.RadioButton rb)
                    rb.IsChecked = current is not null && (current is string s ? string.Equals(rb.Tag as string, s, StringComparison.OrdinalIgnoreCase) : Equals(rb.Tag, current));
            }
        }

        private void SubLink_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.RadioButton rb)
                return;

            if (_watchedHome is not null)
                _watchedHome.ShowTab(rb.Tag as string);
            else if (_watchedHost is not null && rb.Tag is Type page)
                _watchedHost.Show(page);
        }

        public void ApplySectionAccent()
        {
            System.Windows.Media.Color? wanted = null;

            if (App.Settings.Prop.ThemeSectionColors)
            {
                wanted = RootFrame.Content switch
                {
                    Pages.HomePage => ThemeTokens.DefaultAccent,
                    Pages.PeoplePage => System.Windows.Media.Color.FromRgb(0x5B, 0x8C, 0xFF),
                    Pages.PerformancePage => System.Windows.Media.Color.FromRgb(0x34, 0xD3, 0x99),
                    Pages.FastFlagSettingsPage => System.Windows.Media.Color.FromRgb(0xF5, 0xB8, 0x41),
                    Pages.CapturePage => System.Windows.Media.Color.FromRgb(0xA0, 0x7B, 0xFF),
                    Pages.SettingsPage => System.Windows.Media.Color.FromRgb(0xC9, 0xCC, 0xD4),
                    _ => null,
                };
            }

            if (Nullable.Equals(ThemeTokens.SectionAccent, wanted))
                return;

            ThemeTokens.SectionAccent = wanted;
            Base.WpfUiWindow.ApplyAccentFrom(App.Settings.Prop.CustomColorThemeEnabled ? PhasmaStrap.Utility.AppColorTheme.LoadForApp() : null);
        }

        private void LaunchButton_Click(object sender, RoutedEventArgs e) => ((MainWindowViewModel)DataContext).SaveAndLaunchCommand.Execute(null);

        private void SaveMenuItem_Click(object sender, RoutedEventArgs e) => ((MainWindowViewModel)DataContext).SaveSettingsCommand.Execute(null);

        private void RestartMenuItem_Click(object sender, RoutedEventArgs e) => ((MainWindowViewModel)DataContext).RestartCommand.Execute(null);

        private void DiscardButton_Click(object sender, RoutedEventArgs e) => ((MainWindowViewModel)DataContext).DiscardCommand.Execute(null);

        private void About_Click(object sender, RoutedEventArgs e) => ((MainWindowViewModel)DataContext).OpenAboutCommand.Execute(null);

        private void LaunchMenuButton_Click(object sender, RoutedEventArgs e)
        {
            TestModeMenuItem.IsChecked = ((MainWindowViewModel)DataContext).TestModeEnabled;
            LaunchMenu.PlacementTarget = LaunchMenuButton;
            LaunchMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            LaunchMenu.IsOpen = true;
        }

        private void TestModeMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var viewModel = (MainWindowViewModel)DataContext;
            viewModel.TestModeEnabled = TestModeMenuItem.IsChecked;
            TestModeMenuItem.IsChecked = viewModel.TestModeEnabled;
        }

        private void NotificationCenter_HistoryChanged(object? sender, EventArgs e) =>
            Dispatcher.BeginInvoke(RefreshNotificationsBadge);

        private void RefreshNotificationsBadge()
        {
            int count = PhasmaStrap.UI.NotificationCenter.History.Count;
            if (count < _seenNotifications)
                _seenNotifications = count;

            NotificationsBadge.Visibility = count > _seenNotifications ? Visibility.Visible : Visibility.Collapsed;

            if (NotificationsPopup.IsOpen)
                FillNotifications(Math.Max(0, count - _seenNotifications));
        }

        // One line in the notification history popup
        public sealed class NotificationRow
        {
            public NotificationRow(PhasmaStrap.UI.NotificationRecord record, bool isNew)
            {
                Record = record;
                NewVisibility = isNew ? Visibility.Visible : Visibility.Collapsed;
            }

            public PhasmaStrap.UI.NotificationRecord Record { get; }

            public string Title => Record.Title;

            public DateTime Timestamp => Record.Timestamp;

            public Visibility NewVisibility { get; }

            public string Detail => string.IsNullOrWhiteSpace(Record.Message) ? Age(Record.Timestamp) : $"{Record.Message} · {Age(Record.Timestamp)}";

            private static string Age(DateTime when)
            {
                TimeSpan age = DateTime.Now - when;

                if (age.TotalMinutes < 1)
                    return "now";

                if (age.TotalHours < 1)
                    return $"{(int)age.TotalMinutes} min";

                if (when.Date == DateTime.Today)
                    return $"{(int)age.TotalHours} h";

                if (when.Date == DateTime.Today.AddDays(-1))
                    return "Yesterday";

                return when.ToString("d MMM");
            }
        }

        private void FillNotifications(int newCount = 0)
        {
            var history = PhasmaStrap.UI.NotificationCenter.History.OrderByDescending(x => x.Timestamp).ToList();
            NotificationsList.ItemsSource = history.Select((record, index) => new NotificationRow(record, index < newCount)).ToList();
            NotificationsList.Visibility = history.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            NotificationsEmpty.Visibility = history.Count > 0 ? Visibility.Collapsed : Visibility.Visible;

            NotificationsNewText.Text = $"{newCount} new";
            NotificationsNewTag.Visibility = newCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            DoNotDisturbToggle.IsChecked = App.Settings.Prop.DoNotDisturbEnabled;
        }

        private long _notificationsClosedAt;

        private void NotificationsPopup_Closed(object? sender, EventArgs e) => _notificationsClosedAt = Environment.TickCount64;

        private void NotificationsButton_Click(object sender, RoutedEventArgs e)
        {
            if (!NotificationsPopup.IsOpen && Environment.TickCount64 - _notificationsClosedAt < 300)
                return;

            int count = PhasmaStrap.UI.NotificationCenter.History.Count;
            FillNotifications(Math.Max(0, count - _seenNotifications));
            NotificationsPopup.IsOpen = !NotificationsPopup.IsOpen;

            _seenNotifications = count;
            NotificationsBadge.Visibility = Visibility.Collapsed;
        }

        private void ClearNotifications_Click(object sender, RoutedEventArgs e)
        {
            PhasmaStrap.UI.NotificationCenter.ClearHistory();
            _seenNotifications = 0;
            FillNotifications();
        }

        private void SendTestNotification_Click(object sender, RoutedEventArgs e) =>
            PhasmaStrap.UI.NotificationCenter.Notify("Test notification", "This is what a PhasmaStrap toast looks like.", PhasmaStrap.UI.NotificationCategory.General);

        private void DoNotDisturbToggle_Click(object sender, RoutedEventArgs e) =>
            App.Settings.Prop.DoNotDisturbEnabled = DoNotDisturbToggle.IsChecked == true;

        private void NotificationSettings_Click(object sender, RoutedEventArgs e)
        {
            NotificationsPopup.IsOpen = false;
            Navigate(typeof(Pages.NotificationsPage));
        }

        private void NotificationsList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is not DependencyObject source)
                return;

            DependencyObject? current = source;
            while (current is not null && current is not ListBoxItem)
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);

            if (current is ListBoxItem item && item.DataContext is NotificationRow row && row.Record.OnClick is not null)
            {
                NotificationsPopup.IsOpen = false;

                try
                {
                    row.Record.OnClick();
                }
                catch (Exception ex)
                {
                    App.Logger.WriteException("MainWindow::NotificationsList", ex);
                }
            }
        }

        #endregion Top bar

        #region Settings search

        private readonly System.Windows.Threading.DispatcherTimer _searchDebounce = new() { Interval = TimeSpan.FromMilliseconds(60) };

        private bool _suppressSearchClose;

        private void InitializeSettingsSearch()
        {
            _searchDebounce.Tick += (_, _) =>
            {
                _searchDebounce.Stop();
                RunSettingsSearch();
            };

            _ = Task.Run(() => Search.SettingsSearchIndex.Entries);

            PreviewKeyDown += MainWindow_SearchShortcut;
            Deactivated += (_, _) => CloseSearchPopup();
        }

        private void MainWindow_SearchShortcut(object sender, KeyEventArgs e)
        {
            if (Keyboard.FocusedElement is FrameworkElement focused && focused.Tag is HotkeyRow)
                return;

            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            bool inTextInput = Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase;

            bool searchShortcut = (ctrl && (e.Key == Key.F || e.Key == Key.K))
                || (!inTextInput && Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.OemQuestion);

            if (searchShortcut)
            {
                OpenSearchPalette(selectAll: true);
                e.Handled = true;
            }
        }

        internal static string RecentKey(Search.SettingsSearchEntry entry) => $"{entry.Kind}|{entry.PageType.Name}|{entry.Tab}|{entry.Section}|{entry.Group}|{entry.Header}";

        private static void RememberRecent(Search.SettingsSearchEntry entry)
        {
            try
            {
                var recents = App.State.Prop.RecentSettingsSearches;
                string key = RecentKey(entry);
                recents.Remove(key);
                recents.Insert(0, key);
                while (recents.Count > 8)
                    recents.RemoveAt(recents.Count - 1);
                App.State.Save();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("MainWindow", $"Could not save recent searches: {ex.Message}");
            }
        }

        private static void RememberQuery(string? text)
        {
            string query = (text ?? "").Trim();
            if (query.Length < 2 || query.Length > 60)
                return;

            try
            {
                var queries = App.State.Prop.RecentSearchQueries;
                queries.RemoveAll(q => string.Equals(q, query, StringComparison.OrdinalIgnoreCase));
                queries.Insert(0, query);
                while (queries.Count > 8)
                    queries.RemoveAt(queries.Count - 1);
                App.State.Save();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("MainWindow", $"Could not save recent search words: {ex.Message}");
            }
        }

        private static List<Search.SettingsSearchResult> RecentResults()
        {
            var results = new List<Search.SettingsSearchResult>();

            var byKey = new Dictionary<string, Search.SettingsSearchEntry>(StringComparer.Ordinal);
            foreach (Search.SettingsSearchEntry entry in Search.SettingsSearchIndex.Entries)
                byKey.TryAdd(RecentKey(entry), entry);

            foreach (string key in App.State.Prop.RecentSettingsSearches)
            {
                if (byKey.TryGetValue(key, out var entry))
                    results.Add(new Search.SettingsSearchResult(entry, 0));
            }

            return results;
        }

        private bool _accountBusy;

        private void PhasmaAccount_Changed(object? sender, EventArgs e)
        {
            if (_closed)
                return;

            Dispatcher.BeginInvoke(new Action(RefreshAccountButton));
        }

        private void InitializeAccountButton()
        {
            PhasmaStrap.Utility.PhasmaAccount.Changed += PhasmaAccount_Changed;
            RefreshAccountButton();

            if (PhasmaStrap.Utility.PhasmaAccount.SignedIn)
                _ = PhasmaStrap.Utility.PhasmaAccount.RefreshAsync();
        }

        private void RefreshAccountButton()
        {
            bool signedIn = PhasmaStrap.Utility.PhasmaAccount.SignedIn;

            AccountLabel.Text = _accountBusy ? "Signing in..."
                : signedIn ? PhasmaStrap.Utility.PhasmaAccount.DisplayName
                : "Log in";

            AccountChevron.Visibility = signedIn && !_accountBusy ? Visibility.Visible : Visibility.Collapsed;
            AccountButton.ToolTip = signedIn ? "Your PhasmaStrap account" : "Sign in to PhasmaStrap";
            AccountWhoItem.Header = signedIn ? $"Signed in as {PhasmaStrap.Utility.PhasmaAccount.DisplayName}" : "Not signed in";

            string avatar = App.State.Prop.AccountAvatar;
            bool hasAvatar = signedIn && !string.IsNullOrEmpty(avatar);

            if (hasAvatar)
            {
                try
                {
                    AccountAvatarBrush.ImageSource = new System.Windows.Media.Imaging.BitmapImage(new Uri(avatar, UriKind.Absolute));
                }
                catch (Exception ex)
                {
                    App.Logger.WriteException("MainWindow::RefreshAccountButton", ex);
                    hasAvatar = false;
                }
            }

            AccountAvatar.Visibility = hasAvatar ? Visibility.Visible : Visibility.Collapsed;
            AccountIcon.Visibility = hasAvatar ? Visibility.Collapsed : Visibility.Visible;
        }

        private async void AccountButton_Click(object sender, RoutedEventArgs e)
        {
            if (_accountBusy)
                return;

            if (PhasmaStrap.Utility.PhasmaAccount.SignedIn)
            {
                AccountMenu.PlacementTarget = AccountButton;
                AccountMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                AccountMenu.IsOpen = true;
                return;
            }

            _accountBusy = true;
            RefreshAccountButton();

            try
            {
                using var cancel = new CancellationTokenSource(TimeSpan.FromMinutes(11));
                await PhasmaStrap.Utility.PhasmaAccount.SignInAsync(null, cancel.Token);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("MainWindow::AccountButton_Click", ex);
            }
            finally
            {
                _accountBusy = false;
                RefreshAccountButton();
            }
        }

        private void AccountSettings_Click(object sender, RoutedEventArgs e) => Navigate(typeof(Pages.PhasmaStrapPage));

        private void AccountWebsite_Click(object sender, RoutedEventArgs e) => Utilities.ShellExecute("https://phasmastrap.com/account");

        private async void AccountSignOut_Click(object sender, RoutedEventArgs e)
        {
            await PhasmaStrap.Utility.PhasmaAccount.SignOutAsync();
            RefreshAccountButton();
        }

        private Search.PaletteFilter _searchFilter = Search.PaletteFilter.All;

        private Point _lastSearchMouse;

        private void SettingsSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchDebounce.Stop();
            _searchDebounce.Start();
        }

        private void SearchChip_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: string tag } && Enum.TryParse(tag, out Search.PaletteFilter filter))
                SetSearchFilter(filter);

            PaletteBox.Focus();
        }

        private void SetSearchFilter(Search.PaletteFilter filter)
        {
            _searchFilter = filter;

            if ((SettingsSearchBox.Text ?? "").Trim().Length > 0)
                RunSettingsSearch();
            else
                UpdateSearchChips(null);
        }

        private void CycleSearchFilter(bool backwards)
        {
            int count = Enum.GetValues<Search.PaletteFilter>().Length;
            int next = ((int)_searchFilter + (backwards ? count - 1 : 1)) % count;
            SetSearchFilter((Search.PaletteFilter)next);
        }

        private void UpdateSearchChips(Search.PaletteResults? results)
        {
            SearchChips.Visibility = results is null ? Visibility.Collapsed : Visibility.Visible;

            SearchChipAll.IsChecked = _searchFilter == Search.PaletteFilter.All;
            SearchChipSettings.IsChecked = _searchFilter == Search.PaletteFilter.Settings;
            SearchChipFlags.IsChecked = _searchFilter == Search.PaletteFilter.Flags;
            SearchChipGames.IsChecked = _searchFilter == Search.PaletteFilter.Games;

            if (results is null)
                return;

            SearchChipAll.Content = $"All  {results.Total}";
            SearchChipSettings.Content = $"Settings  {results.SettingCount}";
            SearchChipFlags.Content = $"Your flags  {results.FlagCount}";
            SearchChipGames.Content = $"Games  {results.GameCount}";
        }

        private void ShowSearchItems(List<Search.PaletteItem> items)
        {
            var view = new System.Windows.Data.ListCollectionView(items);
            view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription(nameof(Search.PaletteItem.Group)));

            SettingsSearchResults.ItemsSource = view;
            SettingsSearchResults.SelectedIndex = items.Count > 0 ? 0 : -1;
            SearchDetail.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Hidden;

            if (items.Count > 0)
                SettingsSearchResults.ScrollIntoView(SettingsSearchResults.SelectedItem);
        }

        private void RunSettingsSearch()
        {
            string query = SettingsSearchBox.Text ?? "";

            if (query.Trim().Length == 0)
            {
                ShowRecentSearches();
                return;
            }

            var recent = new HashSet<string>(App.State.Prop.RecentSettingsSearches, StringComparer.Ordinal);
            Search.PaletteResults results = Search.PaletteSearch.Search(query, _searchFilter, recent, RecentKey);

            ShowSearchItems(results.Items);
            UpdateSearchChips(results);

            int shown = results.Items.Count;
            if (shown == 0)
            {
                SettingsSearchEmpty.Text = results.Total > 0
                    ? $"Nothing here for \"{query.Trim()}\".\nPress Tab to see the other {results.Total} results."
                    : $"Nothing matches \"{query.Trim()}\".\nTry a plainer word, like fps, sound or theme.";
                SettingsSearchEmpty.Visibility = Visibility.Visible;
            }
            else
            {
                SettingsSearchEmpty.Visibility = Visibility.Collapsed;
            }

            string counted = shown == 1 ? "1 result" : $"{shown} results";
            IReadOnlyList<string> also = Search.SettingsSearchEngine.AlsoMatching(query);
            SettingsSearchFooter.Text = also.Count > 0 ? $"{counted}  ·  also matching {string.Join(", ", also)}" : counted;

            SearchOpen = true;
        }

        private void ShowRecentSearches()
        {
            if (!SettingsSearchBox.IsKeyboardFocusWithin && !PaletteBox.IsKeyboardFocusWithin)
            {
                CloseSearchPopup();
                return;
            }

            ShowSearchItems(Search.PaletteSearch.StartItems(RecentResults(), App.State.Prop.RecentSearchQueries));
            UpdateSearchChips(null);
            SettingsSearchEmpty.Visibility = Visibility.Collapsed;
            SettingsSearchFooter.Text = "Type to search settings, your flags and games you've played";
            SearchOpen = true;
        }

        private bool SearchOpen
        {
            get => SearchOverlay.Visibility == Visibility.Visible;
            set
            {
                SearchOverlay.Visibility = value ? Visibility.Visible : Visibility.Collapsed;

                if (value)
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(FocusPaletteBox));
            }
        }

        private void FocusPaletteBox()
        {
            if (!SearchOpen || PaletteBox.IsKeyboardFocusWithin)
                return;

            PaletteBox.Focus();
            Keyboard.Focus(PaletteBox);
            PaletteBox.CaretIndex = PaletteBox.Text.Length;
        }

        private void OpenSearchPalette(bool selectAll)
        {
            if (!SearchOpen)
            {
                if ((SettingsSearchBox.Text ?? "").Trim().Length > 0)
                    RunSettingsSearch();
                else
                    ShowRecentSearches();
            }

            SearchOpen = true;
            FocusPaletteBox();

            if (selectAll)
                PaletteBox.SelectAll();
        }

        private void SearchScrim_MouseDown(object sender, MouseButtonEventArgs e)
        {
            CloseSearchPopup();
            e.Handled = true;
        }

        private void CloseSearchPopup()
        {
            if (_suppressSearchClose)
                return;

            SearchOpen = false;
        }

        private void SettingsSearchBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (!SearchOpen)
                OpenSearchPalette(selectAll: false);
        }

        private void SettingsSearchBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (e.NewFocus is DependencyObject target && (IsInsidePopup(target) || ReferenceEquals(target, SettingsSearchBox)))
                return;

            CloseSearchPopup();
        }

        private bool IsInsidePopup(DependencyObject element)
        {
            DependencyObject? current = element;
            while (current is not null)
            {
                if (ReferenceEquals(current, SearchPanel))
                    return true;

                current = current is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
            }

            return false;
        }

        private void MoveSearchSelection(int by)
        {
            int count = SettingsSearchResults.Items.Count;
            if (count == 0)
                return;

            if (!SearchOpen)
                SearchOpen = true;

            SettingsSearchResults.SelectedIndex = Math.Clamp(SettingsSearchResults.SelectedIndex + by, 0, count - 1);
            SettingsSearchResults.ScrollIntoView(SettingsSearchResults.SelectedItem);
        }

        private void SettingsSearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Down:
                    MoveSearchSelection(1);
                    e.Handled = true;
                    break;

                case Key.Up:
                    MoveSearchSelection(-1);
                    e.Handled = true;
                    break;

                case Key.PageDown:
                    MoveSearchSelection(8);
                    e.Handled = true;
                    break;

                case Key.PageUp:
                    MoveSearchSelection(-8);
                    e.Handled = true;
                    break;

                case Key.Tab when SearchOpen && (SettingsSearchBox.Text ?? "").Trim().Length > 0:
                    CycleSearchFilter((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift);
                    if (!PaletteBox.IsKeyboardFocusWithin)
                        PaletteBox.Focus();
                    e.Handled = true;
                    break;

                case Key.Enter:
                    if (_searchDebounce.IsEnabled)
                    {
                        _searchDebounce.Stop();
                        RunSettingsSearch();
                    }

                    if (SettingsSearchResults.SelectedItem is Search.PaletteItem selected)
                        ActivateSearchResult(selected);
                    else if (SettingsSearchResults.Items.Count > 0 && SettingsSearchResults.Items[0] is Search.PaletteItem first)
                        ActivateSearchResult(first);

                    e.Handled = true;
                    break;

                case Key.Escape:
                    if (SearchOpen)
                        CloseSearchPopup();
                    else
                        SettingsSearchBox.Text = "";
                    e.Handled = true;
                    break;
            }
        }

        private static ListBoxItem? SearchItemAt(object? source)
        {
            DependencyObject? current = source as DependencyObject;
            while (current is not null && current is not ListBoxItem)
                current = current is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);

            return current as ListBoxItem;
        }

        private void SettingsSearchResults_MouseMove(object sender, MouseEventArgs e)
        {
            // Only follow the mouse when it really moved, so scrolling with the keyboard doesn't fight it.
            Point position = e.GetPosition(SettingsSearchResults);
            if (position == _lastSearchMouse)
                return;

            _lastSearchMouse = position;

            if (SearchItemAt(e.OriginalSource) is ListBoxItem item && item.DataContext is Search.PaletteItem hovered && !ReferenceEquals(SettingsSearchResults.SelectedItem, hovered))
                SettingsSearchResults.SelectedItem = hovered;
        }

        private void SettingsSearchResults_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (SearchItemAt(e.OriginalSource) is ListBoxItem item && item.DataContext is Search.PaletteItem result)
            {
                ActivateSearchResult(result);
                e.Handled = true;
            }
        }

        private void SearchDetailOpen_Click(object sender, RoutedEventArgs e)
        {
            if (SettingsSearchResults.SelectedItem is Search.PaletteItem item)
                ActivateSearchResult(item);
        }

        private void ActivateSearchResult(Search.PaletteItem item)
        {
            // "Recent searches": the words that led to this result
            if (item.Category != Search.PaletteCategory.Query)
                RememberQuery(SettingsSearchBox.Text);

            _suppressSearchClose = true;
            try
            {
                SearchOpen = false;
                SettingsSearchBox.Text = "";
                SettingsSearchResults.ItemsSource = null;
                _searchDebounce.Stop();
                _searchFilter = Search.PaletteFilter.All;
            }
            finally
            {
                _suppressSearchClose = false;
            }

            switch (item.Category)
            {
                case Search.PaletteCategory.Setting when item.Setting is not null:
                    RememberRecent(item.Setting.Entry);
                    Search.SettingsSearchNavigator.Reveal(RootNavigation, RootFrame, item.Setting.Entry);
                    break;

                case Search.PaletteCategory.Page when item.PageType is not null:
                    Search.SettingsSearchNavigator.OpenPage(RootNavigation, RootFrame, item.PageType);
                    break;

                case Search.PaletteCategory.Flag when item.FlagName is not null:
                    string flag = item.FlagName;
                    Search.SettingsSearchNavigator.OpenPage(RootNavigation, RootFrame, typeof(Pages.FastFlagEditorPage), page =>
                    {
                        if (page is Pages.FastFlagEditorPage editor)
                            editor.FocusFlag(flag);
                    });
                    break;

                case Search.PaletteCategory.Query when item.Header.Length > 0:
                    string words = item.Header;
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => SearchFor(words)));
                    break;

                case Search.PaletteCategory.Game when item.Game is not null:
                    PlayTimeEntry game = item.Game;
                    Search.SettingsSearchNavigator.OpenPage(RootNavigation, RootFrame, typeof(Pages.HomePage), page =>
                    {
                        if (page.DataContext is HomeViewModel home)
                            home.OpenGame(game.UniverseId, game.PlaceId, game.Name, game.IconUrl);
                    });
                    break;
            }
        }

        /// <summary>
        /// Brings the window forward and shows the setting a shared link points at (phasmastrap://settings/... or
        /// https://phasmastrap.com/s/...). Safe to call before the window has loaded.
        /// </summary>
        public void OpenSettingLink(string link)
        {
            if (_closed)
                return;

            string? key = Search.SettingLink.KeyFromLink(link);
            Search.SettingsSearchEntry? entry = key is null ? null : Search.SettingLink.Resolve(key);

            App.Logger.WriteLine("MainWindow", entry is null
                ? $"Setting link did not match anything: {link}"
                : $"Opening setting link for {entry.PageType.Name} / {entry.Header}");

            void Go()
            {
                if (_closed)
                    return;

                if (!IsVisible)
                {
                    RestoreFromTray();
                }
                else
                {
                    if (WindowState == System.Windows.WindowState.Minimized)
                        WindowState = ReadPlacement().Maximized ? System.Windows.WindowState.Maximized : System.Windows.WindowState.Normal;
                    Activate();
                }

                if (entry is null)
                {
                    Frontend.ShowMessageBox("This link points to a setting that this version of PhasmaStrap doesn't have.", MessageBoxImage.Information);
                    return;
                }

                Search.SettingsSearchNavigator.Reveal(RootNavigation, RootFrame, entry);
            }

            if (IsLoaded)
            {
                Dispatcher.BeginInvoke(Go, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                return;
            }

            RoutedEventHandler? onLoaded = null;
            onLoaded = (_, _) =>
            {
                Loaded -= onLoaded;
                Dispatcher.BeginInvoke(Go, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            };
            Loaded += onLoaded;
        }

        /// <summary>Puts <paramref name="text"/> in the top bar search and opens the results.</summary>
        public void SearchFor(string text)
        {
            SettingsSearchBox.Text = text ?? "";
            OpenSearchPalette(selectAll: false);
        }

        #endregion Settings search


        private static string PlacementPath => Path.Combine(Paths.Base, "SettingsWindow.json");

        private static bool IsUiTest => Environment.GetEnvironmentVariable("PHASMASTRAP_UITEST_BACKGROUND") == "1";

        private System.Windows.Threading.DispatcherTimer? _placementTimer;

        private Models.Persistable.WindowState ReadPlacement()
        {
            try
            {
                if (File.Exists(PlacementPath))
                    return JsonSerializer.Deserialize<Models.Persistable.WindowState>(File.ReadAllText(PlacementPath)) ?? new();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("MainWindow", $"Window placement unreadable: {ex.Message}");
            }

            return new Models.Persistable.WindowState { Width = _state.Width, Height = _state.Height, Left = _state.Left, Top = _state.Top };
        }

        public void LoadState()
        {
            Models.Persistable.WindowState placement = ReadPlacement();

            if (placement.Width >= MinWidth && placement.Height >= MinHeight && IsOnAScreen(placement.Left, placement.Top, placement.Width, placement.Height))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = placement.Left;
                Top = placement.Top;
                Width = placement.Width;
                Height = placement.Height;
            }
            else if (placement.Width >= MinWidth && placement.Height >= MinHeight)
            {
                Width = Math.Min(placement.Width, SystemParameters.WorkArea.Width);
                Height = Math.Min(placement.Height, SystemParameters.WorkArea.Height);
            }

            if (placement.Maximized)
            {
                void MaximizeOnce(object? sender, EventArgs e)
                {
                    ContentRendered -= MaximizeOnce;
                    Dispatcher.BeginInvoke(() => WindowState = System.Windows.WindowState.Maximized, System.Windows.Threading.DispatcherPriority.Background);
                }
                ContentRendered += MaximizeOnce;
            }

            _placementTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _placementTimer.Tick += (_, _) =>
            {
                _placementTimer.Stop();
                SavePlacement();
            };

            void Changed(object? sender, EventArgs e)
            {
                if (!IsLoaded)
                    return;
                _placementTimer.Stop();
                _placementTimer.Start();
            }

            SizeChanged += Changed;
            LocationChanged += Changed;
            StateChanged += Changed;

            if (Application.Current is not null)
                Application.Current.SessionEnding += (_, _) => SavePlacement();
        }

        private static bool IsOnAScreen(double left, double top, double width, double height)
        {
            try
            {
                double scale;
                using (var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero))
                    scale = g.DpiX / 96.0;

                foreach (System.Windows.Forms.Screen screen in System.Windows.Forms.Screen.AllScreens)
                {
                    var area = screen.WorkingArea;
                    double l = area.Left / scale, t = area.Top / scale, r = area.Right / scale, b = area.Bottom / scale;
                    double overlapW = Math.Min(left + width, r) - Math.Max(left, l);
                    double overlapH = Math.Min(top + height, b) - Math.Max(top, t);
                    if (overlapW >= 120 && overlapH >= 80)
                        return true;
                }
            }
            catch (Exception)
            {
            }

            return false;
        }

        private void SavePlacement()
        {
            if (IsUiTest || WindowState == System.Windows.WindowState.Minimized && !IsVisible)
                return;

            try
            {
                Rect bounds = WindowState == System.Windows.WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
                if (bounds.IsEmpty || bounds.Width < 100 || bounds.Height < 100)
                    return;

                var placement = new Models.Persistable.WindowState
                {
                    Left = bounds.Left,
                    Top = bounds.Top,
                    Width = bounds.Width,
                    Height = bounds.Height,
                    Maximized = WindowState == System.Windows.WindowState.Maximized,
                };

                Directory.CreateDirectory(Paths.Base);
                File.WriteAllText(PlacementPath, JsonSerializer.Serialize(placement));
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("MainWindow", $"Window placement not saved: {ex.Message}");
            }
        }

        private async void ShowAlreadyRunningSnackbar()
        {
            await Task.Delay(500);
            AlreadyRunningSnackbar.Show();
        }

        #region INavigationWindow methods

        public Frame GetFrame() => RootFrame;

        public INavigation GetNavigation() => RootNavigation;

        public bool Navigate(Type pageType)
        {
            Type host = SectionHosts.Resolve(pageType);

            if (RootFrame.Content?.GetType() != host && !RootNavigation.Navigate(host))
                return false;

            if (host != pageType)
                Dispatcher.BeginInvoke(() => ShowSection(pageType), System.Windows.Threading.DispatcherPriority.Loaded);

            return true;
        }

        private void ShowSection(Type pageType)
        {
            if (RootFrame.Content is UI.Elements.Controls.ISectionHostPage host)
                host.SectionHost.Show(pageType);
        }

        public void SetPageService(IPageService pageService) => RootNavigation.PageService = pageService;

        public void ShowWindow() => Show();

        public void CloseWindow() => Close();

        #endregion INavigationWindow methods

        private Storyboard? _mist;

        private void MainWindow_MistLoaded(object sender, RoutedEventArgs e)
        {
            _mist = (Storyboard)Resources["MistDrift"];
            _mist.Begin(this, true);

            Activated += (_, _) => { try { _mist?.Resume(this); } catch (Exception) { } };
            Deactivated += (_, _) => { try { _mist?.Pause(this); } catch (Exception) { } };
            StateChanged += (_, _) =>
            {
                try
                {
                    if (WindowState == System.Windows.WindowState.Minimized)
                        _mist?.Pause(this);
                    else if (IsActive)
                        _mist?.Resume(this);
                }
                catch (Exception) { }
            };
        }

        private void WpfUiWindow_Closing(object sender, CancelEventArgs e)
        {
            var viewModel = (MainWindowViewModel)DataContext;

            bool shouldMinimizeToTray = App.Settings.Prop.MinimizeToTrayOnClose
                && !_exitRequested
                && !viewModel.RestartAfterClose
                && !viewModel.LaunchAfterClose
                && !App.LaunchSettings.TestModeFlag.Active;

            SavePlacement();

            if (shouldMinimizeToTray)
            {
                e.Cancel = true;
                MinimizeToTray();
                return;
            }

            if (!viewModel.RestartAfterClose && !viewModel.LaunchAfterClose && HasUnsavedChangesOnClose())
            {
                App.Logger.WriteLine("MainWindow::Closing", "Unsaved changes found, asking before closing");

                var result = Frontend.ShowMessageBox(Strings.Menu_UnsavedChanges, MessageBoxImage.Warning, MessageBoxButton.YesNo);

                if (result != MessageBoxResult.Yes)
                    e.Cancel = true;
            }

            App.State.Save();
        }

        /// <summary>Hides the window to the tray, as closing does with "Minimise to tray". Used by Deployment > "Close PhasmaStrap".</summary>
        public void HideToTray()
        {
            SavePlacement();
            MinimizeToTray();
        }

        private void MinimizeToTray()
        {
            Hide();
            try { _mist?.Pause(this); } catch (Exception) { }

            if (_trayIcon is not null)
                return;

            _trayIcon = new System.Windows.Forms.NotifyIcon
            {
                Icon = Properties.Resources.IconPhasmaStrap,
                Text = App.ProjectName,
                Visible = true
            };

            _trayIcon.DoubleClick += (_, _) => RestoreFromTray();

            var contextMenu = new System.Windows.Forms.ContextMenuStrip();
            contextMenu.Items.Add(Strings.Common_Open, null, (_, _) => RestoreFromTray());
            contextMenu.Items.Add(Strings.Common_Exit, null, (_, _) => ExitFromTray());
            _trayIcon.ContextMenuStrip = contextMenu;
        }

        private void RestoreFromTray()
        {
            Show();

            if (WindowState == System.Windows.WindowState.Minimized)
                WindowState = ReadPlacement().Maximized ? System.Windows.WindowState.Maximized : System.Windows.WindowState.Normal;
            Activate();

            _trayIcon?.Dispose();
            _trayIcon = null;
        }

        private void ExitFromTray()
        {
            _exitRequested = true;

            _trayIcon?.Dispose();
            _trayIcon = null;

            Close();
        }

        private void WpfUiWindow_Closed(object sender, EventArgs e)
        {
            _closed = true;

            if (ThemeTokens.SectionAccent is not null)
            {
                ThemeTokens.SectionAccent = null;
                Base.WpfUiWindow.ApplyAccentFrom(App.Settings.Prop.CustomColorThemeEnabled ? PhasmaStrap.Utility.AppColorTheme.LoadForApp() : null);
            }

            _ = PhasmaStrap.Utility.PhasmaAccount.MaybeAutoBackUpAsync();

            try { _mist?.Stop(this); } catch (Exception) { }
            _searchDebounce.Stop();
            _unsavedTimer.Stop();
            PhasmaStrap.UI.NotificationCenter.HistoryChanged -= NotificationCenter_HistoryChanged;
            PhasmaStrap.Utility.PhasmaAccount.Changed -= PhasmaAccount_Changed;
            _trayIcon?.Dispose();

            ControllerService.Shutdown();

            var viewModel = (MainWindowViewModel)DataContext;

            if (viewModel.RestartAfterClose)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c timeout /t 1 /nobreak >nul & start \"\" \"{Paths.Process}\" -settings",
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                });
                App.SoftTerminate();
                return;
            }

            if (App.LaunchSettings.TestModeFlag.Active || viewModel.LaunchAfterClose)
                LaunchHandler.LaunchRoblox(LaunchMode.Player);
            else
                App.SoftTerminate();
        }
    }
}
