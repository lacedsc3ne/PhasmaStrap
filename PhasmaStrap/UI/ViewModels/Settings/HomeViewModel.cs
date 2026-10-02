using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using PhasmaStrap.Integrations;
using PhasmaStrap.Models;
using PhasmaStrap.Utility;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    /// <summary>A game shown as a tile or a row anywhere in the catalog.</summary>
    public sealed class GameCard : NotifyPropertyChangedViewModel
    {
        public long UniverseId { get; set; }

        /// <summary>The place Play launches. For played games this is the place last played.</summary>
        public long PlaceId { get; init; }

        public string Name { get; init; } = "";

        public string Subtitle { get; init; } = "";

        /// <summary>Tracked play time, for the Library's "Most played" order. 0 for games not played.</summary>
        public double TotalMinutes { get; init; }

        /// <summary>When it was last played (local time), or default for games not played.</summary>
        public DateTime LastPlayed { get; init; }

        private string _iconUrl = "";
        public string IconUrl
        {
            get => _iconUrl;
            set { _iconUrl = value ?? ""; OnPropertyChanged(nameof(IconUrl)); }
        }

        private string _bannerUrl = "";
        public string BannerUrl
        {
            get => _bannerUrl;
            set { _bannerUrl = value ?? ""; OnPropertyChanged(nameof(BannerUrl)); OnPropertyChanged(nameof(HasBanner)); }
        }

        public bool HasBanner => _bannerUrl.Length > 0;

        private string _playingText = "";
        public string PlayingText
        {
            get => _playingText;
            set { _playingText = value ?? ""; OnPropertyChanged(nameof(PlayingText)); OnPropertyChanged(nameof(HasPlaying)); }
        }

        public bool HasPlaying => _playingText.Length > 0;

        public string ProfileName => FlagLayers.ProfileFor(App.FlagProfiles.Prop, PlaceId, UniverseId)?.Name ?? "";

        public bool HasProfile => ProfileName.Length > 0;

        public void RefreshProfile()
        {
            OnPropertyChanged(nameof(ProfileName));
            OnPropertyChanged(nameof(HasProfile));
        }
    }

    public sealed class FriendPlayingRow
    {
        public string Name { get; init; } = "";
        public string Location { get; init; } = "";
        public string? AvatarUrl { get; init; }
        public FriendPresence? Presence { get; init; }
        public bool Joinable => Presence?.Joinable ?? false;
    }

    public sealed class CatalogSortRow : NotifyPropertyChangedViewModel
    {
        public string Name { get; init; } = "";
        public List<GameCard> Games { get; init; } = new();

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
        }
    }

    public class HomeViewModel : NotifyPropertyChangedViewModel
    {
        private const string LOG_IDENT = "HomeViewModel";

        public const string TabHome = "home";
        public const string TabLibrary = "library";
        public const string SectionPrivateServers = "privateservers";
        public const string SectionHistory = "history";
        public const string SectionServerBrowser = "serverbrowser";

        public const string ViewHome = "home";
        public const string ViewSearch = "search";
        public const string ViewGame = "game";
        public const string ViewSection = "section";

        private const int HomeLibraryCount = 14;

        public HomeViewModel()
        {
            LoadLibrary();
            _ = LoadFriendsAsync();
            _ = LoadSortsAsync();

            GamesStore.Changed += GamesStore_Changed;
        }

        private void GamesStore_Changed(object? sender, EventArgs e)
        {
            void Refresh()
            {
                if (_libraryFilter == FilterFavourites)
                    ApplyLibraryView();
                else
                    OnPropertyChanged(nameof(FavouriteCount));
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
                Refresh();
            else
                dispatcher.BeginInvoke(Refresh);
        }

        #region Navigation between views

        private string _tab = TabHome;
        public string Tab
        {
            get => _tab;
            private set { _tab = value; OnPropertyChanged(nameof(Tab)); }
        }

        private string _view = ViewHome;
        public string View
        {
            get => _view;
            private set
            {
                _view = value;
                OnPropertyChanged(nameof(View));
                OnPropertyChanged(nameof(HomeVisibility));
                OnPropertyChanged(nameof(LibraryVisibility));
                OnPropertyChanged(nameof(SearchVisibility));
                OnPropertyChanged(nameof(GameVisibility));
                OnPropertyChanged(nameof(SectionVisibility));
            }
        }

        public Visibility HomeVisibility => _view == ViewHome && _tab == TabHome ? Visibility.Visible : Visibility.Collapsed;
        public Visibility LibraryVisibility => _view == ViewHome && _tab == TabLibrary ? Visibility.Visible : Visibility.Collapsed;
        public Visibility SearchVisibility => _view == ViewSearch ? Visibility.Visible : Visibility.Collapsed;
        public Visibility GameVisibility => _view == ViewGame ? Visibility.Visible : Visibility.Collapsed;
        public Visibility SectionVisibility => _view == ViewSection ? Visibility.Visible : Visibility.Collapsed;

        private Uri? _sectionSource;
        public Uri? SectionSource
        {
            get => _sectionSource;
            private set { _sectionSource = value; OnPropertyChanged(nameof(SectionSource)); }
        }

        public ICommand ShowTabCommand => new RelayCommand<string>(ShowTab);

        public void ShowTab(string? tab)
        {
            if (string.IsNullOrEmpty(tab))
                return;

            _searchCts?.Cancel();
            CloseGame();

            if (tab == SectionPrivateServers || tab == SectionHistory || tab == SectionServerBrowser)
            {
                string page = tab switch
                {
                    SectionPrivateServers => "PrivateServersPage.xaml",
                    SectionServerBrowser => "ServerBrowserPage.xaml",
                    _ => "HistoryPage.xaml",
                };
                SectionSource = new Uri($"/UI/Elements/Settings/Pages/{page}", UriKind.Relative);
                Tab = tab;
                View = ViewSection;
                return;
            }

            Tab = tab;
            View = ViewHome;
        }

        private string _viewBeforeGame = ViewHome;

        private GameViewModel? _game;
        public GameViewModel? Game
        {
            get => _game;
            private set { _game = value; OnPropertyChanged(nameof(Game)); }
        }

        public ICommand OpenGameCommand => new RelayCommand<GameCard>(OpenGame);

        public void OpenGame(GameCard? card)
        {
            if (card is null || (card.UniverseId <= 0 && card.PlaceId <= 0))
                return;

            OpenGame(card.UniverseId, card.PlaceId, card.Name, card.IconUrl);
        }

        public void OpenGame(long universeId, long placeId, string name, string iconUrl)
        {
            Game?.Dispose();

            if (_view != ViewGame)
                _viewBeforeGame = _view;

            var game = new GameViewModel(universeId, placeId, name, iconUrl);
            game.ProfileAssigned += (_, _) => RefreshProfiles();
            Game = game;
            View = ViewGame;
        }

        public ICommand BackCommand => new RelayCommand(GoBack);

        private void GoBack()
        {
            CloseGame();
            View = _viewBeforeGame == ViewGame ? ViewHome : _viewBeforeGame;
        }

        private void CloseGame()
        {
            Game?.Dispose();
            Game = null;
        }

        internal void RefreshProfiles()
        {
            foreach (GameCard card in Library.Concat(HomeLibrary).Concat(LibraryView).Concat(SearchResults).Concat(SortGames))
                card.RefreshProfile();
            Hero?.RefreshProfile();

            if (_libraryFilter == FilterOwnFlags)
                ApplyLibraryView();
        }

        #endregion

        #region Library and hero

        public ObservableCollection<GameCard> Library { get; } = new();

        public ObservableCollection<GameCard> HomeLibrary { get; } = new();

        private GameCard? _hero;
        public GameCard? Hero
        {
            get => _hero;
            private set
            {
                _hero = value;
                OnPropertyChanged(nameof(Hero));
                OnPropertyChanged(nameof(HeroVisibility));
                OnPropertyChanged(nameof(WelcomeVisibility));
            }
        }

        public Visibility HeroVisibility => _hero is null ? Visibility.Collapsed : Visibility.Visible;

        public Visibility WelcomeVisibility => _hero is null ? Visibility.Visible : Visibility.Collapsed;

        private string _heroDetail = "";
        /// <summary>Minutes played in this game over the last 7 days, from the session history on Activity. 0 when none or off.</summary>
        private static double MinutesThisWeek(long universeId, long placeId)
        {
            try
            {
                DateTime since = DateTime.UtcNow.AddDays(-7);

                return SessionStore.Shared.Load().Sessions
                    .SelectMany(s => s.Visits)
                    .Where(v => v.JoinedUtc >= since && (universeId > 0 ? v.UniverseId == universeId : v.PlaceId == placeId))
                    .Sum(v => Math.Max(0, ((v.LeftUtc > v.JoinedUtc ? v.LeftUtc : v.JoinedUtc) - v.JoinedUtc).TotalMinutes));
            }
            catch
            {
                return 0;
            }
        }

        public string HeroDetail
        {
            get => _heroDetail;
            private set { _heroDetail = value; OnPropertyChanged(nameof(HeroDetail)); }
        }

        public string LibraryCountText => Library.Count == 1 ? "1 game" : $"All {Library.Count} games";

        public Visibility LibraryEmptyVisibility => Library.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        public ICommand RefreshCommand => new RelayCommand(() =>
        {
            LoadLibrary();
            _ = LoadFriendsAsync();
            if (Sorts.Count == 0)
                _ = LoadSortsAsync();
        });

        private void LoadLibrary()
        {
            List<PlayTimeEntry> entries = PlayTimeStore.GetAll().OrderByDescending(x => x.LastPlayed).ToList();

            Library.Clear();
            HomeLibrary.Clear();

            foreach (PlayTimeEntry entry in entries)
            {
                var card = new GameCard
                {
                    UniverseId = entry.UniverseId,
                    PlaceId = entry.PlaceId,
                    Name = entry.DisplayName,
                    Subtitle = $"{entry.TotalTimeText} played  ·  {entry.LastPlayedText}",
                    IconUrl = entry.IconUrl,
                    TotalMinutes = entry.TotalMinutes,
                    LastPlayed = entry.LastPlayed,
                };

                Library.Add(card);
                if (HomeLibrary.Count < HomeLibraryCount)
                    HomeLibrary.Add(card);
            }

            PlayTimeEntry? last = entries.FirstOrDefault();
            if (last is null)
            {
                Hero = null;
            }
            else
            {
                Hero = new GameCard
                {
                    UniverseId = last.UniverseId,
                    PlaceId = last.PlaceId,
                    Name = last.Name.Length > 0 ? last.Name : last.DisplayName,
                    IconUrl = last.IconUrl,
                };
                string placeName = PlaceNames.NameOf(last.PlaceId);
                string prefix = placeName.Length > 0 && placeName != last.Name ? placeName + "  ·  " : "";
                double week = MinutesThisWeek(last.UniverseId, last.PlaceId);
                string time = week >= 1 ? SessionStats.Duration(week) + " this week" : last.TotalTimeText + " in total";
                HeroDetail = prefix + "played " + last.LastPlayedText.ToLowerInvariant() + "  ·  " + time;
                _ = FillHeroBannerAsync(Hero);
            }

            OnPropertyChanged(nameof(LibraryCountText));
            OnPropertyChanged(nameof(LibraryEmptyVisibility));

            ApplyLibraryView();

            _ = FillLibraryNamesAsync(entries);
        }

        #region Library filters

        public const string FilterAll = "all";
        public const string FilterRecent = "recent";
        public const string FilterOwnFlags = "flags";
        public const string FilterFavourites = "favourites";

        /// <summary>How far back "Recent" looks.</summary>
        private const int RecentDays = 14;

        /// <summary>The Library tab's tiles: <see cref="Library"/> filtered and sorted, plus favourites never played.</summary>
        public ObservableCollection<GameCard> LibraryView { get; } = new();

        private string _libraryFilter = FilterAll;
        public string LibraryFilter
        {
            get => _libraryFilter;
            private set
            {
                _libraryFilter = value;
                OnPropertyChanged(nameof(LibraryFilter));
                OnPropertyChanged(nameof(IsFilterAll));
                OnPropertyChanged(nameof(IsFilterRecent));
                OnPropertyChanged(nameof(IsFilterOwnFlags));
                OnPropertyChanged(nameof(IsFilterFavourites));
                ApplyLibraryView();
            }
        }

        public bool IsFilterAll { get => _libraryFilter == FilterAll; set { if (value) LibraryFilter = FilterAll; } }
        public bool IsFilterRecent { get => _libraryFilter == FilterRecent; set { if (value) LibraryFilter = FilterRecent; } }
        public bool IsFilterOwnFlags { get => _libraryFilter == FilterOwnFlags; set { if (value) LibraryFilter = FilterOwnFlags; } }
        public bool IsFilterFavourites { get => _libraryFilter == FilterFavourites; set { if (value) LibraryFilter = FilterFavourites; } }

        private bool _mostPlayed;
        /// <summary>Most played first instead of most recent first.</summary>
        public bool MostPlayed
        {
            get => _mostPlayed;
            set { _mostPlayed = value; OnPropertyChanged(nameof(MostPlayed)); ApplyLibraryView(); }
        }

        public int FavouriteCount => GamesStore.Shared.Favourites().Count;

        public string LibraryViewNote => _libraryFilter switch
        {
            FilterRecent => $"Games you played in the last {RecentDays} days",
            FilterOwnFlags => "Games that start with their own FastFlag profile",
            FilterFavourites => "Games you marked with the heart on their page",
            _ => "Every game you have played through PhasmaStrap, kept across restarts",
        };

        public Visibility LibraryViewEmptyVisibility => Library.Count > 0 && LibraryView.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        public string LibraryViewEmptyText => _libraryFilter switch
        {
            FilterRecent => $"Nothing played in the last {RecentDays} days.",
            FilterOwnFlags => "None of your games have their own flags yet. Right click a game and pick Give it its own flags.",
            FilterFavourites => "No favourites yet. Open a game and press the heart to keep it here.",
            _ => "",
        };

        internal void ApplyLibraryView()
        {
            IEnumerable<GameCard> cards = Library;

            if (_libraryFilter == FilterFavourites)
            {
                List<GamesStore.FavouriteGame> favourites = GamesStore.Shared.Favourites();
                var played = Library.Where(card => favourites.Any(f => SameGame(f, card))).ToList();

                // Favourites you have not played through PhasmaStrap still get a tile.
                var unplayed = favourites
                    .Where(f => !played.Any(card => SameGame(f, card)))
                    .Select(f => new GameCard { UniverseId = f.UniverseId, PlaceId = f.PlaceId, Name = f.Name, IconUrl = f.IconUrl, Subtitle = "Not played yet" });

                cards = played.Concat(unplayed);
            }
            else if (_libraryFilter == FilterRecent)
            {
                DateTime since = DateTime.Now.AddDays(-RecentDays);
                cards = cards.Where(card => card.LastPlayed >= since);
            }
            else if (_libraryFilter == FilterOwnFlags)
            {
                cards = cards.Where(card => card.HasProfile);
            }

            cards = _mostPlayed
                ? cards.OrderByDescending(card => card.TotalMinutes).ThenByDescending(card => card.LastPlayed)
                : cards.OrderByDescending(card => card.LastPlayed);

            LibraryView.Clear();
            foreach (GameCard card in cards)
                LibraryView.Add(card);

            OnPropertyChanged(nameof(LibraryViewNote));
            OnPropertyChanged(nameof(LibraryViewEmptyVisibility));
            OnPropertyChanged(nameof(LibraryViewEmptyText));
            OnPropertyChanged(nameof(FavouriteCount));

            _ = FillIconsAsync(LibraryView.Where(card => card.IconUrl.Length == 0).ToList());
        }

        private static bool SameGame(GamesStore.FavouriteGame favourite, GameCard card) =>
            (favourite.UniverseId > 0 && favourite.UniverseId == card.UniverseId) || (favourite.PlaceId > 0 && favourite.PlaceId == card.PlaceId);

        #endregion

        private bool _namesFilled;

        private async Task FillLibraryNamesAsync(List<PlayTimeEntry> entries)
        {
            if (_namesFilled)
                return;

            _namesFilled = true;

            try
            {
                if (await PlaceNames.FillAsync(entries))
                    LoadLibrary();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not fill game names: {ex.Message}");
            }
        }

        private static async Task FillHeroBannerAsync(GameCard card)
        {
            try
            {
                long universe = card.UniverseId;
                if (universe <= 0)
                {
                    universe = await UniversePlaces.GetUniverseIdAsync(card.PlaceId) ?? 0;
                    card.UniverseId = universe;
                }

                if (universe <= 0)
                    return;

                card.BannerUrl = await GameCatalog.GetBannerUrlAsync(universe) ?? "";

                GameDetails? details = await GameCatalog.GetDetailsAsync(universe);
                if (details is not null && details.Playing > 0)
                    card.PlayingText = $"{GameCatalog.Compact(details.Playing)} playing";
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Hero banner failed: {ex.Message}");
            }
        }

        public ICommand PlayCommand => new RelayCommand<GameCard>(card =>
        {
            if (card is null || card.PlaceId <= 0)
                return;

            LaunchUri(RobloxLaunch.DeepLink(PlaceNames.StartPlaceOf(card.PlaceId)));
        });

        public ICommand CopyLinkCommand => new RelayCommand<GameCard>(card =>
        {
            if (card is null || card.PlaceId <= 0)
                return;

            try
            {
                Clipboard.SetText($"https://www.roblox.com/games/{card.PlaceId}");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Copy failed: {ex.Message}");
            }
        });

        public ICommand OpenHeroServersCommand => new RelayCommand(() =>
        {
            if (Hero is null)
                return;

            OpenGame(Hero);
        });

        public bool IsInLibrary(GameCard card) => Library.Contains(card);

        /// <summary>Forgets a played place, along with its play time.</summary>
        public void RemoveFromLibrary(GameCard card)
        {
            if (PlayTimeStore.Remove(card.PlaceId))
                LoadLibrary();
        }

        #endregion

        #region Friends playing

        public ObservableCollection<FriendPlayingRow> FriendsPlaying { get; } = new();

        private string _friendsStatus = "Looking for friends in a game...";
        public string FriendsStatus
        {
            get => _friendsStatus;
            private set { _friendsStatus = value; OnPropertyChanged(nameof(FriendsStatus)); }
        }

        private string _friendsCountText = "";
        /// <summary>"3 of 41 online", next to the panel title. Empty until the friends are loaded.</summary>
        public string FriendsCountText
        {
            get => _friendsCountText;
            private set { _friendsCountText = value; OnPropertyChanged(nameof(FriendsCountText)); }
        }

        /// <summary>Raised when the page should open the Party page.</summary>
        public event EventHandler? OpenPartyRequested;

        private bool _partyBusy;

        public string PartyButtonText => PartyService.InParty ? "Open your party" : "Start a party";

        public ICommand StartPartyCommand => new AsyncRelayCommand(async () =>
        {
            if (_partyBusy)
                return;

            _partyBusy = true;
            try
            {
                // Without an account or with parties off, the Party page explains what to turn on.
                if (!PartyService.InParty && PhasmaAccount.SignedIn && App.Settings.Prop.PartyEnabled)
                    await PartyService.CreateAsync();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Starting a party failed: {ex.Message}");
            }
            finally
            {
                _partyBusy = false;
            }

            OnPropertyChanged(nameof(PartyButtonText));
            OpenPartyRequested?.Invoke(this, EventArgs.Empty);
        });

        public ICommand JoinFriendCommand => new RelayCommand<FriendPlayingRow>(row =>
        {
            if (row?.Presence is null || !row.Presence.Joinable)
                return;

            LaunchUri(FriendsService.GetJoinDeeplink(row.Presence));
        });

        private async Task LoadFriendsAsync()
        {
            try
            {
                RobloxCookie.RobloxAccount? me = await RobloxCookie.GetAccountAsync();
                if (me is null)
                {
                    FriendsPlaying.Clear();
                    FriendsCountText = "";
                    FriendsStatus = "Sign in to Roblox to see which friends are playing.";
                    return;
                }

                List<FriendInfo> friends = await FriendsService.GetFriendsAsync(me.UserId);
                Dictionary<long, FriendPresence> presence = await FriendsService.GetPresenceAsync(friends.Select(f => f.UserId));

                var playing = friends
                    .Where(f => presence.TryGetValue(f.UserId, out FriendPresence? p) && p.Type == FriendPresenceType.InGame)
                    .Take(6)
                    .ToList();

                Dictionary<long, string> avatars = playing.Count == 0
                    ? new Dictionary<long, string>()
                    : await FriendsService.GetAvatarsAsync(playing.Select(f => f.UserId));

                FriendsPlaying.Clear();
                foreach (FriendInfo friend in playing)
                {
                    FriendPresence p = presence[friend.UserId];
                    avatars.TryGetValue(friend.UserId, out string? avatar);

                    FriendsPlaying.Add(new FriendPlayingRow
                    {
                        Name = string.IsNullOrWhiteSpace(friend.DisplayName) ? friend.Username : friend.DisplayName,
                        Location = string.IsNullOrEmpty(p.LastLocation) ? "In a game" : p.LastLocation,
                        AvatarUrl = avatar,
                        Presence = p,
                    });
                }

                int online = presence.Values.Count(p => p.Type != FriendPresenceType.Offline);
                FriendsCountText = friends.Count == 0 ? "" : $"{online} of {friends.Count} online";
                FriendsStatus = FriendsPlaying.Count == 0
                    ? (online == 0 ? "None of your friends are online right now." : "Nobody is in a game right now.")
                    : "";
                OnPropertyChanged(nameof(PartyButtonText));
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Friends playing failed: {ex.Message}");
                FriendsCountText = "";
                FriendsStatus = "Couldn't load your friends right now.";
            }
        }

        #endregion

        #region Catalog front page

        public ObservableCollection<CatalogSortRow> Sorts { get; } = new();

        public ObservableCollection<GameCard> SortGames { get; } = new();

        public Visibility SortsVisibility => Sorts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        public ICommand SelectSortCommand => new RelayCommand<CatalogSortRow>(SelectSort);

        private void SelectSort(CatalogSortRow? row)
        {
            if (row is null)
                return;

            foreach (CatalogSortRow sort in Sorts)
                sort.IsSelected = ReferenceEquals(sort, row);

            SortGames.Clear();
            foreach (GameCard card in row.Games.Take(HomeSortCount))
                SortGames.Add(card);

            OnPropertyChanged(nameof(BrowseVisibility));

            _ = FillIconsAsync(SortGames.ToList());
        }

        /// <summary>Tiles in the catalog row on Home. Browse shows the whole list.</summary>
        private const int HomeSortCount = 12;

        public Visibility BrowseVisibility => Sorts.Any(s => s.IsSelected) ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Shows every game of the picked catalog list, in the search view.</summary>
        public ICommand BrowseCommand => new RelayCommand(() =>
        {
            CatalogSortRow? row = Sorts.FirstOrDefault(s => s.IsSelected);
            if (row is null)
                return;

            _searchCts?.Cancel();
            CloseGame();
            SetLinkTarget(null);

            SearchResults.Clear();
            foreach (GameCard card in row.Games)
                SearchResults.Add(card);

            SearchBusy = false;
            SearchStatus = $"{row.Name}  ·  {row.Games.Count} games";
            View = ViewSearch;

            _ = FillIconsAsync(row.Games);
        });

        private async Task LoadSortsAsync()
        {
            List<CatalogSort> sorts;
            try
            {
                sorts = await GameCatalog.GetSortsAsync();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Front page sorts failed: {ex.Message}");
                return;
            }

            Sorts.Clear();
            foreach (CatalogSort sort in sorts.Take(5))
            {
                Sorts.Add(new CatalogSortRow
                {
                    Name = sort.Name,
                    Games = sort.Games.Select(g => new GameCard
                    {
                        UniverseId = g.UniverseId,
                        PlaceId = g.RootPlaceId,
                        Name = g.Name,
                        PlayingText = g.Playing > 0 ? $"{GameCatalog.Compact(g.Playing)} playing" : "",
                    }).ToList(),
                });
            }

            OnPropertyChanged(nameof(SortsVisibility));

            if (Sorts.Count > 0)
                SelectSort(Sorts[0]);
        }

        private static async Task FillIconsAsync(List<GameCard> cards)
        {
            List<GameCard> missing = cards.Where(c => c.IconUrl.Length == 0 && c.UniverseId > 0).ToList();
            if (missing.Count == 0)
                return;

            try
            {
                List<GameInfo> withIcons = await GameLookup.WithIconsAsync(missing.Select(c => new GameInfo(c.UniverseId, c.PlaceId, c.Name)).ToList());
                foreach (GameInfo info in withIcons)
                {
                    foreach (GameCard card in missing.Where(c => c.UniverseId == info.UniverseId))
                        card.IconUrl = info.IconUrl;
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Icon lookup failed: {ex.Message}");
            }
        }

        #endregion

        #region Search, links and place IDs

        private string _searchText = "";
        public string SearchText
        {
            get => _searchText;
            set { _searchText = value ?? ""; OnPropertyChanged(nameof(SearchText)); }
        }

        private string _searchStatus = "";
        public string SearchStatus
        {
            get => _searchStatus;
            private set { _searchStatus = value; OnPropertyChanged(nameof(SearchStatus)); }
        }

        private bool _searchBusy;
        public bool SearchBusy
        {
            get => _searchBusy;
            private set { _searchBusy = value; OnPropertyChanged(nameof(SearchBusy)); }
        }

        public ObservableCollection<GameCard> SearchResults { get; } = new();

        private RobloxLaunchTarget? _linkTarget;

        public Visibility LinkVisibility => _linkTarget is null ? Visibility.Collapsed : Visibility.Visible;

        private string _linkText = "";
        public string LinkText
        {
            get => _linkText;
            private set { _linkText = value; OnPropertyChanged(nameof(LinkText)); }
        }

        public ICommand SearchCommand => new AsyncRelayCommand(SearchAsync);

        public ICommand ClearSearchCommand => new RelayCommand(() =>
        {
            _searchCts?.Cancel();
            SearchText = "";
            SearchResults.Clear();
            SetLinkTarget(null);
            if (_view == ViewSearch)
                View = ViewHome;
        });

        public ICommand JoinLinkCommand => new RelayCommand(() =>
        {
            if (_linkTarget is null)
                return;

            LaunchUri(_linkTarget.ToDeepLink());
        });

        private CancellationTokenSource? _searchCts;

        private void SetLinkTarget(RobloxLaunchTarget? target)
        {
            _linkTarget = target;
            LinkText = target is null ? "" : $"This looks like {target.Describe().ToLowerInvariant()}.";
            OnPropertyChanged(nameof(LinkVisibility));
        }

        private async Task SearchAsync()
        {
            string text = SearchText.Trim();
            if (text.Length == 0)
                return;

            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            CancellationToken ct = _searchCts.Token;

            CloseGame();
            SetLinkTarget(null);
            SearchResults.Clear();
            View = ViewSearch;
            SearchBusy = true;
            SearchStatus = "Searching...";

            try
            {
                // A plain place ID or game link opens the game page straight away.
                if (RobloxLinkParser.TryParse(text, out RobloxLaunchTarget parsed))
                {
                    if (parsed.Kind == RobloxLinkKind.Place && !parsed.IsPrivateServer && parsed.PlaceId > 0)
                    {
                        SearchStatus = "";
                        OpenGame(0, parsed.PlaceId, $"Place {parsed.PlaceId}", "");
                        _viewBeforeGame = ViewHome;
                        return;
                    }

                    RobloxLaunchTarget? resolved = await RobloxLinkParser.ResolveAsync(text, ct);
                    if (resolved is not null && resolved.Kind != RobloxLinkKind.Unknown)
                    {
                        SetLinkTarget(resolved);
                        SearchStatus = "";
                        return;
                    }
                }

                List<GameInfo> games = await GameLookup.SearchAsync(text, ct);
                if (ct.IsCancellationRequested)
                    return;

                foreach (GameInfo game in games)
                {
                    SearchResults.Add(new GameCard
                    {
                        UniverseId = game.UniverseId,
                        PlaceId = game.RootPlaceId,
                        Name = game.Name,
                        IconUrl = game.IconUrl,
                    });
                }

                SearchStatus = games.Count == 0 ? $"Nothing in the catalog matches \"{text}\"." : $"Results for \"{text}\"";

                List<GameDetails> details = await GameCatalog.GetDetailsAsync(games.Select(g => g.UniverseId), ct);
                foreach (GameDetails detail in details)
                {
                    foreach (GameCard card in SearchResults.Where(c => c.UniverseId == detail.UniverseId))
                        card.PlayingText = detail.Playing > 0 ? $"{GameCatalog.Compact(detail.Playing)} playing" : "";
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Search failed: {ex.Message}");
                SearchStatus = $"Search failed: {ex.Message}";
            }
            finally
            {
                SearchBusy = false;
            }
        }

        #endregion

        #region Flag profiles and accounts for the right click menus

        /// <summary>The profile a whole game starts with, or "" for the usual flags.</summary>
        internal static string ProfileIdFor(long universeId) =>
            universeId <= 0 ? "" : App.FlagProfiles.Prop.Rules.FirstOrDefault(r => r.IsWholeGame && r.UniverseId == universeId)?.ProfileId ?? "";

        /// <summary>Gives a whole game a flag profile, or takes it away when <paramref name="profileId"/> is empty.</summary>
        internal static void AssignProfile(long universeId, long rootPlaceId, string gameName, string iconUrl, string profileId)
        {
            if (universeId <= 0)
                return;

            var data = App.FlagProfiles.Prop;
            FlagGameRule? rule = data.Rules.FirstOrDefault(r => r.IsWholeGame && r.UniverseId == universeId);

            if (string.IsNullOrEmpty(profileId))
            {
                if (rule is not null)
                    data.Rules.Remove(rule);
            }
            else
            {
                if (rule is null)
                {
                    rule = new FlagGameRule();
                    data.Rules.Add(rule);
                }

                rule.UniverseId = universeId;
                rule.RootPlaceId = rootPlaceId;
                rule.PlaceId = 0;
                rule.PlaceName = "";
                rule.GameName = gameName;
                rule.IconUrl = iconUrl;
                rule.ProfileId = profileId;

                if (rootPlaceId > 0)
                    GameLookup.Remember(rootPlaceId, universeId);
            }

            try
            {
                // Saved straight away, like the game page: a launch reads it from disk.
                App.FlagProfiles.Save();
                App.FlagProfiles.NotifyEdited();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Saving the profile assignment failed: {ex.Message}");
            }
        }

        /// <summary>Makes an empty profile named after the game and gives it to the whole game.</summary>
        internal static string CreateOwnProfile(long universeId, long rootPlaceId, string gameName, string iconUrl)
        {
            var profile = new FlagProfile { Name = FlagLayers.UniqueName(App.FlagProfiles.Prop, gameName) };
            App.FlagProfiles.Prop.Profiles.Add(profile);
            AssignProfile(universeId, rootPlaceId, gameName, iconUrl, profile.Id);
            return profile.Id;
        }

        internal static List<AccountQuickSwitch.Account> SavedAccounts() => AccountQuickSwitch.List(Paths.AccountBackups);

        /// <summary>Signs Roblox in as a saved account, then opens <paramref name="uri"/>. Roblox has to be closed, since the login is swapped on disk.</summary>
        internal static async Task LaunchAsAccountAsync(AccountQuickSwitch.Account account, string uri)
        {
            Process[] running = Process.GetProcessesByName(App.RobloxPlayerAppName);
            bool open = running.Length > 0;
            foreach (Process process in running)
                process.Dispose();

            if (open)
            {
                Frontend.ShowMessageBox($"Close Roblox first, then try again to play as {account.Title}.", MessageBoxImage.Warning);
                return;
            }

            try
            {
                await AccountQuickSwitch.SwitchAsync(Paths.AccountBackups, RobloxCookie.LiveCookiesDatPath, account.UserId);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Account switch failed: {ex.Message}");
                Frontend.ShowMessageBox($"Couldn't switch to {account.Title}: {ex.Message}", MessageBoxImage.Warning);
                return;
            }

            LaunchUri(uri);
        }

        #endregion

        internal static void LaunchUri(string uri)
        {
            if (string.IsNullOrWhiteSpace(uri))
                return;

            try
            {
                Process.Start(Paths.Process, $"-player \"{uri}\"");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Launch failed: {ex.Message}");
            }
        }
    }
}
