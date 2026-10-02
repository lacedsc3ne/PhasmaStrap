using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Collections.ObjectModel;

using Wpf.Ui.Common;

using PhasmaStrap.UI.Elements.Controls;
using PhasmaStrap.UI.Elements.Dialogs;
using PhasmaStrap.Utility;
using PhasmaStrap.Utility.Backend;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public sealed class FlagRow : INotifyPropertyChanged
    {
        private string _name = "";
        private string _value = "";
        private string _note = "";
        private string _tone = "";

        public string Name { get => _name; set { _name = value; Changed(nameof(Name)); Changed(nameof(Title)); Changed(nameof(Description)); SwitchChanged(); } }
        public string Value { get => _value; set { _value = value; Changed(nameof(Value)); Changed(nameof(IsOn)); SwitchChanged(); } }
        public string Note { get => _note; set { _note = value; Changed(nameof(Note)); } }

        public string Tone { get => _tone; set { _tone = value; Changed(nameof(Tone)); } }

        private bool _isTurnedOff;
        public bool IsTurnedOff { get => _isTurnedOff; set { _isTurnedOff = value; SwitchChanged(); Changed(nameof(DeleteHint)); } }

        private bool _isInherited;
        public bool IsInherited { get => _isInherited; set { _isInherited = value; Changed(nameof(DeleteHint)); } }

        /// <summary>Readable name for the card view (server catalog title, or the flag name in words).</summary>
        public string Title => FlagCategories.TitleOf(_name);

        /// <summary>One line from the server catalog, or "".</summary>
        public string Description => FlagCategories.DescriptionOf(_name);

        public string Category => FlagCategories.CategoryOf(_name);

        /// <summary>FFlag style flag holding True or False: shown as a switch in the card view.</summary>
        public bool IsSwitch => !_isTurnedOff && FlagCategories.IsSwitch(_name, _value);

        public bool IsOn => _value.Equals("True", StringComparison.OrdinalIgnoreCase);

        public Visibility SwitchVisibility => IsSwitch ? Visibility.Visible : Visibility.Collapsed;

        public Visibility ValueBoxVisibility => IsSwitch ? Visibility.Collapsed : Visibility.Visible;

        public string DeleteHint => _isInherited ? "Turn off for these games" : _isTurnedOff ? "Stop turning it off" : "Delete";

        private void SwitchChanged()
        {
            Changed(nameof(IsSwitch));
            Changed(nameof(SwitchVisibility));
            Changed(nameof(ValueBoxVisibility));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public static class FlagEditorRequest
    {
        public static string? ProfileToOpen { get; set; }
    }

    /// <summary>One entry in the flag editor's sidebar: your flags, or a profile.</summary>
    public sealed class FlagScopeEntry
    {
        public string? ProfileId { get; init; }
        public string Name { get; init; } = "";
        public string Summary { get; init; } = "";
        public string ToolTip { get; init; } = "";
        public Visibility DefaultVisibility => ProfileId is null ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>A card of flags in one category.</summary>
    public sealed class FlagGroup
    {
        public string Name { get; init; } = "";
        public SymbolRegular Symbol { get; init; } = SymbolRegular.Flag24;
        public List<FlagRow> Rows { get; init; } = new();
        public string CountText => Rows.Count == 1 ? "1 flag" : $"{Rows.Count} flags";
    }

    /// <summary>A game or place a profile is given to, shown as a chip.</summary>
    public sealed class FlagAssignChip
    {
        public FlagGameRule Rule { get; init; } = null!;
        public string Title { get; init; } = "";
        public string Subtitle { get; init; } = "";
        public string Initials { get; init; } = "";
        public string? IconUrl { get; init; }
        public string ToolTip => $"{Title}: {Subtitle}";
    }

    /// <summary>A game in the "Assign to a game" picker.</summary>
    public sealed class FlagAssignResult
    {
        public long UniverseId { get; init; }
        public long RootPlaceId { get; init; }
        public long LinkedPlaceId { get; init; }
        public string Name { get; init; } = "";
        public string Detail { get; init; } = "";
        public string? IconUrl { get; init; }
        public string Initials => FastFlagEditorPage.InitialsOf(Name);
    }

    public partial class FastFlagEditorPage
    {
        private readonly ObservableCollection<FlagRow> _rows = new();

        private readonly ObservableCollection<FlagGroup> _groups = new();

        private readonly ObservableCollection<FlagAssignChip> _assigned = new();

        private readonly ObservableCollection<FlagAssignResult> _assignResults = new();

        private readonly ObservableCollection<PhasmaStrap.Integrations.UniversePlace> _assignPlaces = new();

        // Card view or table view; remembered while the app runs.
        private static bool _tableView;

        // The card row the keyboard (F2, Del, Ctrl C) and the right click menu act on.
        private FlagRow? _cardRow;

        private bool _hideQuickFlags = false;
        private bool _showYoursInProfile = true;
        private string _searchFilter = "";
        private bool _refreshingScopes;

        private string? _profileId;

        private FlagProfile? Profile => App.FlagProfiles.Find(_profileId);

        private bool EditingProfile => Profile is not null;

        private static readonly HashSet<string> QuickFlagNames = new(FastFlagManager.PresetFlags.Values, StringComparer.Ordinal);

        // The last row holds the list card plus the help note under it.
        private const double MinimumListHeight = 420;

        public FastFlagEditorPage()
        {
            InitializeComponent();
            DataGrid.ItemsSource = _rows;
            CardsView.ItemsSource = _groups;
            AssignedList.ItemsSource = _assigned;
            AssignResults.ItemsSource = _assignResults;
            AssignPlaces.ItemsSource = _assignPlaces;

            ItemMenu.Attach<FlagScopeEntry>(ScopeList, BuildScopeMenu);

            if (_tableView)
                TableViewButton.IsChecked = true;
            ApplyView();
        }

        public static string InitialsOf(string name)
        {
            string[] words = (name ?? "").Split(new[] { ' ', '-', '_', ':' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => char.IsLetterOrDigit(w[0])).ToArray();

            if (words.Length == 0)
                return "?";
            if (words.Length == 1)
                return words[0][..1].ToUpperInvariant();
            return (words[0][..1] + words[1][..1]).ToUpperInvariant();
        }

        private void ViewSwitch_Checked(object sender, RoutedEventArgs e)
        {
            if (CardsView is null || TableView is null)
                return;

            _tableView = TableViewButton.IsChecked == true;
            ApplyView();
        }

        private void ApplyView()
        {
            CardsView.Visibility = _tableView ? Visibility.Collapsed : Visibility.Visible;
            TableView.Visibility = _tableView ? Visibility.Visible : Visibility.Collapsed;
            DeleteSelectedButton.Visibility = _tableView ? Visibility.Visible : Visibility.Collapsed;

            EditHintText.Text = _tableView
                ? "Double click a cell to edit. A profile holds flag changes for chosen games only. Give it to games on Per game flags."
                : "Type a value and press Enter to change a flag, or flip its switch. A profile holds flag changes for chosen games only. Give it to games on Per game flags.";

            if (!_tableView && RootLayout is not null)
                RootLayout.Height = double.NaN;

            UpdateLayoutHeight();
        }

        private void PageScroller_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateLayoutHeight();

        private void RootLayout_LayoutUpdated(object? sender, EventArgs e) => UpdateLayoutHeight();

        private void UpdateLayoutHeight()
        {
            if (PageScroller is null || RootLayout is null || RootLayout.RowDefinitions.Count == 0)
                return;

            // Cards grow with their content and the page scrolls; only the table is sized to the window.
            if (!_tableView)
                return;

            double above = 0;
            for (int i = 0; i < RootLayout.RowDefinitions.Count - 1; i++)
                above += RootLayout.RowDefinitions[i].ActualHeight;

            double available = PageScroller.ViewportHeight - RootLayout.Margin.Top - RootLayout.Margin.Bottom;
            double wanted = Math.Max(available, above + MinimumListHeight);

            if (wanted <= 0 || double.IsNaN(wanted) || double.IsInfinity(wanted))
                return;

            if (double.IsNaN(RootLayout.Height) || Math.Abs(RootLayout.Height - wanted) > 0.5)
                RootLayout.Height = wanted;
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            if (FlagEditorRequest.ProfileToOpen is string requested)
            {
                FlagEditorRequest.ProfileToOpen = null;
                if (App.FlagProfiles.Find(requested) is not null)
                    _profileId = requested;
            }

            App.FlagProfiles.Edited -= OnProfilesEdited;
            App.FlagProfiles.Edited += OnProfilesEdited;

            ManagerDisabledBar.Visibility = App.Settings.Prop.UseFastFlagManager ? Visibility.Collapsed : Visibility.Visible;

            RefreshScopes();
            ReloadList();

            _ = LoadCatalogAsync();
        }

        // Titles, descriptions and categories from the PhasmaStrap server, when it answers.
        private async Task LoadCatalogAsync()
        {
            if (FlagsCaptureApi.Catalog is not null)
                return;

            FlagCatalog? catalog = await FlagsCaptureApi.LoadFlagCatalogAsync();
            if (catalog is not null && IsLoaded)
                ReloadList();
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e) => App.FlagProfiles.Edited -= OnProfilesEdited;

        private void OnProfilesEdited(object? sender, EventArgs e)
        {
            if (!EditingProfile)
                _profileId = null;

            RefreshScopes();
            ReloadList();
        }

        private void MarkProfilesEdited()
        {
            App.FlagProfiles.Edited -= OnProfilesEdited;
            App.FlagProfiles.NotifyEdited();
            App.FlagProfiles.Edited += OnProfilesEdited;
        }

        private void RefreshScopes()
        {
            _refreshingScopes = true;

            int yours = App.FastFlags.Prop.Count;
            var items = new List<FlagScopeEntry>
            {
                new()
                {
                    ProfileId = null,
                    Name = "Your flags",
                    Summary = $"{Count(yours, "flag")} · Every game",
                    ToolTip = "Flags every game you launch through PhasmaStrap starts with",
                }
            };

            foreach (FlagProfile profile in App.FlagProfiles.Prop.Profiles.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                int games = App.FlagProfiles.RulesUsing(profile.Id).Count();
                items.Add(new FlagScopeEntry
                {
                    ProfileId = profile.Id,
                    Name = profile.Name,
                    Summary = $"{Count(profile.ChangeCount, "flag")} · {(games == 0 ? "Not used" : Count(games, "game"))}",
                    ToolTip = $"Runs on top of your flags in {(games == 0 ? "no game yet" : Count(games, "game"))}. Right click to rename, duplicate or delete.",
                });
            }

            ScopeList.ItemsSource = items;
            ScopeList.SelectedItem = items.FirstOrDefault(i => i.ProfileId == _profileId) ?? items[0];

            _refreshingScopes = false;

            UpdateScopeUi();
        }

        private static string Count(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

        private void ScopeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_refreshingScopes || ScopeList.SelectedItem is not FlagScopeEntry item || item.ProfileId == _profileId)
                return;

            _profileId = item.ProfileId;
            UpdateScopeUi();
            ClearSearch(false);
            ReloadList();
        }

        private void UpdateScopeUi()
        {
            FlagProfile? profile = Profile;

            TurnOffButton.Visibility = profile is null ? Visibility.Collapsed : Visibility.Visible;
            ShowYoursButton.Visibility = profile is null ? Visibility.Collapsed : Visibility.Visible;
            CompareButton.Visibility = profile is null ? Visibility.Collapsed : Visibility.Visible;
            DuplicateButton.Visibility = profile is null ? Visibility.Collapsed : Visibility.Visible;
            ShareButton.Visibility = profile is null ? Visibility.Collapsed : Visibility.Visible;

            if (profile is null)
            {
                ScopeTitle.Text = "Your flags";
                ScopeText.Text = "These flags apply to every game you launch through PhasmaStrap. A game with a profile gets these plus the profile's changes.";
                UsedByPanel.Visibility = Visibility.Collapsed;
                _assigned.Clear();
                AssignPopup.IsOpen = false;
                return;
            }

            ScopeTitle.Text = profile.Name;
            ScopeText.Text = $"Runs on top of your flags, only in the games it is given to. The list shows everything those games start with: the profile's own flags, and your flags marked \"From your flags\". Change a value to change it for these games only; delete one of your flags here to turn it off for them.";

            List<FlagGameRule> rules = App.FlagProfiles.RulesUsing(profile.Id)
                .OrderBy(r => r.GameName.Length > 0 ? r.GameName : "~", StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.PlaceId)
                .ToList();

            _assigned.Clear();
            foreach (FlagGameRule rule in rules)
            {
                string game = rule.GameName.Length > 0 ? rule.GameName : rule.UniverseId > 0 ? $"Game {rule.UniverseId}" : $"Place {rule.PlaceId}";
                string where = rule.IsWholeGame
                    ? "Whole game"
                    : rule.PlaceId == rule.RootPlaceId ? "Only its start place"
                    : rule.PlaceName.Length > 0 ? $"Only {rule.PlaceName}" : $"Only place {rule.PlaceId}";

                _assigned.Add(new FlagAssignChip
                {
                    Rule = rule,
                    Title = game,
                    Subtitle = where,
                    Initials = InitialsOf(game),
                    IconUrl = rule.IconUrl.Length > 0 ? rule.IconUrl : null,
                });
            }

            UsedByText.Text = rules.Count == 0 ? "No game uses this profile yet. Assign it to a game so it starts with these flags." : "";
            UsedByPanel.Visibility = Visibility.Visible;
        }

        public static string DescribeRule(FlagGameRule rule)
        {
            string game = rule.GameName.Length > 0 ? rule.GameName : rule.UniverseId > 0 ? $"game {rule.UniverseId}" : "";

            if (rule.IsWholeGame)
                return game;

            string place = rule.PlaceName.Length > 0 ? rule.PlaceName : $"place {rule.PlaceId}";
            return game.Length > 0 && !string.Equals(game, place, StringComparison.OrdinalIgnoreCase) ? $"{game} ({place} only)" : $"{place} only";
        }

        private void GoToGames_Click(object sender, RoutedEventArgs e) => FastFlagSettingsPage.SelectTab(this, "FastFlagGamesPage");

        private Window? Owner => Window.GetWindow(this);

        private string? AskName(string title, string prompt, string initial)
        {
            var dialog = new TextInputDialog(title, prompt, initial) { Owner = Owner };
            dialog.ShowDialog();
            return dialog.Confirmed ? dialog.Value : null;
        }

        private FlagProfile? CreateProfile(string? suggestedName = null)
        {
            string? name = AskName("New profile", "Name the profile (for example the game it is for, or what it does):", suggestedName ?? "");
            if (name is null)
                return null;

            var profile = new FlagProfile { Name = FlagLayers.UniqueName(App.FlagProfiles.Prop, name) };
            App.FlagProfiles.Prop.Profiles.Add(profile);
            MarkProfilesEdited();
            OfferToClearGlobalFlags(profile.Name);
            return profile;
        }

        private void OfferToClearGlobalFlags(string profileName)
        {
            int count = App.FastFlags.Prop.Count;
            if (count == 0)
                return;

            var answer = Frontend.ShowMessageBox(
                $"You have {count} flag{(count == 1 ? "" : "s")} set globally, outside any profile. Those stay switched on in every game, on top of whatever \"{profileName}\" sets.\n\nClear them, so your profiles alone decide which flags are set?",
                MessageBoxImage.Question,
                MessageBoxButton.YesNo);

            if (answer != MessageBoxResult.Yes)
                return;

            App.FastFlags.Prop.Clear();
            MarkProfilesEdited();
        }

        private void SwitchTo(string? profileId)
        {
            _profileId = profileId;
            RefreshScopes();
            ClearSearch(false);
            ReloadList();
        }

        private void NewProfile_Click(object sender, RoutedEventArgs e)
        {
            FlagProfile? profile = CreateProfile();
            if (profile is not null)
                SwitchTo(profile.Id);
        }

        private void ProfileMenu_Click(object sender, RoutedEventArgs e)
        {
            FlagProfile? profile = Profile;
            var menu = new System.Windows.Controls.ContextMenu { PlacementTarget = ProfileMenuButton, Placement = PlacementMode.Bottom };

            menu.Items.Add(MenuItem("Rename", profile is not null, () => RenameProfile(profile!)));
            menu.Items.Add(MenuItem("Duplicate", profile is not null, () => DuplicateProfile(profile!)));
            menu.Items.Add(MenuItem("Copy share code", profile is not null, () => CopyShareCode(profile!)));
            menu.Items.Add(MenuItem("Add a profile from a share code", true, PasteShareCode));
            menu.Items.Add(MenuItem("Publish to the gallery", profile is not null, () => PublishToGallery(profile!)));
            menu.Items.Add(MenuItem("Add a profile from the gallery", true, AddFromGallery));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItem("Delete this profile", profile is not null, () => DeleteProfile(profile!)));

            menu.IsOpen = true;
        }

        // ---- Assign to a game ----

        private CancellationTokenSource? _assignCts;
        private FlagAssignResult? _assignGame;
        private readonly System.Windows.Threading.DispatcherTimer _assignDebounce = new() { Interval = TimeSpan.FromMilliseconds(450) };
        private bool _assignDebounceHooked;

        private void AssignButton_Click(object sender, RoutedEventArgs e)
        {
            if (Profile is null)
                return;

            AssignSearchBox.Text = "";
            ShowAssignStep(pickPlace: false);
            ShowRecentGames();
            AssignPopup.IsOpen = true;

            Dispatcher.BeginInvoke(new Action(() => AssignSearchBox.Focus()), System.Windows.Threading.DispatcherPriority.Input);
        }

        private void ShowAssignStep(bool pickPlace)
        {
            AssignResults.Visibility = pickPlace ? Visibility.Collapsed : Visibility.Visible;
            AssignWherePanel.Visibility = pickPlace ? Visibility.Visible : Visibility.Collapsed;
            AssignHint.Text = pickPlace ? "The whole game, or only one of its places" : "Pick a game, then whole game or one place";
        }

        private string RuleDetail(long universeId)
        {
            List<FlagGameRule> rules = App.FlagProfiles.Prop.Rules.Where(r => r.UniverseId == universeId && universeId > 0).ToList();
            if (rules.Count == 0)
                return "";

            var names = rules.Select(r => App.FlagProfiles.Find(r.ProfileId)?.Name).Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList();
            string where = rules.Count == 1 ? (rules[0].IsWholeGame ? "Whole game" : "1 place") : $"{rules.Count} places";
            return names.Count == 0 ? where : $"{where} · uses {string.Join(", ", names)}";
        }

        private void ShowRecentGames()
        {
            _assignResults.Clear();

            IEnumerable<IGrouping<long, PlayTimeEntry>> recent;
            try
            {
                recent = PhasmaStrap.Integrations.PlayTimeStore.GetAll().Where(e => e.UniverseId > 0).GroupBy(e => e.UniverseId).Take(8).ToList();
            }
            catch
            {
                recent = Enumerable.Empty<IGrouping<long, PlayTimeEntry>>();
            }

            foreach (var game in recent)
            {
                PlayTimeEntry latest = game.OrderByDescending(e => e.LastPlayed).First();
                string detail = RuleDetail(game.Key);

                _assignResults.Add(new FlagAssignResult
                {
                    UniverseId = game.Key,
                    RootPlaceId = latest.PlaceId,
                    Name = latest.Name.Length > 0 ? latest.Name : $"Game {game.Key}",
                    IconUrl = string.IsNullOrEmpty(latest.IconUrl) ? null : latest.IconUrl,
                    Detail = detail.Length > 0 ? detail : "Played recently",
                });
            }

            AssignHint.Text = _assignResults.Count > 0 ? "Recently played, or search above. Pick a game, then whole game or one place" : "Search for a game above";
        }

        private void AssignSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_assignDebounceHooked)
            {
                _assignDebounce.Tick += async (_, _) =>
                {
                    _assignDebounce.Stop();
                    await SearchGamesAsync();
                };
                _assignDebounceHooked = true;
            }

            _assignDebounce.Stop();
            _assignDebounce.Start();
        }

        private async void AssignSearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                _assignDebounce.Stop();
                e.Handled = true;
                await SearchGamesAsync();
            }
            else if (e.Key == Key.Escape)
            {
                AssignPopup.IsOpen = false;
                e.Handled = true;
            }
        }

        private async Task SearchGamesAsync()
        {
            string text = AssignSearchBox.Text.Trim();
            ShowAssignStep(pickPlace: false);

            if (text.Length == 0)
            {
                ShowRecentGames();
                return;
            }

            _assignCts?.Cancel();
            var cts = _assignCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

            AssignHint.Text = "Searching...";

            try
            {
                RobloxLaunchTarget? target = RobloxLinkParser.TryParse(text, out RobloxLaunchTarget parsed) ? parsed
                    : text.Contains('.') ? await RobloxLinkParser.ResolveAsync(text, cts.Token) : null;

                if (target is { Kind: RobloxLinkKind.Place, PlaceId: > 0 })
                {
                    GameInfo? game = await GameLookup.FromPlaceAsync(target.PlaceId, cts.Token);
                    if (cts.IsCancellationRequested || _assignCts != cts)
                        return;

                    _assignResults.Clear();

                    if (game is null)
                    {
                        AssignHint.Text = $"No game found for place {target.PlaceId}.";
                        return;
                    }

                    _assignResults.Add(new FlagAssignResult
                    {
                        UniverseId = game.UniverseId,
                        RootPlaceId = game.RootPlaceId,
                        LinkedPlaceId = target.PlaceId,
                        Name = game.Name,
                        IconUrl = string.IsNullOrEmpty(game.IconUrl) ? null : game.IconUrl,
                        Detail = RuleDetail(game.UniverseId) is { Length: > 0 } detail ? detail : "From the link",
                    });

                    AssignHint.Text = "Pick a game, then whole game or one place";
                    return;
                }

                List<GameInfo> games = await GameLookup.SearchAsync(text, cts.Token);
                if (cts.IsCancellationRequested || _assignCts != cts)
                    return;

                _assignResults.Clear();
                foreach (GameInfo game in games)
                {
                    _assignResults.Add(new FlagAssignResult
                    {
                        UniverseId = game.UniverseId,
                        RootPlaceId = game.RootPlaceId,
                        Name = game.Name,
                        IconUrl = string.IsNullOrEmpty(game.IconUrl) ? null : game.IconUrl,
                        Detail = RuleDetail(game.UniverseId),
                    });
                }

                AssignHint.Text = games.Count == 0 ? "No games found. Try other words, or paste the game's link." : "Pick a game, then whole game or one place";
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                AssignHint.Text = $"Search failed: {ex.Message}";
            }
        }

        private async void AssignResults_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AssignResults.SelectedItem is not FlagAssignResult game)
                return;

            AssignResults.SelectedItem = null;
            _assignGame = game;
            AssignGameText.Text = game.Name;
            _assignPlaces.Clear();
            ShowAssignStep(pickPlace: true);

            List<PhasmaStrap.Integrations.UniversePlace> places;
            try
            {
                places = await PhasmaStrap.Integrations.UniversePlaces.GetPlacesAsync(game.UniverseId, game.RootPlaceId);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("FastFlagEditorPage", $"Could not list the places of {game.Name}: {ex.Message}");
                places = new();
            }

            if (_assignGame != game)
                return;

            if (places.Count == 0)
            {
                if (game.RootPlaceId > 0)
                    places.Add(new PhasmaStrap.Integrations.UniversePlace(game.RootPlaceId, "Start place", true));
                if (game.LinkedPlaceId > 0 && game.LinkedPlaceId != game.RootPlaceId)
                    places.Add(new PhasmaStrap.Integrations.UniversePlace(game.LinkedPlaceId, $"Place {game.LinkedPlaceId}", false));
            }

            foreach (PhasmaStrap.Integrations.UniversePlace place in places)
                _assignPlaces.Add(place);
        }

        private void AssignBack_Click(object sender, RoutedEventArgs e)
        {
            _assignGame = null;
            ShowAssignStep(pickPlace: false);
            AssignHint.Text = "Pick a game, then whole game or one place";
        }

        private void AssignWholeGame_Click(object sender, RoutedEventArgs e)
        {
            if (_assignGame is FlagAssignResult game)
                Assign(game, 0, "");
        }

        private void AssignPlaces_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AssignPlaces.SelectedItem is not PhasmaStrap.Integrations.UniversePlace place || _assignGame is not FlagAssignResult game)
                return;

            AssignPlaces.SelectedItem = null;
            Assign(game, place.PlaceId, place.Name);
        }

        // Same rule handling as the Per game flags tab: one rule per whole game or per place.
        private void Assign(FlagAssignResult game, long placeId, string placeName)
        {
            FlagProfile? profile = Profile;
            if (profile is null)
                return;

            FlagGameRule? rule = placeId <= 0
                ? App.FlagProfiles.Prop.Rules.FirstOrDefault(r => r.IsWholeGame && r.UniverseId == game.UniverseId)
                : App.FlagProfiles.Prop.Rules.FirstOrDefault(r => r.PlaceId == placeId);

            string? before = rule is null || rule.ProfileId == profile.Id ? null : App.FlagProfiles.Find(rule.ProfileId)?.Name;

            if (rule is null)
            {
                rule = new FlagGameRule();
                App.FlagProfiles.Prop.Rules.Add(rule);
            }

            rule.UniverseId = game.UniverseId;
            rule.RootPlaceId = game.RootPlaceId;
            rule.PlaceId = placeId;
            rule.PlaceName = placeName;
            rule.GameName = game.Name;
            rule.IconUrl = game.IconUrl ?? "";
            rule.ProfileId = profile.Id;

            if (placeId > 0)
                GameLookup.Remember(placeId, game.UniverseId);
            if (game.RootPlaceId > 0)
                GameLookup.Remember(game.RootPlaceId, game.UniverseId);

            AssignPopup.IsOpen = false;
            _assignGame = null;

            MarkProfilesEdited();
            RefreshScopes();

            if (before is not null)
                UsedByText.Text = $"{game.Name} used \"{before}\" before and now uses \"{profile.Name}\".";
        }

        private void Unassign_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: FlagAssignChip chip })
                return;

            App.FlagProfiles.Prop.Rules.Remove(chip.Rule);
            MarkProfilesEdited();
            RefreshScopes();
        }

        private void Compare_Click(object sender, RoutedEventArgs e)
        {
            FlagProfile? profile = Profile;
            if (profile is null)
                return;

            var dialog = new FlagPreviewDialog(profile.Name, profile) { Owner = Owner };
            dialog.ShowDialog();
        }

        private void Duplicate_Click(object sender, RoutedEventArgs e)
        {
            if (Profile is FlagProfile profile)
                DuplicateProfile(profile);
        }

        private void Share_Click(object sender, RoutedEventArgs e)
        {
            if (Profile is FlagProfile profile)
                CopyShareCode(profile);
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            FlagProfile? profile = Profile;
            string scope = profile is null ? "your flags" : $"\"{profile.Name}\"";

            var menu = new System.Windows.Controls.ContextMenu { PlacementTarget = ImportButton, Placement = PlacementMode.Top };
            menu.Items.Add(MenuItem("Add a profile from a share code", true, PasteShareCode));
            menu.Items.Add(MenuItem("Add a profile from the gallery", true, AddFromGallery));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItem($"Paste JSON into {scope}", true, () => ShowAddDialog(jsonTab: true)));
            menu.IsOpen = true;
        }

        private void BuildScopeMenu(FlagScopeEntry entry, ItemMenu menu)
        {
            FlagProfile? profile = App.FlagProfiles.Find(entry.ProfileId);

            if (entry.ProfileId != _profileId)
                SwitchTo(entry.ProfileId);

            if (profile is null)
            {
                menu.Add("Export JSON", SymbolRegular.ArrowExportRtl24, () => ExportJSONButton_Click(this, new RoutedEventArgs()))
                    .Add("Paste JSON into your flags", SymbolRegular.ArrowImport24, () => ShowAddDialog(jsonTab: true))
                    .Separator()
                    .Add("New profile", SymbolRegular.Add24, () => NewProfile_Click(this, new RoutedEventArgs()));
                return;
            }

            menu.Add("Rename", SymbolRegular.Rename24, () => RenameProfile(profile), "F2")
                .Add("Duplicate", SymbolRegular.Copy24, () => DuplicateProfile(profile))
                .Add("Compare with your flags", SymbolRegular.BranchCompare24, () => Compare_Click(this, new RoutedEventArgs()))
                .Separator()
                .Add("Copy share code", SymbolRegular.Share24, () => CopyShareCode(profile))
                .Add("Publish to the gallery", SymbolRegular.CloudArrowUp24, () => PublishToGallery(profile))
                .Add("Choose games", SymbolRegular.Games24, () => GoToGames_Click(this, new RoutedEventArgs()))
                .Separator()
                .Add("Delete this profile", SymbolRegular.Delete24, () => DeleteProfile(profile), "Del", danger: true);
        }

        private void ScopeList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.OriginalSource is TextBox || Profile is not FlagProfile profile)
                return;

            if (e.Key == Key.F2)
            {
                RenameProfile(profile);
                e.Handled = true;
            }
            else if (e.Key == Key.Delete)
            {
                DeleteProfile(profile);
                e.Handled = true;
            }
        }

        private static MenuItem MenuItem(string header, bool enabled, Action action, string gesture = "", bool danger = false)
        {
            var item = new MenuItem { Header = header, IsEnabled = enabled, InputGestureText = gesture };
            if (danger)
                item.SetResourceReference(Control.ForegroundProperty, "SystemFillColorCriticalBrush");
            item.Click += (_, _) => action();
            return item;
        }

        private void RenameProfile(FlagProfile profile)
        {
            string? name = AskName("Rename profile", "New name:", profile.Name);
            if (name is null)
                return;

            profile.Name = FlagLayers.UniqueName(App.FlagProfiles.Prop, name, profile.Id);
            MarkProfilesEdited();
            RefreshScopes();
        }

        private void DuplicateProfile(FlagProfile profile)
        {
            FlagProfile copy = profile.Copy();
            copy.Id = FlagProfile.NewId();
            copy.Name = FlagLayers.UniqueName(App.FlagProfiles.Prop, profile.Name + " copy");

            App.FlagProfiles.Prop.Profiles.Add(copy);
            MarkProfilesEdited();
            SwitchTo(copy.Id);
        }

        private async void CopyShareCode(FlagProfile profile)
        {
            if (profile.ChangeCount == 0)
            {
                Frontend.ShowMessageBox("This profile is empty, so there is nothing to share yet.", MessageBoxImage.Information);
                return;
            }

            string code = FlagLayers.ToShareCode(profile);
            string? link = await PhasmaStrap.Utility.PhasmaAccount.ShortLinkAsync("flagProfile", profile.Name, code);

            ClipboardShare.CopyText(link ?? code);

            Frontend.ShowMessageBox(
                link is null
                    ? $"The share code for \"{profile.Name}\" is on your clipboard. Anyone can add it with Profile options > Add a profile from a share code.\n\nThe short link needs the PhasmaStrap server, which could not be reached just now, so this is the long code instead.\n\nIt holds the flags only - not which games use it."
                    : $"{link} is on your clipboard.\n\nSend it to anyone. They add it with Profile options > Add a profile from a share code, or open the link.\n\nIt holds the flags only - not which games use it.",
                MessageBoxImage.Information);
        }

        private async void PublishToGallery(FlagProfile profile)
        {
            if (profile.ChangeCount == 0)
            {
                Frontend.ShowMessageBox("This profile is empty, so there is nothing to publish yet.", MessageBoxImage.Information);
                return;
            }

            if (!PhasmaStrap.Utility.PhasmaAccount.SignedIn)
            {
                Frontend.ShowMessageBox("Sign in on the Accounts page first, so the gallery can show who made it.", MessageBoxImage.Information);
                return;
            }

            var confirm = Frontend.ShowMessageBox(
                $"Publishing \"{profile.Name}\" puts its flags, its name and the name on your account in the public gallery, where anyone can read and add them.\n\nYou can remove it again at any time. Carry on?",
                MessageBoxImage.Question,
                MessageBoxButton.YesNo);

            if (confirm != MessageBoxResult.Yes)
                return;

            string? summary = AskName("Publish to the gallery", "One line about what it does (optional):", "");

            var (problem, url) = await PhasmaStrap.Utility.PhasmaAccount.PublishToGalleryAsync("flagProfile", profile.Name, summary ?? "", FlagLayers.ToShareCode(profile));

            if (problem is null && url is not null)
                ClipboardShare.CopyText(url);

            Frontend.ShowMessageBox(
                problem is null
                    ? $"\"{profile.Name}\" has been sent to the gallery and is under review. It shows up at phasmastrap.com/gallery once an admin has looked at it.\n\nIts link, {url}, is on your clipboard and works right now."
                    : $"It was not published: {problem}",
                problem is null ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }

        private void AddFromGallery()
        {
            var dialog = new GalleryPickerDialog("Gallery", "FastFlag profiles people have published. Double-click one to add it.", "flagProfile") { Owner = Owner };
            dialog.ShowDialog();

            if (!dialog.Confirmed)
                return;

            AddProfileFromCode(dialog.Code);
        }

        private void PasteShareCode()
        {
            string clipboard = "";
            try { clipboard = System.Windows.Clipboard.GetText(); } catch { }

            string found = FlagLayers.FindProfileCode(clipboard);
            string? code = AskName("Add a profile", "Paste a share code or a phasmastrap.com/g/ link:", found.Length > 0 ? found : GalleryLinkIn(clipboard));
            if (code is null)
                return;

            AddPastedCode(code);
        }

        private static string GalleryLinkIn(string text)
        {
            var match = System.Text.RegularExpressions.Regex.Match(text ?? "", @"https?://[^\s]*phasmastrap\.com/g/[a-z0-9]{4,24}", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success ? match.Value : "";
        }

        private async void AddPastedCode(string code)
        {
            string? galleryId = PhasmaStrap.Utility.PhasmaAccount.GalleryIdIn(code);

            if (galleryId is not null)
            {
                string? fetched = await PhasmaStrap.Utility.PhasmaAccount.GalleryTakeAsync(galleryId);

                if (fetched is null)
                {
                    Frontend.ShowMessageBox("That link does not point at anything in the gallery any more.", MessageBoxImage.Warning);
                    return;
                }

                code = fetched;
            }

            AddProfileFromCode(code);
        }

        private void AddProfileFromCode(string code)
        {
            FlagProfile? profile = FlagLayers.FromShareCode(code);
            if (profile is null)
            {
                Frontend.ShowMessageBox("That isn't a working profile share code. Check that the whole code was copied.", MessageBoxImage.Warning);
                return;
            }

            List<string> bad = profile.Flags.Where(f => FlagValidation.Problem(f.Key, f.Value) is not null).Select(f => f.Key).ToList();
            foreach (string key in bad)
                profile.Flags.Remove(key);

            profile.Name = FlagLayers.UniqueName(App.FlagProfiles.Prop, profile.Name.Length > 0 ? profile.Name : "Shared profile");
            App.FlagProfiles.Prop.Profiles.Add(profile);
            MarkProfilesEdited();
            SwitchTo(profile.Id);

            string message = $"Added \"{profile.Name}\" with {profile.Flags.Count} flag(s)" + (profile.Remove.Count > 0 ? $" and {profile.Remove.Count} turned off" : "") + ". Give it to a game on the Per-game flags tab, then press Save.";
            if (bad.Count > 0)
                message += $"\n\n{bad.Count} flag(s) in the code were not valid and were left out: {string.Join(", ", bad.Take(10))}";

            Frontend.ShowMessageBox(message, MessageBoxImage.Information);
        }

        private void DeleteProfile(FlagProfile profile)
        {
            int games = App.FlagProfiles.RulesUsing(profile.Id).Count();
            string question = games == 0
                ? $"Delete the profile \"{profile.Name}\"?"
                : $"Delete the profile \"{profile.Name}\"? {games} game(s) use it and will go back to just your flags.";

            if (Frontend.ShowMessageBox(question, MessageBoxImage.Question, MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                return;

            App.FlagProfiles.Prop.Profiles.Remove(profile);
            App.FlagProfiles.Prop.Rules.RemoveAll(r => r.ProfileId == profile.Id);
            MarkProfilesEdited();
            SwitchTo(null);
        }

        private void ReloadList()
        {
            string? selectedName = (DataGrid.SelectedItem as FlagRow)?.Name;

            _rows.Clear();

            FlagProfile? profile = Profile;

            var rows = new List<FlagRow>();

            if (profile is null)
            {
                foreach (var (name, raw) in App.FastFlags.Prop)
                {
                    bool quick = QuickFlagNames.Contains(name);
                    if ((quick && _hideQuickFlags) || !MatchesSearch(name))
                        continue;

                    string value = raw?.ToString() ?? "";
                    string? problem = FlagValidation.Problem(name, value);

                    rows.Add(new FlagRow
                    {
                        Name = name,
                        Value = value,
                        Note = problem ?? (quick ? "Set by a Roblox FFlags toggle" : ""),
                        Tone = problem is null ? "" : "Problem",
                    });
                }
            }
            else
            {
                foreach (var (name, value) in profile.Flags)
                {
                    if (MatchesSearch(name))
                        rows.Add(ProfileRow(name, value));
                }

                foreach (string name in profile.Remove)
                {
                    if (!MatchesSearch(name) || profile.Flags.ContainsKey(name))
                        continue;

                    string? yours = App.FastFlags.GetValue(name);
                    rows.Add(new FlagRow
                    {
                        Name = name,
                        Value = "",
                        IsTurnedOff = true,
                        Note = yours is null ? "Turned off (it isn't in your flags right now)" : $"Turned off for these games (yours is {yours})",
                        Tone = "Off",
                    });
                }

                if (_showYoursInProfile)
                {
                    foreach (var (name, raw) in App.FastFlags.Prop)
                    {
                        if (profile.Flags.ContainsKey(name) || profile.Remove.Contains(name) || !MatchesSearch(name))
                            continue;

                        if (_hideQuickFlags && QuickFlagNames.Contains(name))
                            continue;

                        rows.Add(new FlagRow
                        {
                            Name = name,
                            Value = raw?.ToString() ?? "",
                            IsInherited = true,
                            Note = "From your flags",
                            Tone = "",
                        });
                    }
                }
            }

            foreach (FlagRow row in rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
                _rows.Add(row);

            RebuildGroups();
            UpdateEmptyText(profile);

            if (selectedName is null)
                return;

            FlagRow? again = _rows.FirstOrDefault(r => r.Name == selectedName);
            if (again is not null)
            {
                DataGrid.SelectedItem = again;
                DataGrid.ScrollIntoView(again);
            }
        }

        private static SymbolRegular SymbolFor(string category) => category switch
        {
            FlagCategories.FrameRate => SymbolRegular.TopSpeed24,
            FlagCategories.Rendering => SymbolRegular.Gauge24,
            FlagCategories.Network => SymbolRegular.Router24,
            FlagCategories.Interface => SymbolRegular.WindowBulletList20,
            FlagCategories.Sound => SymbolRegular.Chat24,
            FlagCategories.Telemetry => SymbolRegular.ShieldCheckmark24,
            FlagCategories.Debug => SymbolRegular.Wrench24,
            _ => SymbolRegular.Flag24,
        };

        // The card view: the same rows, one card per category.
        private void RebuildGroups()
        {
            _groups.Clear();

            foreach (var group in _rows
                .GroupBy(r => r.Category, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => FlagCategories.OrderOf(g.Key))
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                _groups.Add(new FlagGroup
                {
                    Name = group.Key,
                    Symbol = SymbolFor(group.Key),
                    Rows = group.ToList(),
                });
            }

            if (_cardRow is not null && !_rows.Contains(_cardRow))
                _cardRow = _rows.FirstOrDefault(r => r.Name == _cardRow.Name);
        }

        private void UpdateEmptyText(FlagProfile? profile)
        {
            string text = "";

            if (_rows.Count == 0)
            {
                if (_searchFilter.Length > 0)
                    text = "No flags match your search.";
                else if (profile is not null)
                    text = "This profile is empty. Add flags with Add new or Search Database, or pick flags of yours this game shouldn't get with Turn off one of your flags. You can also right-click flags under \"Your flags\" to copy them here.";
                else if (App.FastFlags.Prop.Count > 0 && _hideQuickFlags)
                    text = "Every flag you have comes from the Roblox FFlags toggles - turn off Hide toggle flags to see them. Add your own with Add new or Search Database.";
                else
                    text = "No flags yet. Add one with Add new or Search Database.";
            }

            EmptyText.Text = text;
            EmptyText.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private static FlagRow ProfileRow(string name, string value)
        {
            string? problem = FlagValidation.Problem(name, value);
            string? yours = App.FastFlags.GetValue(name);

            return new FlagRow
            {
                Name = name,
                Value = value,
                Note = problem ?? (yours is null ? "Added for these games" : yours == value ? "Same as your flags" : $"Changes your value ({yours})"),
                Tone = problem is not null ? "Problem" : yours is null ? "Added" : "Changed",
            };
        }

        private bool MatchesSearch(string name) => _searchFilter.Length == 0 || name.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase);

        private void ClearSearch(bool refresh = true)
        {
            SearchTextBox.Text = "";
            _searchFilter = "";

            if (refresh)
                ReloadList();
        }

        /// <summary>Shows one of your own flags (every game scope), used when it is picked from the top bar search.</summary>
        public void FocusFlag(string name)
        {
            if (_profileId is not null)
            {
                _profileId = null;
                RefreshScopes();
            }

            if (QuickFlagNames.Contains(name) && _hideQuickFlags)
            {
                TogglePresetsButton.IsChecked = false;
                _hideQuickFlags = false;
            }

            if (!_tableView)
            {
                // The card view has no selection: narrow the list down to that flag instead.
                SearchTextBox.Text = name;
                _searchFilter = name;
                _searchDebounce.Stop();
                ReloadList();
                _cardRow = _rows.FirstOrDefault(r => r.Name == name);
                return;
            }

            ClearSearch();
            Select(name);
            DataGrid.Focus();
        }

        private void Select(string name)
        {
            FlagRow? row = _rows.FirstOrDefault(r => r.Name == name);
            if (row is null)
                return;

            DataGrid.SelectedItem = row;
            DataGrid.ScrollIntoView(row);
        }

        private bool ScopeHas(string name)
        {
            FlagProfile? profile = Profile;
            return profile is null ? App.FastFlags.GetValue(name) is not null : profile.Flags.ContainsKey(name);
        }

        private void SetInScope(string name, string value)
        {
            FlagProfile? profile = Profile;

            if (profile is null)
            {
                App.FastFlags.SetValue(name, value);
                return;
            }

            profile.Flags[name] = value;
            profile.Remove.Remove(name);
            MarkProfilesEdited();
        }

        private void RemoveFromScope(string name)
        {
            FlagProfile? profile = Profile;

            if (profile is null)
            {
                App.FastFlags.SetValue(name, null);
                return;
            }

            profile.Flags.Remove(name);
            profile.Remove.Remove(name);
            MarkProfilesEdited();
        }

        private void ShowAddDialog() => ShowAddDialog(jsonTab: false);

        private void ShowAddDialog(bool jsonTab)
        {
            var dialog = new AddFastFlagDialog { Owner = Owner };
            if (jsonTab)
                dialog.Tabs.SelectedIndex = 1;
            dialog.ShowDialog();

            if (dialog.Result != MessageBoxResult.OK)
                return;

            if (dialog.Tabs.SelectedIndex == 0)
                AddSingle(dialog.FlagNameTextBox.Text.Trim(), dialog.FlagValueTextBox.Text.Trim());
            else if (dialog.Tabs.SelectedIndex == 1)
                ImportJSON(dialog.JsonTextBox.Text);
        }

        private string? AddFromDatabase(string name, string value)
        {
            if (ScopeHas(name))
                return "already there";

            if (FlagValidation.Problem(name, value) is string problem)
                return problem.TrimEnd('.');

            SetInScope(name, value);

            if (!MatchesSearch(name))
                ClearSearch(false);

            ReloadList();
            UpdateScopeUi();
            return null;
        }

        private void AddSingle(string name, string value)
        {
            if (ScopeHas(name))
            {
                Frontend.ShowMessageBox(Strings.Menu_FastFlagEditor_AlreadyExists, MessageBoxImage.Information);

                if (QuickFlagNames.Contains(name) && _hideQuickFlags)
                {
                    TogglePresetsButton.IsChecked = false;
                    _hideQuickFlags = false;
                }

                ClearSearch();
                Select(name);
                return;
            }

            string? problem = FlagValidation.Problem(name, value);
            if (problem is not null)
            {
                Frontend.ShowMessageBox($"{name}\n\n{problem}", MessageBoxImage.Warning);
                return;
            }

            SetInScope(name, value);

            if (!MatchesSearch(name))
                ClearSearch(false);

            ReloadList();
            Select(name);
            UpdateScopeUi();
        }

        private void ImportJSON(string json)
        {
            Dictionary<string, object>? list;

            json = json.Trim();

            if (!json.StartsWith('{'))
                json = '{' + json;

            if (!json.EndsWith('}'))
            {
                int lastIndex = json.LastIndexOf('}');
                json = lastIndex == -1 ? json + '}' : json[..(lastIndex + 1)];
            }

            try
            {
                var options = new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
                list = JsonSerializer.Deserialize<Dictionary<string, object>>(json, options) ?? throw new Exception("JSON deserialization returned null");
            }
            catch (Exception ex)
            {
                Frontend.ShowMessageBox(string.Format(Strings.Menu_FastFlagEditor_InvalidJSON, ex.Message), MessageBoxImage.Error);
                ShowAddDialog();
                return;
            }

            if (list.Count > 16)
            {
                if (Frontend.ShowMessageBox(Strings.Menu_FastFlagEditor_LargeConfig, MessageBoxImage.Warning, MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                    return;
            }

            List<string> conflicting = list.Keys.Where(ScopeHas).ToList();
            bool overwrite = false;

            if (conflicting.Count > 0)
            {
                string message = string.Format(Strings.Menu_FastFlagEditor_ConflictingImport, conflicting.Count, string.Join(", ", conflicting.Take(25)));
                if (conflicting.Count > 25)
                    message += "...";

                overwrite = Frontend.ShowMessageBox(message, MessageBoxImage.Question, MessageBoxButton.YesNo) == MessageBoxResult.Yes;
            }

            var skipped = new List<string>();
            int added = 0;

            foreach (var (key, raw) in list)
            {
                string? value = raw?.ToString();
                if (value is null)
                    continue;

                if (ScopeHas(key) && !overwrite)
                    continue;

                if (FlagValidation.Problem(key, value) is string problem)
                {
                    skipped.Add($"{key}: {problem}");
                    continue;
                }

                SetInScope(key, value);
                added++;
            }

            ClearSearch();
            UpdateScopeUi();

            if (skipped.Count > 0)
            {
                Frontend.ShowMessageBox(
                    $"Added {added} flag(s). {skipped.Count} were left out because they are not valid:\n\n" + string.Join("\n", skipped.Take(15)) + (skipped.Count > 15 ? "\n..." : ""),
                    MessageBoxImage.Warning);
            }
        }

        private void DataGrid_BeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
        {
            if (e.Row.DataContext is FlagRow { IsTurnedOff: true } row && e.Column.DisplayIndex == 1)
                row.Value = App.FastFlags.GetValue(row.Name) ?? "";

            if (e.Row.DataContext is FlagRow { IsInherited: true } && e.Column.DisplayIndex == 0)
                e.Cancel = true;
        }

        private void DataGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit || e.Row.DataContext is not FlagRow row || e.EditingElement is not TextBox textbox)
                return;

            string text = textbox.Text.Trim();

            if (e.Column.DisplayIndex == 0)
            {
                string oldName = row.Name;
                if (text == oldName)
                    return;

                string value = row.IsTurnedOff ? "" : row.Value;

                if (ScopeHas(text))
                {
                    Frontend.ShowMessageBox(Strings.Menu_FastFlagEditor_AlreadyExists, MessageBoxImage.Information);
                    e.Cancel = true;
                    textbox.Text = oldName;
                    return;
                }

                if (FlagValidation.NameProblem(text) is string nameProblem)
                {
                    Frontend.ShowMessageBox(nameProblem, MessageBoxImage.Warning);
                    e.Cancel = true;
                    textbox.Text = oldName;
                    return;
                }

                FlagProfile? profile = Profile;
                if (row.IsTurnedOff && profile is not null)
                {
                    profile.Remove.Remove(oldName);
                    if (!profile.Remove.Contains(text))
                        profile.Remove.Add(text);
                    MarkProfilesEdited();
                }
                else
                {
                    RemoveFromScope(oldName);
                    SetInScope(text, value);
                }

                row.Name = text;
            }
            else if (e.Column.DisplayIndex == 1)
            {
                if (row.IsInherited && text == row.Value)
                    return;

                if (!CommitValue(row, text))
                {
                    e.Cancel = true;
                    textbox.Text = row.IsTurnedOff ? "" : row.Value;
                    return;
                }
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                ReloadList();
                UpdateScopeUi();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        /// <summary>Gives a flag a new value in the scope being edited (both views use this). False when the value isn't valid.</summary>
        private bool CommitValue(FlagRow row, string text)
        {
            if (FlagValidation.Problem(row.Name, text) is string problem)
            {
                Frontend.ShowMessageBox($"{row.Name}\n\n{problem}", MessageBoxImage.Warning);
                return false;
            }

            SetInScope(row.Name, text);
            row.Value = text;
            row.IsTurnedOff = false;
            row.IsInherited = false;
            return true;
        }

        // Card view: refresh one row's note in place, so typing on into the next box keeps its focus.
        private void RefreshRowNote(FlagRow row)
        {
            FlagProfile? profile = Profile;

            if (profile is null)
            {
                string? problem = FlagValidation.Problem(row.Name, row.Value);
                row.Note = problem ?? (QuickFlagNames.Contains(row.Name) ? "Set by a Roblox FFlags toggle" : "");
                row.Tone = problem is null ? "" : "Problem";
                return;
            }

            FlagRow fresh = ProfileRow(row.Name, row.Value);
            row.Note = fresh.Note;
            row.Tone = fresh.Tone;
        }

        private void AfterCardEdit(FlagRow row)
        {
            RefreshRowNote(row);
            RefreshScopes();
            UpdateScopeUi();
        }

        private void CardSwitch_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton toggle || toggle.Tag is not FlagRow row)
                return;

            _cardRow = row;
            string value = toggle.IsChecked == true ? "True" : "False";

            if (!CommitValue(row, value))
            {
                toggle.IsChecked = row.IsOn;
                return;
            }

            AfterCardEdit(row);
        }

        private void CardValue_KeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not TextBox box || box.Tag is not FlagRow row)
                return;

            if (e.Key == Key.Enter)
            {
                CommitCardValue(box, row);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                box.Text = row.IsTurnedOff ? "" : row.Value;
                box.SelectAll();
                e.Handled = true;
            }
        }

        private void CardValue_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is TextBox box && box.Tag is FlagRow row)
                CommitCardValue(box, row);
        }

        private void CommitCardValue(TextBox box, FlagRow row)
        {
            string text = box.Text.Trim();
            string current = row.IsTurnedOff ? "" : row.Value;

            if (text == current || (text.Length == 0 && row.IsTurnedOff))
                return;

            if (row.IsInherited && text == row.Value)
                return;

            if (!CommitValue(row, text))
            {
                box.Text = current;
                return;
            }

            AfterCardEdit(row);
        }

        private void CardDelete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: FlagRow row })
                DeleteRows(new List<FlagRow> { row });
        }

        private void CardRow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: FlagRow row } element)
                return;

            _cardRow = row;

            // Keep keyboard shortcuts working on the row unless a value box or switch was clicked.
            if (e.OriginalSource is DependencyObject source && FindAncestor<TextBox>(source) is null && FindAncestor<ButtonBase>(source) is null)
                element.Focus();
        }

        private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
        {
            while (node is not null && node is not T)
                node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
            return node as T;
        }

        private void CardRow_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: FlagRow row, ContextMenu: System.Windows.Controls.ContextMenu menu })
            {
                e.Handled = true;
                return;
            }

            _cardRow = row;
            FillRowMenu(menu, new List<FlagRow> { row }, fromTable: false);
        }

        private void CardsView_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.OriginalSource is TextBox || _cardRow is null || !_rows.Contains(_cardRow))
                return;

            FlagRow row = _cardRow;

            if (e.Key == Key.Delete)
            {
                DeleteRows(new List<FlagRow> { row });
                e.Handled = true;
            }
            else if (e.Key == Key.F2)
            {
                EditValue(row);
                e.Handled = true;
            }
            else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
            {
                CopyAsJson(new List<FlagRow> { row });
                e.Handled = true;
            }
        }

        private void AddButton_Click(object sender, RoutedEventArgs e) => ShowAddDialog();

        private void SearchDatabaseButton_Click(object sender, RoutedEventArgs e) => OpenDatabase(null);

        private void OpenDatabase(string? query)
        {
            FlagProfile? profile = Profile;
            string target = profile is null ? "your flags" : $"profile \"{profile.Name}\"";

            var dialog = new FFlagSearchDialog(AddFromDatabase, target, query) { Owner = Owner };
            dialog.ShowDialog();
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e) => DeleteSelected();

        private void CleanList_Click(object sender, RoutedEventArgs e)
        {
            FlagProfile? profile = Profile;
            List<string> ignored = PhasmaStrap.Utility.FlagAllowlist.Ignored(profile is null ? App.FastFlags.Prop.Keys : profile.Flags.Keys);

            if (ignored.Count == 0)
            {
                Frontend.ShowMessageBox($"Every flag here is on Roblox's allowlist (as of {PhasmaStrap.Utility.FlagAllowlist.AsOf}), so there is nothing to remove.", MessageBoxImage.Information);
                return;
            }

            string sample = string.Join("\n", ignored.Take(8)) + (ignored.Count > 8 ? $"\n...and {ignored.Count - 8} more" : "");

            var answer = Frontend.ShowMessageBox(
                $"{ignored.Count} flag(s) here are not on Roblox's allowlist (as of {PhasmaStrap.Utility.FlagAllowlist.AsOf}), so the Roblox player ignores them:\n\n{sample}\n\nRemove them?",
                MessageBoxImage.Warning,
                MessageBoxButton.YesNo);

            if (answer != MessageBoxResult.Yes)
                return;

            foreach (string name in ignored)
            {
                if (profile is null)
                    App.FastFlags.SetValue(name, null);
                else
                    profile.Flags.Remove(name);
            }

            if (profile is not null)
                MarkProfilesEdited();

            App.Logger.WriteLine("FastFlagEditorPage", $"Clean list removed {ignored.Count} flag(s) Roblox ignores from {(profile is null ? "your flags" : $"profile {profile.Name}")}");

            ReloadList();
            RefreshScopes();
            UpdateScopeUi();
            UpdateEmptyText(Profile);
        }

        private void DeleteSelected() => DeleteRows(DataGrid.SelectedItems.OfType<FlagRow>().ToList());

        private void DeleteRows(List<FlagRow> doomed)
        {
            FlagProfile? profile = Profile;
            bool turnedOff = false;
            bool changed = false;

            foreach (FlagRow row in doomed)
            {
                if (profile is not null && row.IsInherited)
                {
                    if (!profile.Remove.Contains(row.Name))
                        profile.Remove.Add(row.Name);
                    turnedOff = true;
                    continue;
                }

                _rows.Remove(row);
                RemoveFromScope(row.Name);
                changed = true;
            }

            if (turnedOff)
            {
                MarkProfilesEdited();
                ReloadList();
            }
            else if (changed)
            {
                RebuildGroups();
            }

            RefreshScopes();
            UpdateScopeUi();
            UpdateEmptyText(Profile);
        }

        private void TurnOffButton_Click(object sender, RoutedEventArgs e)
        {
            FlagProfile? profile = Profile;
            if (profile is null)
                return;

            var candidates = App.FastFlags.Prop
                .Where(f => !profile.Remove.Contains(f.Key) && !profile.Flags.ContainsKey(f.Key))
                .OrderBy(f => f.Key, StringComparer.OrdinalIgnoreCase)
                .Select(f => (f.Key, f.Value?.ToString() ?? ""))
                .ToList();

            if (candidates.Count == 0)
            {
                Frontend.ShowMessageBox("There is nothing left to turn off - every one of your flags is already changed or turned off in this profile.", MessageBoxImage.Information);
                return;
            }

            var dialog = new FlagPickerDialog("Turn off flags for this profile",
                $"Pick flags from your own list that games using \"{profile.Name}\" should NOT get. Ctrl+click picks several.",
                candidates)
            { Owner = Owner };

            if (dialog.ShowDialog() != true || dialog.Picked.Count == 0)
                return;

            foreach (string name in dialog.Picked)
            {
                if (!profile.Remove.Contains(name))
                    profile.Remove.Add(name);
            }

            MarkProfilesEdited();
            ClearSearch();
            UpdateScopeUi();
        }

        private void ToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton button)
                return;

            _hideQuickFlags = button.IsChecked ?? false;
            ReloadList();
        }

        private void ShowYoursButton_Click(object sender, RoutedEventArgs e)
        {
            _showYoursInProfile = ShowYoursButton.IsChecked ?? true;
            ReloadList();
        }

        private void DataGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            List<FlagRow> rows = DataGrid.SelectedItems.OfType<FlagRow>().ToList();
            if (rows.Count == 0 || DataGrid.ContextMenu is not System.Windows.Controls.ContextMenu menu)
            {
                e.Handled = true;
                return;
            }

            FillRowMenu(menu, rows, fromTable: true);
        }

        // The right click menu for one or more flags, shared by the table and the cards.
        private void FillRowMenu(System.Windows.Controls.ContextMenu menu, List<FlagRow> rows, bool fromTable)
        {
            menu.Items.Clear();

            if (rows.Count == 1)
            {
                FlagRow single = rows[0];
                menu.Items.Add(MenuItem("Edit value", !fromTable || !DataGrid.IsReadOnly, () => EditValue(single), "F2"));
                menu.Items.Add(MenuItem("Copy name", true, () => ClipboardShare.CopyText(single.Name)));
            }

            menu.Items.Add(MenuItem("Copy as JSON", true, () => CopyAsJson(rows), "Ctrl+C"));

            if (rows.Count == 1)
            {
                FlagRow single = rows[0];
                menu.Items.Add(MenuItem("Look it up in the database", true, () => OpenDatabase(single.Name)));
            }

            menu.Items.Add(new Separator());

            FlagProfile? current = Profile;
            List<FlagProfile> others = App.FlagProfiles.Prop.Profiles.Where(p => p.Id != current?.Id).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
            string what = rows.Count == 1 ? "this flag" : $"these {rows.Count} flags";

            if (current is null)
            {
                menu.Items.Add(ProfileSubmenu($"Copy {what} to a profile", others, profile => CopyRows(rows, profile, move: false)));
                menu.Items.Add(ProfileSubmenu($"Move {what} to a profile", others, profile => CopyRows(rows, profile, move: true)));
                menu.Items.Add(ProfileSubmenu($"Turn {what} off in a profile", others, profile => TurnOffIn(rows, profile)));
            }
            else
            {
                List<FlagRow> valued = rows.Where(r => !r.IsTurnedOff && !r.IsInherited).ToList();
                List<FlagRow> yours = rows.Where(r => r.IsInherited).ToList();

                if (yours.Count > 0)
                    menu.Items.Add(MenuItem(yours.Count == 1 ? "Turn this flag off for these games" : $"Turn these {yours.Count} flags off for these games", true, () => { TurnOffIn(yours, current); ReloadList(); }));

                menu.Items.Add(MenuItem($"Copy {what} to your flags", valued.Count > 0, () => CopyRows(valued, null, move: false)));
                menu.Items.Add(MenuItem($"Move {what} to your flags", valued.Count > 0, () => CopyRows(valued, null, move: true)));
                menu.Items.Add(ProfileSubmenu($"Copy {what} to another profile", others, profile => CopyRows(rows, profile, move: false)));
            }

            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItem("Delete", true, () => DeleteRows(rows), "Del", danger: true));
        }

        private static void CopyAsJson(List<FlagRow> rows)
        {
            var dictionary = rows.Where(r => !r.IsTurnedOff).ToDictionary(r => r.Name, r => (object)r.Value);
            ClipboardShare.CopyText(JsonSerializer.Serialize(dictionary, new JsonSerializerOptions { WriteIndented = true }));
        }

        // Right click picks the clicked row first, so the menu acts on it.
        private void DataGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            DependencyObject? current = e.OriginalSource as DependencyObject;
            while (current is not null && current is not DataGridRow)
                current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);

            if (current is DataGridRow { IsSelected: false } row)
            {
                DataGrid.SelectedItems.Clear();
                row.IsSelected = true;
            }
        }

        private void DataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.OriginalSource is TextBox || DataGrid.SelectedItems.Count == 0)
                return;

            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
            {
                CopyAsJson(DataGrid.SelectedItems.OfType<FlagRow>().ToList());
                e.Handled = true;
                return;
            }

            if (e.Key != Key.Delete)
                return;

            DeleteSelected();
            e.Handled = true;
        }

        private void EditValue(FlagRow row)
        {
            if (!_tableView)
            {
                // Card view: ask for the value in a small box.
                string? text = AskName("Edit value", $"New value for {row.Name}:", row.IsTurnedOff ? "" : row.Value);
                if (text is null)
                    return;

                text = text.Trim();
                if (text == (row.IsTurnedOff ? "" : row.Value) || !CommitValue(row, text))
                    return;

                AfterCardEdit(row);
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                DataGrid.SelectedItem = row;
                DataGrid.ScrollIntoView(row);
                DataGrid.CurrentCell = new DataGridCellInfo(row, DataGrid.Columns[1]);
                DataGrid.Focus();
                DataGrid.BeginEdit();
            }), System.Windows.Threading.DispatcherPriority.Input);
        }

        private MenuItem ProfileSubmenu(string header, List<FlagProfile> profiles, Action<FlagProfile> action)
        {
            var parent = new MenuItem { Header = header };

            foreach (FlagProfile profile in profiles)
            {
                FlagProfile target = profile;
                parent.Items.Add(MenuItem(target.Name, true, () => action(target)));
            }

            if (profiles.Count > 0)
                parent.Items.Add(new Separator());

            parent.Items.Add(MenuItem("New profile...", true, () =>
            {
                FlagProfile? created = CreateProfile();
                if (created is not null)
                    action(created);
            }));

            return parent;
        }

        private void CopyRows(List<FlagRow> rows, FlagProfile? target, bool move)
        {
            FlagProfile? source = Profile;

            foreach (FlagRow row in rows)
            {
                if (target is null)
                {
                    if (!row.IsTurnedOff)
                        App.FastFlags.SetValue(row.Name, row.Value);
                }
                else if (row.IsTurnedOff)
                {
                    if (!target.Remove.Contains(row.Name))
                        target.Remove.Add(row.Name);
                }
                else
                {
                    target.Flags[row.Name] = row.Value;
                    target.Remove.Remove(row.Name);
                }

                if (move)
                {
                    if (source is null)
                        App.FastFlags.SetValue(row.Name, null);
                    else
                    {
                        source.Flags.Remove(row.Name);
                        source.Remove.Remove(row.Name);
                    }
                }
            }

            MarkProfilesEdited();
            ReloadList();
            UpdateScopeUi();

            string where = target is null ? "your flags" : $"\"{target.Name}\"";
            string verb = move ? "Moved" : "Copied";
            string hint = target is not null && !App.FlagProfiles.RulesUsing(target.Id).Any() ? " No game uses that profile yet - give it one on the Per-game flags tab." : "";
            ShowStatus($"{verb} {rows.Count} flag(s) to {where}.{hint}");
        }

        private void TurnOffIn(List<FlagRow> rows, FlagProfile target)
        {
            foreach (FlagRow row in rows)
            {
                if (!target.Remove.Contains(row.Name))
                    target.Remove.Add(row.Name);
                target.Flags.Remove(row.Name);
            }

            MarkProfilesEdited();
            ShowStatus($"Games using \"{target.Name}\" won't get {(rows.Count == 1 ? rows[0].Name : $"these {rows.Count} flags")}.");
        }

        private void ShowStatus(string message) => Frontend.ShowMessageBox(message, MessageBoxImage.Information);

        private static readonly Regex _groupPrefixRegex = new("^[A-Z]+[a-z]*", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private void ExportJSONButton_Click(object sender, RoutedEventArgs e)
        {
            FlagProfile? profile = Profile;
            Dictionary<string, object> flags = profile is null
                ? App.FastFlags.Prop
                : profile.Flags.ToDictionary(f => f.Key, f => (object)f.Value);

            if (flags.Count == 0)
            {
                Frontend.ShowMessageBox(Strings.Menu_FastFlagEditor_NoFlagsToCopy, MessageBoxImage.Information);
                return;
            }

            var dialog = new CopyFlagsDialog(flags.Count) { Owner = Owner };
            dialog.ShowDialog();

            if (dialog.Result != MessageBoxResult.OK)
                return;

            var options = new JsonSerializerOptions { WriteIndented = true };

            string payload = dialog.SelectedFormat switch
            {
                CopyFlagsFormat.GroupedJson => BuildGroupedJson(flags),
                CopyFlagsFormat.Base64 => Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(flags, options))),
                _ => JsonSerializer.Serialize(flags, options)
            };

            System.Windows.Clipboard.SetDataObject(payload);

            string message = Strings.Menu_FastFlagEditor_JsonCopiedToClipboard;
            if (profile is not null && profile.Remove.Count > 0)
                message += $"\n\nThe {profile.Remove.Count} flag(s) this profile turns off can't be written as JSON. Use Profile options > Copy share code to share the whole profile.";

            Frontend.ShowMessageBox(message, MessageBoxImage.Information);
        }

        private static string BuildGroupedJson(Dictionary<string, object> prop)
        {
            var groups = prop
                .GroupBy(kvp =>
                {
                    var match = _groupPrefixRegex.Match(kvp.Key);
                    return match.Success ? match.Value : "Other";
                })
                .OrderBy(g => g.Key)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine("{");

            for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                sb.Append("  // ").AppendLine(groups[groupIndex].Key);

                var entries = groups[groupIndex].OrderBy(kvp => kvp.Key).ToList();
                for (int i = 0; i < entries.Count; i++)
                {
                    bool last = i == entries.Count - 1 && groupIndex == groups.Count - 1;
                    sb.Append("  \"").Append(entries[i].Key).Append("\": ").Append(JsonSerializer.Serialize(entries[i].Value));
                    sb.AppendLine(last ? "" : ",");
                }
            }

            sb.AppendLine("}");
            return sb.ToString();
        }

        private readonly System.Windows.Threading.DispatcherTimer _searchDebounce = new() { Interval = TimeSpan.FromMilliseconds(120) };
        private bool _searchDebounceHooked;

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox textbox)
                return;

            _searchFilter = textbox.Text;

            if (!_searchDebounceHooked)
            {
                _searchDebounce.Tick += (_, _) =>
                {
                    _searchDebounce.Stop();
                    ReloadList();
                };
                _searchDebounceHooked = true;
            }

            _searchDebounce.Stop();
            _searchDebounce.Start();
        }
    }
}
