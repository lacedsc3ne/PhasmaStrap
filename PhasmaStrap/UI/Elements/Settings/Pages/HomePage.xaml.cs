using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

using Wpf.Ui.Common;

using PhasmaStrap.Enums;
using PhasmaStrap.Integrations;
using PhasmaStrap.UI.Elements.Controls;
using PhasmaStrap.UI.Elements.Settings.Search;
using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class HomePage : ISearchToolHost
    {
        private readonly HomeViewModel _viewModel;

        private GameViewModel? _watchedGame;

        public HomePage()
        {
            _viewModel = new HomeViewModel();
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            _viewModel.OpenPartyRequested += (_, _) =>
            {
                if (Window.GetWindow(this) is MainWindow window)
                    window.Navigate(typeof(PartyPage));
            };
            DataContext = _viewModel;
            InitializeComponent();

            Loaded += (_, _) => ApplyHomeBackground();

            // Every tile list on the page shares one menu.
            ItemMenu.Attach<GameCard>(GamesRoot, BuildGameMenu);
        }

        private void BuildGameMenu(GameCard card, ItemMenu menu)
        {
            long placeId = card.PlaceId;
            long startPlaceId = placeId > 0 ? PlaceNames.StartPlaceOf(placeId) : 0;
            bool known = placeId > 0;
            bool hasUniverse = card.UniverseId > 0;
            string? ownProfileId = FlagLayers.ProfileFor(App.FlagProfiles.Prop, placeId, card.UniverseId)?.Id;

            menu.Add("Launch", SymbolRegular.Play24, () => _viewModel.PlayCommand.Execute(card), gesture: "Enter", enabled: known, bold: true)
                .Sub("Launch with account", SymbolRegular.PersonSwap24, sub => FillAccountMenu(sub, RobloxLaunch.DeepLink(startPlaceId)), enabled: known)
                .Sub("Launch with flag profile", SymbolRegular.Flag24, sub => FillOneTimeProfileMenu(sub, card.UniverseId, startPlaceId, () => _viewModel.PlayCommand.Execute(card)), enabled: known)
                .Sub("Use a flag profile", SymbolRegular.Flag24, sub => FillProfileMenu(sub, card.UniverseId, startPlaceId, card.Name, card.IconUrl, _viewModel.RefreshProfiles), enabled: hasUniverse);

            if (ownProfileId is not null)
                menu.Add("Edit its flags", SymbolRegular.Edit24, () => OpenFlagEditor(ownProfileId));
            else
                menu.Add("Give it its own flags", SymbolRegular.Wrench24, () =>
                {
                    string id = HomeViewModel.CreateOwnProfile(card.UniverseId, startPlaceId, card.Name, card.IconUrl);
                    _viewModel.RefreshProfiles();
                    OpenFlagEditor(id);
                }, enabled: hasUniverse);

            menu.Add("Pick a server", SymbolRegular.Server24, () => _viewModel.OpenGame(card))
                .Separator()
                .Copy("Copy game link", known ? $"https://www.roblox.com/games/{placeId}" : null)
                .Copy("Copy place ID", known ? placeId.ToString() : null)
                .Add("Create desktop shortcut", SymbolRegular.Desktop24, () => _ = CreateShortcutAsync(startPlaceId), enabled: known)
                .Add("Open on the website", SymbolRegular.Open24, () => Utilities.ShellExecute($"https://www.roblox.com/games/{placeId}"), enabled: known);

            if (_viewModel.IsInLibrary(card))
            {
                menu.Separator()
                    .Add("Remove from library…", SymbolRegular.Delete24, () =>
                    {
                        MessageBoxResult answer = Frontend.ShowMessageBox(
                            $"Remove \"{card.Name}\" from your library?\n\nIts play time is forgotten. It comes back the next time you play it.",
                            MessageBoxImage.Question, MessageBoxButton.YesNo);

                        if (answer == MessageBoxResult.Yes)
                            _viewModel.RemoveFromLibrary(card);
                    }, danger: true);
            }
        }

        /// <summary>One item per saved account, each signing in as that account and opening <paramref name="uri"/>.</summary>
        internal static void FillAccountMenu(ItemMenu sub, string uri)
        {
            foreach (AccountQuickSwitch.Account account in HomeViewModel.SavedAccounts())
                sub.Add(account.Title, SymbolRegular.Person24, () => _ = HomeViewModel.LaunchAsAccountAsync(account, uri));
        }

        /// <summary>Your usual flags plus every profile, with a check on the one the whole game uses now.</summary>
        internal static void FillProfileMenu(ItemMenu sub, long universeId, long rootPlaceId, string gameName, string iconUrl, Action? changed = null)
        {
            string current = HomeViewModel.ProfileIdFor(universeId);

            void Option(string name, string id)
            {
                bool selected = id == current;
                sub.Add(name, selected ? SymbolRegular.Checkmark24 : SymbolRegular.Flag24, () =>
                {
                    HomeViewModel.AssignProfile(universeId, rootPlaceId, gameName, iconUrl, id);
                    changed?.Invoke();
                }, enabled: !selected);
            }

            Option("Your usual flags", "");

            foreach (FlagProfile profile in App.FlagProfiles.Prop.Profiles.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
                Option(profile.Name, profile.Id);
        }

        /// <summary>
        /// Your usual flags plus every profile, each launching once with that profile without changing what the game
        /// normally starts with. Needs the FastFlag manager, since that is what writes the flags at launch.
        /// </summary>
        internal static void FillOneTimeProfileMenu(ItemMenu sub, long universeId, long placeId, Action launch)
        {
            bool usable = App.Settings.Prop.UseFastFlagManager;

            void Option(string name, string id) => sub.Add(name, SymbolRegular.Flag24, () =>
            {
                FlagProfileSession.RequestOneTime(id, universeId, placeId);
                launch();
            }, enabled: usable);

            Option("Your usual flags", "");

            foreach (FlagProfile profile in App.FlagProfiles.Prop.Profiles.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
                Option(profile.ChangeCount == 0 ? $"{profile.Name} (empty)" : profile.Name, profile.Id);
        }

        /// <summary>The button next to "Pick a server": launch one of the game's places, or launch with a flag profile once.</summary>
        private async void HeroPlaces_Click(object sender, RoutedEventArgs e)
        {
            GameCard? hero = _viewModel.Hero;
            if (hero is null || hero.PlaceId <= 0)
                return;

            List<UniversePlace> places = new();
            try
            {
                long universe = hero.UniverseId > 0 ? hero.UniverseId : await UniversePlaces.GetUniverseIdAsync(hero.PlaceId) ?? 0;
                if (universe > 0)
                    places = await UniversePlaces.GetPlacesAsync(universe, PlaceNames.StartPlaceOf(hero.PlaceId));
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("HomePage", $"Places lookup failed: {ex.Message}");
            }

            long startPlaceId = PlaceNames.StartPlaceOf(hero.PlaceId);
            var menu = new ItemMenu();

            if (places.Count > 1)
            {
                foreach (UniversePlace place in places.Take(20))
                {
                    long placeId = place.PlaceId;
                    menu.Add(place.IsRootPlace ? $"{place.Name} (start place)" : place.Name, SymbolRegular.Play24,
                        () => HomeViewModel.LaunchUri(RobloxLaunch.DeepLink(placeId)), bold: placeId == hero.PlaceId);
                }

                menu.Separator();
            }

            menu.Sub("Launch with flag profile", SymbolRegular.Flag24, sub => FillOneTimeProfileMenu(sub, hero.UniverseId, startPlaceId, () => _viewModel.PlayCommand.Execute(hero)))
                .Sub("Launch with account", SymbolRegular.PersonSwap24, sub => FillAccountMenu(sub, RobloxLaunch.DeepLink(startPlaceId)))
                .Separator()
                .Add("Open the game page", SymbolRegular.Open24, () => _viewModel.OpenGame(hero));

            OpenBelow(menu, HeroPlacesButton);
        }

        /// <summary>Opens a menu as a dropdown under <paramref name="button"/>.</summary>
        internal static void OpenBelow(ItemMenu menu, UIElement button)
        {
            if (menu.Count == 0)
                return;

            menu.Menu.PlacementTarget = button;
            menu.Menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.Menu.IsOpen = true;
        }

        private void OpenFlagEditor(string profileId)
        {
            FlagEditorRequest.ProfileToOpen = profileId;

            if (Window.GetWindow(this) is MainWindow window)
                window.Navigate(typeof(FastFlagEditorPage));
        }

        private static async Task CreateShortcutAsync(long placeId)
        {
            GameShortcutCreator.Result result = await GameShortcutCreator.CreateAsync(placeId.ToString(), Paths.Desktop);

            NotificationCenter.Notify(result.Success ? "Shortcut added to your desktop" : "Couldn't make the shortcut", result.Message, NotificationCategory.General);
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(HomeViewModel.Tab))
            {
                // Keeps the tab strip in step when a tab is opened from elsewhere (a game's page, search).
                System.Windows.Controls.RadioButton? tab = _viewModel.Tab switch
                {
                    HomeViewModel.TabHome => HomeTab,
                    HomeViewModel.TabLibrary => LibraryTab,
                    HomeViewModel.SectionServerBrowser => ServerBrowserTab,
                    HomeViewModel.SectionPrivateServers => PrivateTab,
                    HomeViewModel.SectionHistory => HistoryTab,
                    _ => null,
                };

                if (tab is not null && tab.IsChecked != true)
                    tab.IsChecked = true;

                return;
            }

            if (e.PropertyName != nameof(HomeViewModel.Game))
                return;

            if (_watchedGame is not null)
                _watchedGame.OpenPageRequested -= Game_OpenPageRequested;

            _watchedGame = _viewModel.Game;

            if (_watchedGame is not null)
                _watchedGame.OpenPageRequested += Game_OpenPageRequested;
        }

        private void Game_OpenPageRequested(object? sender, Type pageType)
        {
            if (Window.GetWindow(this) is MainWindow window)
                window.Navigate(pageType);
        }

        private void LaunchRoblox_Click(object sender, RoutedEventArgs e) => HomeViewModel.LaunchUri("roblox://");

        private void ShowLibrary_Click(object sender, RoutedEventArgs e)
        {
            LibraryTab.IsChecked = true;
            _viewModel.ShowTab(HomeViewModel.TabLibrary);
        }

        void ISearchToolHost.ShowToolFor(SettingsSearchEntry entry)
        {
            if (entry.NestedPageType == typeof(PrivateServersPage))
            {
                PrivateTab.IsChecked = true;
                _viewModel.ShowTab(HomeViewModel.SectionPrivateServers);
            }
            else if (entry.NestedPageType == typeof(HistoryPage))
            {
                HistoryTab.IsChecked = true;
                _viewModel.ShowTab(HomeViewModel.SectionHistory);
            }
        }

        private void ApplyHomeBackground()
        {
            var prop = App.Settings.Prop;

            if (!prop.HomepageBackgroundEnabled || prop.HomepageBackgroundMode == HomepageBackgroundMode.None)
            {
                Background = Brushes.Transparent;
                return;
            }

            try
            {
                Brush brush = HomepageBackgroundRenderer.BuildBrushFromSettings();
                brush.Freeze();
                Background = brush;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("HomePage", $"Could not apply the Home page background: {ex.Message}");
                Background = Brushes.Transparent;
            }
        }
    }
}
