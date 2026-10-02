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

        private readonly System.Windows.Threading.DispatcherTimer _unsavedTimer = new() { Interval = TimeSpan.FromSeconds(1) };

        private int _seenNotifications;

        private void InitializeTopBar()
        {
            var viewModel = (MainWindowViewModel)DataContext;
            viewModel.RequestSaveNoticeEvent += (_, _) => TakeSettingsSnapshot();

            TakeSettingsSnapshot();
            _unsavedTimer.Tick += (_, _) => RefreshUnsavedBar();
            _unsavedTimer.Start();

            RootFrame.Navigated += (_, _) => RefreshSettingsButton();

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

        private void TakeSettingsSnapshot()
        {
            _savedSettingsSnapshot = SerializeSettings();
            _savedSettingsHash = App.Settings.LastFileHash;
        }

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Keep the top bar on one line: drop the brand name, then the account name, as the window narrows.
            double width = e.NewSize.Width;
            BrandText.Visibility = width < 1180 ? Visibility.Collapsed : Visibility.Visible;
            AccountTextPanel.Visibility = width < 1090 ? Visibility.Collapsed : Visibility.Visible;
            SettingsSearchBox.Width = width < 1120 ? 170 : 220;
        }

        private bool HasUnsavedChanges()
        {
            if (App.FastFlags.Changed || App.FlagProfiles.Changed || App.PendingSettingTasks.Any(x => x.Value.Changed))
                return true;

            string now = SerializeSettings();
            return now.Length > 0 && _savedSettingsSnapshot.Length > 0 && now != _savedSettingsSnapshot;
        }

        private void RefreshUnsavedBar()
        {
            if (!IsVisible || WindowState == System.Windows.WindowState.Minimized)
                return;

            // Pages that save on their own (deferred saves) move the file on; treat that as the new baseline.
            if (!string.Equals(App.Settings.LastFileHash, _savedSettingsHash, StringComparison.Ordinal))
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
                FillNotifications();
        }

        private void FillNotifications()
        {
            var history = PhasmaStrap.UI.NotificationCenter.History.OrderByDescending(x => x.Timestamp).ToList();
            NotificationsList.ItemsSource = history;
            NotificationsList.Visibility = history.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            NotificationsEmpty.Visibility = history.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        private void NotificationsButton_Click(object sender, RoutedEventArgs e)
        {
            FillNotifications();
            NotificationsPopup.IsOpen = !NotificationsPopup.IsOpen;

            _seenNotifications = PhasmaStrap.UI.NotificationCenter.History.Count;
            NotificationsBadge.Visibility = Visibility.Collapsed;
        }

        private void ClearNotifications_Click(object sender, RoutedEventArgs e)
        {
            PhasmaStrap.UI.NotificationCenter.ClearHistory();
            _seenNotifications = 0;
            FillNotifications();
        }

        private void NotificationsList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is not DependencyObject source)
                return;

            DependencyObject? current = source;
            while (current is not null && current is not ListBoxItem)
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);

            if (current is ListBoxItem item && item.DataContext is PhasmaStrap.UI.NotificationRecord record && record.OnClick is not null)
            {
                NotificationsPopup.IsOpen = false;

                try
                {
                    record.OnClick();
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
            LocationChanged += (_, _) => CloseSearchPopup();
            SizeChanged += (_, _) => CloseSearchPopup();
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
                SettingsSearchBox.Focus();
                SettingsSearchBox.SelectAll();
                e.Handled = true;
            }
        }

        private static string RecentKey(Search.SettingsSearchEntry entry) => $"{entry.Kind}|{entry.PageType.Name}|{entry.Tab}|{entry.Section}|{entry.Group}|{entry.Header}";

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

        private void InitializeAccountButton()
        {
            PhasmaStrap.Utility.PhasmaAccount.Changed += (_, _) => Dispatcher.Invoke(RefreshAccountButton);
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

        private void SettingsSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchDebounce.Stop();
            _searchDebounce.Start();
        }

        private void RunSettingsSearch()
        {
            string query = SettingsSearchBox.Text ?? "";

            if (query.Trim().Length == 0)
            {
                ShowRecentSearches();
                return;
            }

            List<Search.SettingsSearchResult> results = Search.SettingsSearchEngine.Search(query);

            if (results.Count > 1 && App.State.Prop.RecentSettingsSearches.Count > 0)
            {
                var recent = new HashSet<string>(App.State.Prop.RecentSettingsSearches, StringComparer.Ordinal);
                results = results
                    .Select(r => recent.Contains(RecentKey(r.Entry)) ? new Search.SettingsSearchResult(r.Entry, r.Score + 35) : r)
                    .OrderByDescending(r => r.Score)
                    .ToList();
            }

            SettingsSearchResults.ItemsSource = results;
            SettingsSearchResults.SelectedIndex = results.Count > 0 ? 0 : -1;
            SettingsSearchFooter.Text = results.Count switch
            {
                0 => "No matching settings",
                1 => "1 result  ·  Enter to open",
                _ => $"{results.Count} results  ·  ↑↓ to move, Enter to open",
            };

            SettingsSearchPopup.IsOpen = true;
        }

        private void ShowRecentSearches()
        {
            List<Search.SettingsSearchResult> recents = RecentResults();
            if (recents.Count == 0 || !SettingsSearchBox.IsKeyboardFocusWithin)
            {
                CloseSearchPopup();
                return;
            }

            SettingsSearchResults.ItemsSource = recents;
            SettingsSearchResults.SelectedIndex = -1;
            SettingsSearchFooter.Text = "Recently opened  ·  type to search everything";
            SettingsSearchPopup.IsOpen = true;
        }

        private void CloseSearchPopup()
        {
            if (_suppressSearchClose)
                return;

            SettingsSearchPopup.IsOpen = false;
        }

        private void SettingsSearchBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if ((SettingsSearchBox.Text ?? "").Trim().Length > 0 && SettingsSearchResults.Items.Count > 0)
                SettingsSearchPopup.IsOpen = true;
            else if ((SettingsSearchBox.Text ?? "").Trim().Length == 0)
                ShowRecentSearches();
        }

        private void SettingsSearchBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (e.NewFocus is DependencyObject target && IsInsidePopup(target))
                return;

            CloseSearchPopup();
        }

        private bool IsInsidePopup(DependencyObject element)
        {
            DependencyObject? current = element;
            while (current is not null)
            {
                if (ReferenceEquals(current, SettingsSearchPopup.Child))
                    return true;

                current = current is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
            }

            return false;
        }

        private void SettingsSearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            int count = SettingsSearchResults.Items.Count;

            switch (e.Key)
            {
                case Key.Down when count > 0:
                    if (!SettingsSearchPopup.IsOpen)
                        SettingsSearchPopup.IsOpen = true;
                    SettingsSearchResults.SelectedIndex = Math.Min(SettingsSearchResults.SelectedIndex + 1, count - 1);
                    SettingsSearchResults.ScrollIntoView(SettingsSearchResults.SelectedItem);
                    e.Handled = true;
                    break;

                case Key.Up when count > 0:
                    SettingsSearchResults.SelectedIndex = Math.Max(SettingsSearchResults.SelectedIndex - 1, 0);
                    SettingsSearchResults.ScrollIntoView(SettingsSearchResults.SelectedItem);
                    e.Handled = true;
                    break;

                case Key.PageDown when count > 0:
                    SettingsSearchResults.SelectedIndex = Math.Min(SettingsSearchResults.SelectedIndex + 8, count - 1);
                    SettingsSearchResults.ScrollIntoView(SettingsSearchResults.SelectedItem);
                    e.Handled = true;
                    break;

                case Key.PageUp when count > 0:
                    SettingsSearchResults.SelectedIndex = Math.Max(SettingsSearchResults.SelectedIndex - 8, 0);
                    SettingsSearchResults.ScrollIntoView(SettingsSearchResults.SelectedItem);
                    e.Handled = true;
                    break;

                case Key.Enter:
                    if (_searchDebounce.IsEnabled)
                    {
                        _searchDebounce.Stop();
                        RunSettingsSearch();
                    }

                    if (SettingsSearchResults.SelectedItem is Search.SettingsSearchResult selected)
                        ActivateSearchResult(selected);
                    else if (SettingsSearchResults.Items.Count > 0 && SettingsSearchResults.Items[0] is Search.SettingsSearchResult first)
                        ActivateSearchResult(first);

                    e.Handled = true;
                    break;

                case Key.Escape:
                    if (SettingsSearchPopup.IsOpen)
                        CloseSearchPopup();
                    else
                        SettingsSearchBox.Text = "";
                    e.Handled = true;
                    break;
            }
        }

        private void SettingsSearchResults_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject source)
            {
                DependencyObject? current = source;
                while (current is not null && current is not ListBoxItem)
                    current = System.Windows.Media.VisualTreeHelper.GetParent(current);

                if (current is ListBoxItem item && item.DataContext is Search.SettingsSearchResult result)
                {
                    ActivateSearchResult(result);
                    e.Handled = true;
                }
            }
        }

        private void ActivateSearchResult(Search.SettingsSearchResult result)
        {
            RememberRecent(result.Entry);
            _suppressSearchClose = true;
            try
            {
                SettingsSearchPopup.IsOpen = false;
                SettingsSearchBox.Text = "";
                SettingsSearchResults.ItemsSource = null;
            }
            finally
            {
                _suppressSearchClose = false;
            }

            Search.SettingsSearchNavigator.Reveal(RootNavigation, RootFrame, result.Entry);
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

            if (!RootNavigation.Navigate(host))
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

            if (App.FastFlags.Changed || App.FlagProfiles.Changed || App.PendingSettingTasks.Any())
            {
                var result = Frontend.ShowMessageBox(Strings.Menu_UnsavedChanges, MessageBoxImage.Warning, MessageBoxButton.YesNo);

                if (result != MessageBoxResult.Yes)
                    e.Cancel = true;
            }

            App.State.Save();
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
            _ = PhasmaStrap.Utility.PhasmaAccount.MaybeAutoBackUpAsync();

            try { _mist?.Stop(this); } catch (Exception) { }
            _searchDebounce.Stop();
            _unsavedTimer.Stop();
            PhasmaStrap.UI.NotificationCenter.HistoryChanged -= NotificationCenter_HistoryChanged;
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
