using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using PhasmaStrap.Integrations;
using PhasmaStrap.Models;
using PhasmaStrap.Utility;
using PhasmaStrap.Utility.Backend;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public sealed class PlaceItem : NotifyPropertyChangedViewModel
    {
        public long PlaceId { get; init; }
        public string Name { get; init; } = "";
        public bool IsRoot { get; init; }

        private string _iconUrl = "";
        public string IconUrl
        {
            get => _iconUrl;
            set { _iconUrl = value ?? ""; OnPropertyChanged(nameof(IconUrl)); }
        }

        public string Caption => StatsText.Length > 0 ? StatsText : IsRoot ? "Start place" : $"Place {PlaceId}";

        public string IdText => $"Place ID {PlaceId}";

        /// <summary>Players and servers, then the place ID, for the Places tab.</summary>
        public string DetailText => StatsText.Length > 0 ? $"{StatsText}  ·  {IdText}" : IdText;

        private string _statsText = "";
        /// <summary>"214K playing · 18.1K servers" once known, from PhasmaStrap's server or this place's server list.</summary>
        public string StatsText
        {
            get => _statsText;
            private set { _statsText = value; OnPropertyChanged(nameof(StatsText)); OnPropertyChanged(nameof(Caption)); OnPropertyChanged(nameof(DetailText)); }
        }

        /// <summary>True once the totals came from the server, so a partial count from one list doesn't replace them.</summary>
        public bool HasFullStats { get; private set; }

        public void SetStats(long playing, long servers, bool full, bool atLeast = false)
        {
            if (HasFullStats && !full)
                return;

            HasFullStats |= full;
            string more = atLeast ? "+" : "";
            StatsText = servers > 0
                ? $"{GameCatalog.Compact(playing)}{more} playing  ·  {GameCatalog.Compact(servers)}{more} server{(servers == 1 ? "" : "s")}"
                : "";
        }
    }

    /// <summary>A friend shown on a server row: a round initial or avatar.</summary>
    public sealed class ServerFriend
    {
        public long UserId { get; init; }
        public string Name { get; init; } = "";
        public string? AvatarUrl { get; init; }
        public string Initial => Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?";
    }

    /// <summary>A friend in the "Friends in this game" card on the Overview tab.</summary>
    public sealed class GameFriendRow
    {
        public long UserId { get; init; }
        public string Name { get; init; } = "";
        public string Detail { get; init; } = "";
        public string? AvatarUrl { get; init; }
        public string Initial => Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?";
        public FriendPresence? Presence { get; init; }
        public bool CanJoin => Presence?.Joinable ?? false;
        public Visibility JoinVisibility => CanJoin ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>A region chip above the server list.</summary>
    public sealed class RegionChip : NotifyPropertyChangedViewModel
    {
        public string Code { get; init; } = "";
        public string Name { get; init; } = "";
        public int Count { get; init; }
        public string Label => Count > 0 ? $"{Name}  {Count}" : Name;

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
        }
    }

    public sealed class GameAccountOption
    {
        /// <summary>0 means whoever is signed in to Roblox at launch.</summary>
        public long UserId { get; init; }
        public string Name { get; init; } = "";
        public string Initial => Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?";
        public override string ToString() => Name;
    }

    public sealed class ServerRow : NotifyPropertyChangedViewModel
    {
        public ServerListItem Server { get; init; } = new();

        private string _region = "";
        /// <summary>"Frankfurt, DE" when known.</summary>
        public string Region
        {
            get => _region;
            set
            {
                _region = value ?? "";
                OnPropertyChanged(nameof(Region));
                OnPropertyChanged(nameof(City));
                OnPropertyChanged(nameof(Badge));
                OnPropertyChanged(nameof(Continent));
                OnPropertyChanged(nameof(HasRegion));
            }
        }

        private string _continentHint = "";
        public string ContinentHint { get => _continentHint; set { _continentHint = value ?? ""; OnPropertyChanged(nameof(Continent)); OnPropertyChanged(nameof(Badge)); } }

        public string City => ServerFacts.SplitRegion(_region).City;
        public string Country => ServerFacts.SplitRegion(_region).Country;
        public string Continent => ServerFacts.ContinentOf(Country, _continentHint);
        public string Badge => ServerFacts.Badge(Country, Continent);
        public bool HasRegion => City.Length > 0;

        private DateTime? _firstSeenUtc;
        public DateTime? FirstSeenUtc
        {
            get => _firstSeenUtc;
            set { _firstSeenUtc = value; OnPropertyChanged(nameof(FirstSeenUtc)); OnPropertyChanged(nameof(UptimeText)); }
        }

        public string UptimeText => ServerFacts.Uptime(_firstSeenUtc);

        private double _fps;
        /// <summary>The server's frame rate: Roblox's own number when it lists one, otherwise PhasmaStrap's server.</summary>
        public double Fps
        {
            get => Server.Fps > 0 ? Server.Fps : _fps;
            set { _fps = value; OnPropertyChanged(nameof(Fps)); OnPropertyChanged(nameof(FpsText)); }
        }

        public string FpsText => Fps > 0 ? Math.Round(Fps).ToString() : "";

        /// <summary>Average FPS of PhasmaStrap players in this server, for the information window.</summary>
        public double ClientFps { get; set; }

        private List<ServerFriend> _friends = new();
        public List<ServerFriend> Friends
        {
            get => _friends;
            set { _friends = value ?? new(); OnPropertyChanged(nameof(Friends)); OnPropertyChanged(nameof(HasFriends)); OnPropertyChanged(nameof(FriendsTip)); }
        }

        public bool HasFriends => _friends.Count > 0;

        public string FriendsTip => string.Join(", ", _friends.Select(f => f.Name));

        public string JoinText => IsFull ? "Full" : "Join";

        public int Playing => Server.Playing;
        public int MaxPlayers => Server.MaxPlayers;
        public string PlayersText => Server.PlayersText;
        public string PingText => Server.Ping > 0 ? $"{Server.Ping} ms" : "No ping";
        public double Fill => Server.MaxPlayers > 0 ? Math.Clamp(Server.Playing / (double)Server.MaxPlayers, 0, 1) : 0;
        public double FillWidth => Math.Round(Fill * 120);
        public bool IsFull => Server.MaxPlayers > 0 && Server.Playing >= Server.MaxPlayers;
        public bool CanJoin => !IsFull;
        public string ShortJobId => Server.JobId.Length > 8 ? Server.JobId[..8] : Server.JobId;

        /// <summary>good, ok, bad or none, picked up by the row's colour triggers.</summary>
        public string PingLevel => Server.Ping <= 0 ? "none" : Server.Ping < 60 ? "good" : Server.Ping < 130 ? "ok" : "bad";
    }

    public sealed class GamePrivateServerRow : NotifyPropertyChangedViewModel
    {
        public PrivateServerInfo Info { get; init; } = new();

        private bool _isFriend;
        /// <summary>The owner is one of your Roblox friends.</summary>
        public bool IsFriend
        {
            get => _isFriend;
            set { _isFriend = value; OnPropertyChanged(nameof(IsFriend)); OnPropertyChanged(nameof(FriendVisibility)); }
        }

        public Visibility FriendVisibility => _isFriend && !Info.Owned ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Short line for the places rail: whose it is.</summary>
        public string RailText => Info.Owned ? "Yours" : string.IsNullOrEmpty(Info.OwnerName) ? "Shared with you" : $"Shared by {Info.OwnerName}";
        public string Title => string.IsNullOrWhiteSpace(Info.Name) ? "Private server" : Info.Name;

        public string Subtitle
        {
            get
            {
                string who = Info.Owned ? "Yours" : string.IsNullOrEmpty(Info.OwnerName) ? "Shared with you" : $"{Info.OwnerName}'s server";
                if (!Info.Active)
                    return who + "  ·  inactive";
                if (Info.Expires is DateTime expires)
                {
                    int days = (int)Math.Ceiling((expires - DateTime.Now).TotalDays);
                    if (days >= 0 && days <= 60)
                        return who + $"  ·  ends in {days} day{(days == 1 ? "" : "s")}";
                }
                return who;
            }
        }

        public bool IsActive => Info.Active;

        public string StateText => Info.Active ? "Can join now" : "Inactive";
    }

    /// <summary>One visit to this game, read from the session history on Activity.</summary>
    public sealed class GameSessionRow
    {
        public long PlaceId { get; init; }
        public string JobId { get; init; } = "";
        public bool CanRejoin { get; init; }
        public string WhenText { get; init; } = "";
        public string PlaceText { get; init; } = "";
        public string ServerText { get; init; } = "";
        public string TimeText { get; init; } = "";
        public string FpsText { get; init; } = "";
    }

    /// <summary>One day in the "last 14 days" chart on a game's History tab.</summary>
    public sealed class GameDayBar
    {
        public double Height { get; init; }
        public string Tip { get; init; } = "";
    }

    public sealed class GameProfileOption
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public override string ToString() => Name;
    }

    /// <summary>One game's page: details, places, public servers, private servers and what it launches with.</summary>
    public sealed class GameViewModel : NotifyPropertyChangedViewModel, IDisposable
    {
        private const string LOG_IDENT = "GameViewModel";

        public const string TabOverview = "overview";
        public const string TabPlaces = "places";
        public const string TabServers = "servers";
        public const string TabAbout = "about";
        public const string TabPrivate = "private";
        public const string TabHistory = "history";

        private readonly CancellationTokenSource _cts = new();
        private readonly long _requestedPlaceId;

        public event EventHandler? ProfileAssigned;

        /// <summary>Raised when the page wants another settings page (overlay, resolution, flag editor) opened.</summary>
        public event EventHandler<Type>? OpenPageRequested;

        public GameViewModel(long universeId, long placeId, string name, string iconUrl)
        {
            UniverseId = universeId;
            _requestedPlaceId = placeId;
            _name = name;
            _iconUrl = iconUrl ?? "";

            RebuildProfileOptions();
            _ = LoadAsync();
            _ = LoadAccountsAsync();
        }

        public void Dispose()
        {
            try
            {
                _cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        #region Details

        public long UniverseId { get; private set; }

        public long RootPlaceId { get; private set; }

        private string _name;
        public string Name { get => _name; private set { _name = value; OnPropertyChanged(nameof(Name)); OnPropertyChanged(nameof(PlacesHeader)); } }

        private string _creator = "";
        public string Creator { get => _creator; private set { _creator = value; OnPropertyChanged(nameof(Creator)); } }

        private string _description = "";
        public string Description { get => _description; private set { _description = value; OnPropertyChanged(nameof(Description)); } }

        private string _iconUrl;
        public string IconUrl { get => _iconUrl; private set { _iconUrl = value ?? ""; OnPropertyChanged(nameof(IconUrl)); } }

        private string _bannerUrl = "";
        public string BannerUrl { get => _bannerUrl; private set { _bannerUrl = value ?? ""; OnPropertyChanged(nameof(BannerUrl)); } }

        private string _playingText = "";
        public string PlayingText { get => _playingText; private set { _playingText = value; OnPropertyChanged(nameof(PlayingText)); } }

        private string _statsText = "";
        public string StatsText { get => _statsText; private set { _statsText = value; OnPropertyChanged(nameof(StatsText)); } }

        private string _aboutText = "";
        public string AboutText { get => _aboutText; private set { _aboutText = value; OnPropertyChanged(nameof(AboutText)); } }

        private string _status = "Loading...";
        public string Status { get => _status; private set { _status = value; OnPropertyChanged(nameof(Status)); } }

        private string _tab = TabServers;
        public string Tab
        {
            get => _tab;
            private set
            {
                _tab = value;
                OnPropertyChanged(nameof(Tab));
                OnPropertyChanged(nameof(ServersTabVisibility));
                OnPropertyChanged(nameof(AboutTabVisibility));
                OnPropertyChanged(nameof(PrivateTabVisibility));
                OnPropertyChanged(nameof(OverviewTabVisibility));
                OnPropertyChanged(nameof(PlacesTabVisibility));
                OnPropertyChanged(nameof(HistoryTabVisibility));
            }
        }

        public bool IsOverviewTab { get => _tab == TabOverview; set { if (value) ShowTab(TabOverview); } }
        public bool IsPlacesTab { get => _tab == TabPlaces; set { if (value) ShowTab(TabPlaces); } }
        public bool IsHistoryTab { get => _tab == TabHistory; set { if (value) ShowTab(TabHistory); } }
        public bool IsServersTab { get => _tab == TabServers; set { if (value) ShowTab(TabServers); } }
        public bool IsAboutTab { get => _tab == TabAbout; set { if (value) ShowTab(TabAbout); } }
        public bool IsPrivateTab { get => _tab == TabPrivate; set { if (value) ShowTab(TabPrivate); } }

        private void ShowTab(string tab)
        {
            if (_tab != tab)
                Tab = tab;

            OnPropertyChanged(nameof(IsServersTab));
            OnPropertyChanged(nameof(IsAboutTab));
            OnPropertyChanged(nameof(IsPrivateTab));
            OnPropertyChanged(nameof(IsOverviewTab));
            OnPropertyChanged(nameof(IsPlacesTab));
            OnPropertyChanged(nameof(IsHistoryTab));

            if (tab == TabPrivate && !_privateLoaded)
                _ = LoadPrivateServersAsync();

            if (tab == TabHistory && !_historyLoaded)
                LoadHistory();
        }

        public Visibility ServersTabVisibility => _tab == TabServers ? Visibility.Visible : Visibility.Collapsed;
        public Visibility AboutTabVisibility => _tab == TabAbout ? Visibility.Visible : Visibility.Collapsed;
        public Visibility PrivateTabVisibility => _tab == TabPrivate ? Visibility.Visible : Visibility.Collapsed;
        public Visibility OverviewTabVisibility => _tab == TabOverview ? Visibility.Visible : Visibility.Collapsed;
        public Visibility PlacesTabVisibility => _tab == TabPlaces ? Visibility.Visible : Visibility.Collapsed;
        public Visibility HistoryTabVisibility => _tab == TabHistory ? Visibility.Visible : Visibility.Collapsed;

        public ICommand ShowTabCommand => new RelayCommand<string>(tab =>
        {
            if (!string.IsNullOrEmpty(tab))
                ShowTab(tab);
        });

        private async Task LoadAsync()
        {
            CancellationToken ct = _cts.Token;

            try
            {
                if (UniverseId <= 0 && _requestedPlaceId > 0)
                    UniverseId = await UniversePlaces.GetUniverseIdAsync(_requestedPlaceId, ct) ?? 0;

                if (UniverseId <= 0)
                {
                    Status = "Couldn't find this game. The place may be private or the ID may be wrong.";
                    Places.Add(new PlaceItem { PlaceId = _requestedPlaceId, Name = Name, IsRoot = true });
                    OnPropertyChanged(nameof(PlacesTitle));
                    SelectPlace(Places[0]);
                    RefreshPlaytime();
                    return;
                }

                GameDetails? details = await GameCatalog.GetDetailsAsync(UniverseId, ct);
                if (details is not null)
                {
                    RootPlaceId = details.RootPlaceId;
                    Name = details.Name.Length > 0 ? details.Name : Name;
                    Creator = details.Creator;
                    Description = details.Description;
                    PlayingText = $"{GameCatalog.Compact(details.Playing)} playing";

                    var parts = new List<string> { $"{GameCatalog.Compact(details.Visits)} visits", $"{GameCatalog.Compact(details.Favorites)} favourites" };
                    if (details.MaxPlayers > 0)
                        parts.Add($"{details.MaxPlayers} per server");
                    if (details.Updated is DateTime updated)
                        parts.Add($"updated {updated.ToLocalTime():d MMM yyyy}");
                    StatsText = string.Join("  ·  ", parts);

                    AboutText = string.IsNullOrWhiteSpace(details.Description) ? "This game has no description." : details.Description.Trim();
                }

                if (RootPlaceId > 0)
                    GameLookup.Remember(RootPlaceId, UniverseId);
                if (_requestedPlaceId > 0)
                    GameLookup.Remember(_requestedPlaceId, UniverseId);

                if (IconUrl.Length == 0)
                {
                    List<GameInfo> icons = await GameLookup.WithIconsAsync(new List<GameInfo> { new(UniverseId, RootPlaceId, Name) }, ct);
                    IconUrl = icons.FirstOrDefault()?.IconUrl ?? "";
                }

                int? liked = await GameCatalog.GetLikedPercentAsync(UniverseId, ct);
                if (liked is int percent)
                    StatsText = $"{percent}% liked  ·  " + StatsText;

                BannerUrl = await GameCatalog.GetBannerUrlAsync(UniverseId, ct) ?? "";

                await LoadPlacesAsync(ct);
                RefreshPlaytime();
                Status = "";

                OnPropertyChanged(nameof(IsFavourite));
                OnPropertyChanged(nameof(FavouriteTip));
                OnPropertyChanged(nameof(NotFavouriteVisibility));
                _ = LoadPlaceStatsAsync();
                _ = LoadFriendsAsync();

                // Loaded straight away so the tab count and the places rail can show them.
                if (!_privateLoaded)
                    _ = LoadPrivateServersAsync();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Loading the game failed: {ex.Message}");
                Status = $"Couldn't load everything for this game: {ex.Message}";
            }
        }

        #endregion

        #region Favourite

        private long FavouritePlaceId => RootPlaceId > 0 ? RootPlaceId : _requestedPlaceId;

        public bool IsFavourite => GamesStore.Shared.IsFavourite(UniverseId, FavouritePlaceId);

        public string FavouriteTip => IsFavourite ? "Remove from your favourites" : "Add to your favourites";

        public Visibility NotFavouriteVisibility => IsFavourite ? Visibility.Collapsed : Visibility.Visible;

        public ICommand ToggleFavouriteCommand => new RelayCommand(() =>
        {
            if (UniverseId <= 0 && FavouritePlaceId <= 0)
                return;

            GamesStore.Shared.SetFavourite(UniverseId, FavouritePlaceId, Name, IconUrl, !IsFavourite);
            OnPropertyChanged(nameof(IsFavourite));
            OnPropertyChanged(nameof(FavouriteTip));
            OnPropertyChanged(nameof(NotFavouriteVisibility));
        });

        #endregion

        #region Places

        public ObservableCollection<PlaceItem> Places { get; } = new();

        public string PlacesTitle => Places.Count == 1 ? "1 place" : $"{Places.Count} places";

        public Visibility PlaceSearchVisibility => Places.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

        private string _placeSearch = "";
        public string PlaceSearch
        {
            get => _placeSearch;
            set
            {
                _placeSearch = value ?? "";
                OnPropertyChanged(nameof(PlaceSearch));

                System.Windows.Data.CollectionViewSource.GetDefaultView(Places).Filter =
                    item => item is PlaceItem place && Matches(_placeSearch, place.Name, place.PlaceId.ToString(), place.Caption);
            }
        }

        private static bool Matches(string search, params string[] fields)
        {
            string wanted = search.Trim();

            return wanted.Length == 0 || fields.Any(field => field.Contains(wanted, StringComparison.OrdinalIgnoreCase));
        }

        private PlaceItem? _selectedPlace;
        public PlaceItem? SelectedPlace
        {
            get => _selectedPlace;
            set
            {
                if (value is null || ReferenceEquals(value, _selectedPlace))
                    return;

                SelectPlace(value);
            }
        }

        public string PlayText => _selectedPlace is null ? "Play" : Places.Count > 1 ? $"Play {_selectedPlace.Name}" : "Play";

        public string PlaceHint => _selectedPlace is not null && !_selectedPlace.IsRoot
            ? "Some games only let you in through the start place. If so you will land there instead."
            : "";

        private async Task LoadPlacesAsync(CancellationToken ct)
        {
            List<UniversePlace> places = await UniversePlaces.GetPlacesAsync(UniverseId, RootPlaceId > 0 ? RootPlaceId : _requestedPlaceId, ct);

            Places.Clear();
            foreach (UniversePlace place in places)
                Places.Add(new PlaceItem { PlaceId = place.PlaceId, Name = place.Name, IsRoot = place.IsRootPlace });

            long root = RootPlaceId > 0 ? RootPlaceId : _requestedPlaceId;
            if (Places.Count == 0 && root > 0)
                Places.Add(new PlaceItem { PlaceId = root, Name = Name, IsRoot = true });

            if (_requestedPlaceId > 0 && Places.All(p => p.PlaceId != _requestedPlaceId))
                Places.Add(new PlaceItem { PlaceId = _requestedPlaceId, Name = PlaceNames.NameOf(_requestedPlaceId) is { Length: > 0 } known ? known : $"Place {_requestedPlaceId}" });

            OnPropertyChanged(nameof(PlacesTitle));
            OnPropertyChanged(nameof(PlaceSearchVisibility));
            OnPropertyChanged(nameof(PlacesHeader));

            PlaceItem? initial = Places.FirstOrDefault(p => p.PlaceId == _requestedPlaceId) ?? Places.FirstOrDefault(p => p.IsRoot) ?? Places.FirstOrDefault();
            if (initial is not null)
                SelectPlace(initial);

            Dictionary<long, string> icons = await GameCatalog.GetPlaceIconsAsync(Places.Select(p => p.PlaceId), ct);
            foreach (PlaceItem place in Places)
            {
                if (icons.TryGetValue(place.PlaceId, out string? icon))
                    place.IconUrl = icon;
                else if (place.IsRoot)
                    place.IconUrl = IconUrl;
            }
        }

        public ICommand SelectPlaceCommand => new RelayCommand<PlaceItem>(place =>
        {
            if (place is not null)
                SelectPlace(place);
        });

        public string PlacesHeader => $"Places in {Name}";

        /// <summary>Players and servers per place from PhasmaStrap's server, when it has them.</summary>
        private async Task LoadPlaceStatsAsync()
        {
            try
            {
                Dictionary<long, PlaceStats> stats = await GamesApi.GetPlaceStatsAsync(Places.Select(p => p.PlaceId));
                if (_cts.IsCancellationRequested || stats.Count == 0)
                    return;

                foreach (PlaceItem place in Places)
                {
                    if (stats.TryGetValue(place.PlaceId, out PlaceStats? stat))
                        place.SetStats(stat.Playing, stat.Servers, full: true);
                }

                long total = stats.Values.Sum(p => p.Servers);
                if (total > 0)
                {
                    _totalServers = total;
                    OnPropertyChanged(nameof(ServerCountText));
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Place stats failed: {ex.Message}");
            }
        }

        /// <summary>Places tab: picks the place and shows its public servers.</summary>
        public ICommand ShowPlaceServersCommand => new RelayCommand<PlaceItem>(place =>
        {
            if (place is null)
                return;

            SelectedPlace = place;
            ShowTab(TabServers);
        });

        /// <summary>Places tab: picks the place and switches the launch setup to that place only.</summary>
        public ICommand PlaceFlagsCommand => new RelayCommand<PlaceItem>(place =>
        {
            if (place is null)
                return;

            SelectedPlace = place;
            OnlyThisPlace = true;
            ShowTab(TabOverview);
        });

        /// <summary>Places tab: picks the place and launches it.</summary>
        /// <summary>The place Play launches: the picked place, else the start place.</summary>
        public long LaunchPlaceId => _selectedPlace?.PlaceId ?? (RootPlaceId > 0 ? RootPlaceId : _requestedPlaceId);

        public ICommand PlayPlaceCommand => new RelayCommand<PlaceItem>(place =>
        {
            if (place is null)
                return;

            SelectedPlace = place;
            Play();
        });

        private void SelectPlace(PlaceItem place)
        {
            _selectedPlace = place;
            OnPropertyChanged(nameof(SelectedPlace));
            OnPropertyChanged(nameof(PlayText));
            OnPropertyChanged(nameof(PlaceHint));
            OnPropertyChanged(nameof(OverlaySummary));
            OnPropertyChanged(nameof(ResolutionSummary));
            OnPropertyChanged(nameof(ScopePlaceText));
            OnPropertyChanged(nameof(SelectedPlaceIdText));
            OnPropertyChanged(nameof(SelectedPlaceCaption));

            _wholeGame = FlagLayers.RuleFor(App.FlagProfiles.Prop, place.PlaceId, 0) is null;
            OnPropertyChanged(nameof(WholeGame));
            OnPropertyChanged(nameof(OnlyThisPlace));
            SyncProfileSelection();

            _ = LoadServersAsync();
        }

        #endregion

        #region Public servers

        public ObservableCollection<ServerRow> Servers { get; } = new();

        private List<ServerListItem> _allServers = new();

        private string _serverStatus = "";
        public string ServerStatus { get => _serverStatus; private set { _serverStatus = value; OnPropertyChanged(nameof(ServerStatus)); } }

        private bool _serversBusy;
        public bool ServersBusy
        {
            get => _serversBusy;
            private set { _serversBusy = value; OnPropertyChanged(nameof(ServersBusy)); OnPropertyChanged(nameof(ServersNotBusy)); }
        }

        public bool ServersNotBusy => !_serversBusy;

        private bool _hideFull = true;
        public bool HideFull
        {
            get => _hideFull;
            set { _hideFull = value; OnPropertyChanged(nameof(HideFull)); ApplyServerView(); }
        }

        private string _sortMode = "ping";
        public string SortMode
        {
            get => _sortMode;
            private set
            {
                _sortMode = value;
                OnPropertyChanged(nameof(SortMode));
                OnPropertyChanged(nameof(SortByPing));
                OnPropertyChanged(nameof(SortByPlayers));
                OnPropertyChanged(nameof(SortBySpace));
            }
        }

        public bool SortByPing { get => _sortMode == "ping"; set { if (value) { SortMode = "ping"; ApplyServerView(); } } }
        public bool SortByPlayers { get => _sortMode == "players"; set { if (value) { SortMode = "players"; ApplyServerView(); } } }
        public bool SortBySpace { get => _sortMode == "space"; set { if (value) { SortMode = "space"; ApplyServerView(); } } }

        /// <summary>All public servers of the game, when PhasmaStrap's server knows it.</summary>
        private long _totalServers;

        public string ServerCountText => _totalServers > 0 ? GameCatalog.Compact(_totalServers) : _allServers.Count > 0 ? GameCatalog.Compact(_allServers.Count) : "";

        /// <summary>Region, uptime and FPS per job id, filled in after the list arrives.</summary>
        private Dictionary<string, ServerStats> _facts = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Friends in each server, from their Roblox presence.</summary>
        private Dictionary<string, List<ServerFriend>> _friendsByJob = new(StringComparer.OrdinalIgnoreCase);

        public ObservableCollection<RegionChip> RegionChips { get; } = new();

        public Visibility RegionChipsVisibility => RegionChips.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

        private string _regionFilter = "";

        public ICommand SelectRegionCommand => new RelayCommand<RegionChip>(chip =>
        {
            if (chip is null)
                return;

            _regionFilter = chip.Code;
            foreach (RegionChip other in RegionChips)
                other.IsSelected = other.Code == _regionFilter;

            ApplyServerView();
        });

        private bool _pingUnder80;
        public bool PingUnder80
        {
            get => _pingUnder80;
            set { _pingUnder80 = value; OnPropertyChanged(nameof(PingUnder80)); ApplyServerView(); }
        }

        private bool _friendsInside;
        public bool FriendsInside
        {
            get => _friendsInside;
            set { _friendsInside = value; OnPropertyChanged(nameof(FriendsInside)); ApplyServerView(); }
        }

        public Visibility FriendsInsideVisibility => _friendsByJob.Count > 0 || _friendsInside ? Visibility.Visible : Visibility.Collapsed;

        private void RebuildRegionChips()
        {
            var counts = _allServers
                .Select(s => _facts.TryGetValue(s.JobId, out ServerStats? f) ? ServerFacts.ContinentOf(f.Country, f.Continent) : "")
                .Where(c => c.Length > 0)
                .GroupBy(c => c)
                .Select(g => (Code: g.Key, Count: g.Count()))
                .OrderByDescending(g => g.Count)
                .ToList();

            if (_regionFilter.Length > 0 && counts.All(c => c.Code != _regionFilter))
                _regionFilter = "";

            RegionChips.Clear();
            RegionChips.Add(new RegionChip { Code = "", Name = "All regions", IsSelected = _regionFilter.Length == 0 });
            foreach (var (code, count) in counts)
            {
                string name = ServerFacts.ContinentName(code);
                RegionChips.Add(new RegionChip { Code = code, Name = name.Length > 0 ? name : code, Count = count, IsSelected = code == _regionFilter });
            }

            OnPropertyChanged(nameof(RegionChipsVisibility));
        }

        private async Task FillServerFactsAsync(long placeId, int loadId)
        {
            try
            {
                Dictionary<string, ServerStats> facts = await GamesApi.FactsAsync(placeId, _allServers, _cts.Token);
                if (loadId != _serverLoadId || facts.Count == 0)
                    return;

                foreach ((string jobId, ServerStats stats) in facts)
                    _facts[jobId] = stats;

                foreach (ServerRow row in Servers)
                    ApplyFacts(row);

                RebuildRegionChips();
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Server facts failed: {ex.Message}");
            }
        }

        private void ApplyFacts(ServerRow row)
        {
            if (_facts.TryGetValue(row.Server.JobId, out ServerStats? stats))
            {
                row.ContinentHint = stats.Continent;
                row.Region = stats.Region;
                row.FirstSeenUtc = stats.FirstSeenUtc;
                if (stats.ServerFps is double fps && fps > 0)
                    row.Fps = fps;
                row.ClientFps = stats.ClientFps ?? 0;
            }

            row.Friends = _friendsByJob.TryGetValue(row.Server.JobId, out List<ServerFriend>? friends) ? friends : new List<ServerFriend>();
        }

        private string ContinentOfServer(ServerListItem server) =>
            _facts.TryGetValue(server.JobId, out ServerStats? f) ? ServerFacts.ContinentOf(f.Country, f.Continent) : "";

        public ICommand RefreshServersCommand => new AsyncRelayCommand(LoadServersAsync);

        private int _serverLoadId;

        private async Task LoadServersAsync()
        {
            PlaceItem? place = _selectedPlace;
            if (place is null || place.PlaceId <= 0)
                return;

            int loadId = ++_serverLoadId;
            ServersBusy = true;
            Servers.Clear();
            ServerStatus = "Looking for servers...";

            try
            {
                List<ServerListItem> servers = await ServerBrowser.ListPublicServersAsync(place.PlaceId, _cts.Token);
                if (loadId != _serverLoadId)
                    return;

                _allServers = servers;

                // Until the server has totals, the list itself gives a lower bound for this place.
                bool capped = servers.Count >= 300;
                place.SetStats(servers.Sum(s => (long)s.Playing), servers.Count, full: false, atLeast: capped);

                RebuildRegionChips();
                ApplyServerView();
                _ = FillServerFactsAsync(place.PlaceId, loadId);
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Server lookup failed: {ex.Message}");
                ServerStatus = $"Server lookup failed: {ex.Message}";
            }
            finally
            {
                if (loadId == _serverLoadId)
                    ServersBusy = false;
            }
        }

        private void ApplyServerView()
        {
            IEnumerable<ServerListItem> view = _allServers;

            if (_hideFull)
                view = view.Where(s => s.MaxPlayers <= 0 || s.Playing < s.MaxPlayers);

            if (_pingUnder80)
                view = view.Where(s => s.Ping > 0 && s.Ping < 80);

            if (_regionFilter.Length > 0)
                view = view.Where(s => ContinentOfServer(s) == _regionFilter);

            if (_friendsInside)
                view = view.Where(s => _friendsByJob.ContainsKey(s.JobId));

            view = _sortMode switch
            {
                "players" => view.OrderByDescending(s => s.Playing),
                "space" => view.OrderByDescending(s => s.MaxPlayers - s.Playing).ThenBy(s => s.Ping <= 0 ? int.MaxValue : s.Ping),
                _ => view.OrderBy(s => s.Ping <= 0 ? int.MaxValue : s.Ping),
            };

            Servers.Clear();
            foreach (ServerListItem server in view.Take(150))
            {
                var row = new ServerRow { Server = server };
                ApplyFacts(row);
                Servers.Add(row);
            }

            OnPropertyChanged(nameof(ServerCountText));

            int hidden = _allServers.Count - Servers.Count;
            ServerStatus = _allServers.Count == 0
                ? "No public servers are running for this place right now."
                : $"{_allServers.Count} servers running" + (hidden > 0 ? $", {hidden} hidden" : "");
        }

        public ICommand JoinServerCommand => new RelayCommand<ServerRow>(row =>
        {
            if (row is null || _selectedPlace is null)
                return;

            Launch(RobloxLaunch.DeepLink(_selectedPlace.PlaceId, row.Server.JobId));
        });

        public ICommand JoinBestCommand => new RelayCommand(() =>
        {
            if (_selectedPlace is null)
                return;

            ServerListItem? best = _allServers
                .Where(s => s.MaxPlayers <= 0 || s.Playing < s.MaxPlayers)
                .OrderBy(s => s.Ping <= 0 ? int.MaxValue : s.Ping)
                .ThenByDescending(s => s.Playing)
                .FirstOrDefault();

            if (best is null)
                Play();
            else
                Launch(RobloxLaunch.DeepLink(_selectedPlace.PlaceId, best.JobId));
        });

        public ICommand PlayCommand => new RelayCommand(Play);

        private void Play()
        {
            long placeId = LaunchPlaceId;
            if (placeId > 0)
                Launch(RobloxLaunch.DeepLink(placeId));
        }

        /// <summary>Opens a Roblox link as the account picked in the launch setup, or as whoever is signed in.</summary>
        public void Launch(string uri)
        {
            long userId = _selectedAccount?.UserId ?? 0;

            AccountQuickSwitch.Account? account = userId > 0 && userId != _signedInUserId
                ? HomeViewModel.SavedAccounts().FirstOrDefault(a => a.UserId == userId)
                : null;

            if (account is null)
                HomeViewModel.LaunchUri(uri);
            else
                _ = HomeViewModel.LaunchAsAccountAsync(account, uri);
        }

        public ICommand CopyLinkCommand => new RelayCommand(() =>
        {
            long placeId = _selectedPlace?.PlaceId ?? RootPlaceId;
            if (placeId <= 0)
                return;

            try
            {
                Clipboard.SetText($"https://www.roblox.com/games/{placeId}");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Copy failed: {ex.Message}");
            }
        });

        public ICommand OpenWebsiteCommand => new RelayCommand(() =>
        {
            long placeId = RootPlaceId > 0 ? RootPlaceId : _requestedPlaceId;
            if (placeId > 0)
                Utilities.ShellExecute($"https://www.roblox.com/games/{placeId}");
        });

        #endregion

        #region Private servers

        public ObservableCollection<GamePrivateServerRow> PrivateServers { get; } = new();

        private bool _privateLoaded;

        private string _privateStatus = "";
        public string PrivateStatus { get => _privateStatus; private set { _privateStatus = value; OnPropertyChanged(nameof(PrivateStatus)); } }

        public string PrivateCountText => PrivateServers.Count > 0 ? PrivateServers.Count.ToString() : "";

        public Visibility PrivateSearchVisibility => PrivateServers.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

        private string _privateSearch = "";
        public string PrivateSearch
        {
            get => _privateSearch;
            set
            {
                _privateSearch = value ?? "";
                OnPropertyChanged(nameof(PrivateSearch));

                System.Windows.Data.CollectionViewSource.GetDefaultView(PrivateServers).Filter =
                    item => item is GamePrivateServerRow row && Matches(_privateSearch, row.Title, row.Subtitle, row.Info.OwnerName ?? "");
            }
        }

        public ICommand RefreshPrivateCommand => new AsyncRelayCommand(LoadPrivateServersAsync);

        private async Task LoadPrivateServersAsync()
        {
            _privateLoaded = true;
            PrivateStatus = "Checking your private servers...";
            PrivateServers.Clear();

            try
            {
                List<PrivateServerInfo> all = await PhasmaStrap.Integrations.PrivateServers.ListAsync(_cts.Token);
                var placeIds = Places.Select(p => p.PlaceId).ToHashSet();

                HashSet<long> hidden = GamesStore.Shared.HiddenPrivateServers();

                foreach (PrivateServerInfo info in all.Where(s => (UniverseId > 0 && s.UniverseId == UniverseId) || placeIds.Contains(s.PlaceId))
                                                      .Where(s => !hidden.Contains(s.Id))
                                                      .OrderByDescending(s => s.Owned).ThenByDescending(s => s.Active))
                    PrivateServers.Add(new GamePrivateServerRow { Info = info, IsFriend = !info.Owned && _friendIds.Contains(info.OwnerId) });

                OnPropertyChanged(nameof(PrivateRailVisibility));

                OnPropertyChanged(nameof(PrivateCountText));

                PrivateStatus = PrivateServers.Count == 0
                    ? "You don't own a private server here and nobody has shared one with you."
                    : $"{PrivateServers.Count} private server{(PrivateServers.Count == 1 ? "" : "s")} you can join";

                OnPropertyChanged(nameof(PrivateSearchVisibility));
            }
            catch (PhasmaStrap.Integrations.PrivateServers.NotSignedInException)
            {
                PrivateStatus = "Sign in to Roblox in your browser to see your private servers.";
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Private servers failed: {ex.Message}");
                PrivateStatus = $"Couldn't read your private servers: {ex.Message}";
            }
        }

        public Visibility PrivateRailVisibility => PrivateServers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        public ICommand CopyPrivateLinkCommand => new AsyncRelayCommand<GamePrivateServerRow>(async row =>
        {
            if (row is null)
                return;

            try
            {
                string? link = await PhasmaStrap.Integrations.PrivateServers.GetLinkAsync(row.Info, _cts.Token);
                if (link is null)
                {
                    PrivateStatus = $"{row.Title} has no invite link right now. Replace the invite link to make one.";
                    return;
                }

                ClipboardShare.CopyText(link);
                PrivateStatus = $"Invite link for {row.Title} copied. Anyone with it can join.";
            }
            catch (Exception ex)
            {
                PrivateStatus = $"Couldn't get the link: {ex.Message}";
            }
        });

        public ICommand NewPrivateLinkCommand => new AsyncRelayCommand<GamePrivateServerRow>(async row =>
        {
            if (row is null)
                return;

            MessageBoxResult answer = Frontend.ShowMessageBox(
                $"Make a new invite link for \"{row.Title}\"?\n\nThe current link stops working, so anyone you gave it to can't use it to join any more. People already added to the server keep access.",
                MessageBoxImage.Question, MessageBoxButton.YesNo);

            if (answer != MessageBoxResult.Yes)
                return;

            try
            {
                string? link = await PhasmaStrap.Integrations.PrivateServers.NewLinkAsync(row.Info, _cts.Token);
                if (link is null)
                {
                    PrivateStatus = "Roblox made a new link but didn't send it back. Use Copy invite link.";
                    return;
                }

                ClipboardShare.CopyText(link);
                PrivateStatus = $"New invite link for {row.Title} copied. The old one no longer works.";
            }
            catch (Exception ex)
            {
                PrivateStatus = $"Couldn't make a new link: {ex.Message}";
            }
        });

        public ICommand OpenPrivateOnWebsiteCommand => new RelayCommand<GamePrivateServerRow>(row =>
        {
            if (row is not null && row.Info.PlaceId > 0)
                Utilities.ShellExecute($"https://www.roblox.com/games/{row.Info.PlaceId}#!/game-instances");
        });

        /// <summary>Hides a private server from the lists on this PC. The Private servers tab can show it again.</summary>
        public void HidePrivateServer(GamePrivateServerRow? row)
        {
            if (row is null)
                return;

            GamesStore.Shared.SetPrivateServerHidden(row.Info.Id, true);
            PrivateServers.Remove(row);
            OnPropertyChanged(nameof(PrivateCountText));
            OnPropertyChanged(nameof(PrivateRailVisibility));
            PrivateStatus = $"{row.Title} is hidden. Show it again from the Private servers tab.";
        }

        public ICommand JoinPrivateCommand => new AsyncRelayCommand<GamePrivateServerRow>(async row =>
        {
            if (row is null)
                return;

            try
            {
                if (!await PhasmaStrap.Integrations.PrivateServers.JoinAsync(row.Info, _cts.Token))
                    PrivateStatus = "Roblox didn't give an invite for that server. It may have ended.";
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Private server join failed: {ex.Message}");
                PrivateStatus = $"Couldn't join: {ex.Message}";
            }
        });

        #endregion

        #region Play time and history

        public string SelectedPlaceIdText => _selectedPlace is null ? "" : _selectedPlace.PlaceId.ToString();

        public string SelectedPlaceCaption => _selectedPlace is null ? "" : _selectedPlace.IsRoot ? "Start place" : _selectedPlace.Name;

        private string _playtimeValue = "0";
        public string PlaytimeValue { get => _playtimeValue; private set { _playtimeValue = value; OnPropertyChanged(nameof(PlaytimeValue)); } }

        private string _playtimeUnit = "minutes";
        public string PlaytimeUnit { get => _playtimeUnit; private set { _playtimeUnit = value; OnPropertyChanged(nameof(PlaytimeUnit)); } }

        private string _lastPlayedText = "Never";
        public string LastPlayedText { get => _lastPlayedText; private set { _lastPlayedText = value; OnPropertyChanged(nameof(LastPlayedText)); } }

        private string _lastPlayedPlace = "Not played through PhasmaStrap yet";
        public string LastPlayedPlace { get => _lastPlayedPlace; private set { _lastPlayedPlace = value; OnPropertyChanged(nameof(LastPlayedPlace)); } }

        private bool BelongsHere(long universeId, long placeId) =>
            (UniverseId > 0 && universeId == UniverseId) || (placeId > 0 && Places.Any(p => p.PlaceId == placeId));

        /// <summary>Totals this game's tracked play time, the same numbers the Library shows.</summary>
        private void RefreshPlaytime()
        {
            List<PlayTimeEntry> entries = PlayTimeStore.GetAll().Where(e => BelongsHere(e.UniverseId, e.PlaceId)).ToList();
            double minutes = entries.Sum(e => e.TotalMinutes);

            if (minutes >= 60)
            {
                PlaytimeValue = ((int)(minutes / 60)).ToString();
                PlaytimeUnit = (int)(minutes / 60) == 1 ? "hour" : "hours";
            }
            else
            {
                PlaytimeValue = ((int)Math.Round(minutes)).ToString();
                PlaytimeUnit = (int)Math.Round(minutes) == 1 ? "minute" : "minutes";
            }

            PlayTimeEntry? last = entries.OrderByDescending(e => e.LastPlayed).FirstOrDefault();
            if (last is not null && last.LastPlayed != default)
            {
                LastPlayedText = last.LastPlayedText;
                string place = PlaceNames.NameOf(last.PlaceId);
                LastPlayedPlace = place.Length > 0 ? place : last.DisplayName;
            }
        }

        public ObservableCollection<GameSessionRow> Sessions { get; } = new();

        public ObservableCollection<GameDayBar> DayBars { get; } = new();

        private bool _historyLoaded;

        private string _historySummary = "";
        public string HistorySummary { get => _historySummary; private set { _historySummary = value; OnPropertyChanged(nameof(HistorySummary)); } }

        public Visibility HistoryEmptyVisibility => _historyLoaded && Sessions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        public Visibility HistoryListVisibility => Sessions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        public ICommand RefreshHistoryCommand => new RelayCommand(LoadHistory);

        public ICommand RejoinCommand => new RelayCommand<GameSessionRow>(row =>
        {
            if (row is not null && row.CanRejoin)
                Launch(RobloxLaunch.DeepLink(row.PlaceId, row.JobId));
        });

        private static string WhenText(DateTime local)
        {
            DateTime today = DateTime.Now.Date;
            if (local.Date == today)
                return $"Today {local:HH:mm}";
            if (local.Date == today.AddDays(-1))
                return $"Yesterday {local:HH:mm}";
            if (local.Date > today.AddDays(-7))
                return $"{local:ddd HH:mm}";
            return local.ToString("d MMM HH:mm");
        }

        private static string DaysAgoText(DateTime local)
        {
            int days = (int)(DateTime.Now.Date - local.Date).TotalDays;
            return days switch
            {
                <= 0 => "today",
                1 => "yesterday",
                < 7 => $"{days} days ago",
                < 14 => "last week",
                _ => $"on {local:d MMM}",
            };
        }

        private void LoadHistory()
        {
            _historyLoaded = true;
            Sessions.Clear();
            DayBars.Clear();

            List<ServerVisit> visits;
            try
            {
                visits = SessionStore.Shared.Load().Sessions
                    .SelectMany(s => s.Visits)
                    .Where(v => BelongsHere(v.UniverseId, v.PlaceId))
                    .OrderByDescending(v => v.JoinedUtc)
                    .ToList();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Session history failed: {ex.Message}");
                visits = new List<ServerVisit>();
            }

            foreach (ServerVisit visit in visits.Take(100))
            {
                string place = PlaceNames.NameOf(visit.PlaceId);
                if (place.Length == 0)
                    place = Places.FirstOrDefault(p => p.PlaceId == visit.PlaceId)?.Name ?? (visit.GameName.Length > 0 ? visit.GameName : $"Place {visit.PlaceId}");

                bool isPublic = visit.ServerType is "" or "Public";
                var server = new List<string>();
                if (visit.Region.Length > 0)
                    server.Add(visit.Region);
                if (!isPublic)
                    server.Add("private");

                (int Average, int Low)? fps = SessionStats.FpsSummary(visit);

                Sessions.Add(new GameSessionRow
                {
                    PlaceId = visit.PlaceId,
                    JobId = visit.JobId,
                    CanRejoin = visit.PlaceId > 0 && visit.JobId.Length > 0 && isPublic,
                    WhenText = WhenText(visit.JoinedUtc.ToLocalTime()),
                    PlaceText = place,
                    ServerText = string.Join("  ·  ", server),
                    TimeText = SessionStats.Duration(visit.Length.TotalMinutes),
                    FpsText = fps is null ? "" : $"{fps.Value.Average} avg",
                });
            }

            // Minutes played per day over the last two weeks, oldest first.
            DateTime first = DateTime.Now.Date.AddDays(-13);
            double[] perDay = new double[14];
            foreach (ServerVisit visit in visits)
            {
                int day = (int)(visit.JoinedUtc.ToLocalTime().Date - first).TotalDays;
                if (day >= 0 && day < 14)
                    perDay[day] += visit.Length.TotalMinutes;
            }

            double most = Math.Max(perDay.Max(), 1);
            for (int i = 0; i < 14; i++)
                DayBars.Add(new GameDayBar { Height = Math.Max(perDay[i] / most * 90, perDay[i] > 0 ? 4 : 2), Tip = $"{first.AddDays(i):ddd d MMM}: {SessionStats.Duration(perDay[i])}" });

            int recent = visits.Count(v => v.JoinedUtc.ToLocalTime().Date >= first);
            double hours = perDay.Sum() / 60;
            string total = hours >= 1 ? $"{(int)Math.Round(hours)} hours" : SessionStats.Duration(perDay.Sum());
            HistorySummary = recent == 0
                ? "Not played in the last 14 days"
                : $"{total} in {recent} session{(recent == 1 ? "" : "s")}";

            OnPropertyChanged(nameof(HistoryEmptyVisibility));
            OnPropertyChanged(nameof(HistoryListVisibility));
        }

        #endregion

        #region Friends in this game

        public ObservableCollection<GameFriendRow> FriendsInGame { get; } = new();

        private HashSet<long> _friendIds = new();

        private string _friendsStatus = "Looking for friends...";
        public string FriendsStatus { get => _friendsStatus; private set { _friendsStatus = value; OnPropertyChanged(nameof(FriendsStatus)); } }

        public Visibility FriendsEmptyVisibility => FriendsInGame.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        public ICommand JoinFriendCommand => new RelayCommand<GameFriendRow>(row =>
        {
            if (row?.Presence is not null && row.Presence.Joinable)
                Launch(FriendsService.GetJoinDeeplink(row.Presence));
        });

        public ICommand RefreshFriendsCommand => new AsyncRelayCommand(LoadFriendsAsync);

        private bool InThisGame(FriendPresence presence) =>
            presence.Type == FriendPresenceType.InGame
            && ((UniverseId > 0 && presence.UniverseId == UniverseId)
                || (RootPlaceId > 0 && presence.RootPlaceId == RootPlaceId)
                || (presence.PlaceId > 0 && Places.Any(p => p.PlaceId == presence.PlaceId)));

        /// <summary>
        /// Friends playing this game right now (Roblox presence), then friends you have played it with before
        /// (the session history on Activity). Also fills the friends shown on each server row.
        /// </summary>
        private async Task LoadFriendsAsync()
        {
            try
            {
                RobloxCookie.RobloxAccount? me = await RobloxCookie.GetAccountAsync();
                if (me is null)
                {
                    FriendsStatus = "Sign in to Roblox to see friends in this game.";
                    return;
                }

                List<FriendInfo> friends = await FriendsService.GetFriendsAsync(me.UserId, _cts.Token);
                _friendIds = friends.Select(f => f.UserId).ToHashSet();

                foreach (GamePrivateServerRow row in PrivateServers)
                    row.IsFriend = !row.Info.Owned && _friendIds.Contains(row.Info.OwnerId);

                Dictionary<long, FriendPresence> presence = friends.Count == 0
                    ? new Dictionary<long, FriendPresence>()
                    : await FriendsService.GetPresenceAsync(friends.Select(f => f.UserId), _cts.Token);

                string NameOf(FriendInfo f) => string.IsNullOrWhiteSpace(f.DisplayName) ? f.Username : f.DisplayName;

                var playing = friends
                    .Where(f => presence.TryGetValue(f.UserId, out FriendPresence? p) && InThisGame(p))
                    .ToList();

                // Friends you have been in a server of this game with, most recent first.
                var together = new List<(long UserId, string Name, DateTime When)>();
                try
                {
                    foreach (ServerVisit visit in SessionStore.Shared.Load().Sessions.SelectMany(x => x.Visits)
                                 .Where(v => BelongsHere(v.UniverseId, v.PlaceId))
                                 .OrderByDescending(v => v.JoinedUtc))
                    {
                        foreach (SessionFriend friend in visit.Friends)
                        {
                            if (playing.Any(f => f.UserId == friend.UserId) || together.Any(t => t.UserId == friend.UserId))
                                continue;

                            together.Add((friend.UserId, friend.Name, visit.JoinedUtc.ToLocalTime()));
                        }
                    }
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Session history for friends failed: {ex.Message}");
                }

                var shown = playing.Select(f => f.UserId).Concat(together.Take(8).Select(t => t.UserId)).ToList();
                Dictionary<long, string> avatars = shown.Count == 0
                    ? new Dictionary<long, string>()
                    : await FriendsService.GetAvatarsAsync(shown, _cts.Token);

                FriendsInGame.Clear();
                var byJob = new Dictionary<string, List<ServerFriend>>(StringComparer.OrdinalIgnoreCase);

                foreach (FriendInfo friend in playing)
                {
                    FriendPresence p = presence[friend.UserId];
                    avatars.TryGetValue(friend.UserId, out string? avatar);

                    string place = Places.FirstOrDefault(x => x.PlaceId == p.PlaceId)?.Name ?? (p.LastLocation.Length > 0 ? p.LastLocation : Name);

                    FriendsInGame.Add(new GameFriendRow
                    {
                        UserId = friend.UserId,
                        Name = NameOf(friend),
                        Detail = $"{place}, right now",
                        AvatarUrl = avatar,
                        Presence = p,
                    });

                    if (!string.IsNullOrEmpty(p.GameId))
                    {
                        if (!byJob.TryGetValue(p.GameId, out List<ServerFriend>? list))
                            byJob[p.GameId] = list = new List<ServerFriend>();

                        list.Add(new ServerFriend { UserId = friend.UserId, Name = NameOf(friend), AvatarUrl = avatar });
                    }
                }

                foreach (var (userId, name, when) in together.Take(8))
                {
                    avatars.TryGetValue(userId, out string? avatar);
                    string friendName = friends.FirstOrDefault(f => f.UserId == userId) is FriendInfo info ? NameOf(info) : name;

                    FriendsInGame.Add(new GameFriendRow
                    {
                        UserId = userId,
                        Name = friendName.Length > 0 ? friendName : $"User {userId}",
                        Detail = $"Played together {DaysAgoText(when)}",
                        AvatarUrl = avatar,
                    });
                }

                _friendsByJob = byJob;
                foreach (ServerRow row in Servers)
                    row.Friends = byJob.TryGetValue(row.Server.JobId, out List<ServerFriend>? list) ? list : new List<ServerFriend>();

                OnPropertyChanged(nameof(FriendsEmptyVisibility));
                OnPropertyChanged(nameof(FriendsInsideVisibility));

                FriendsStatus = FriendsInGame.Count == 0
                    ? "None of your friends are playing this right now."
                    : playing.Count == 0 ? "Nobody is playing it right now." : $"{playing.Count} playing now";
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Friends in this game failed: {ex.Message}");
                FriendsStatus = "Couldn't load your friends right now.";
            }
        }

        #endregion

        #region Account

        public ObservableCollection<GameAccountOption> AccountOptions { get; } = new();

        private long _signedInUserId;

        private GameAccountOption? _selectedAccount;
        /// <summary>Which saved account Play and Join use for this game. Kept per game on this PC.</summary>
        public GameAccountOption? SelectedAccount
        {
            get => _selectedAccount;
            set
            {
                if (value is null || ReferenceEquals(value, _selectedAccount) || _cts.IsCancellationRequested)
                    return;

                _selectedAccount = value;
                OnPropertyChanged(nameof(SelectedAccount));
                OnPropertyChanged(nameof(AccountNote));

                if (UniverseId > 0)
                    GamesStore.Shared.SetAccountFor(UniverseId, value.UserId == _signedInUserId ? 0 : value.UserId);
            }
        }

        public string AccountNote => _selectedAccount is null || _selectedAccount.UserId == 0 || _selectedAccount.UserId == _signedInUserId
            ? "Plays as whoever is signed in to Roblox."
            : "Roblox has to be closed to switch accounts. PhasmaStrap swaps the login, then launches.";

        public Visibility AccountVisibility => AccountOptions.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

        private async Task LoadAccountsAsync()
        {
            try
            {
                RobloxCookie.RobloxAccount? me = await RobloxCookie.GetAccountAsync();
                _signedInUserId = me?.UserId ?? 0;

                // Waits for the game's ID, which the saved choice is keyed by.
                for (int i = 0; i < 40 && UniverseId <= 0 && !_cts.IsCancellationRequested; i++)
                    await Task.Delay(250);

                List<AccountQuickSwitch.Account> saved = HomeViewModel.SavedAccounts();

                AccountOptions.Clear();
                AccountOptions.Add(new GameAccountOption { UserId = 0, Name = me is null ? "Signed in account" : $"{me.Username} (signed in)" });
                foreach (AccountQuickSwitch.Account account in saved.Where(a => a.UserId != _signedInUserId))
                    AccountOptions.Add(new GameAccountOption { UserId = account.UserId, Name = account.Title });

                long wanted = GamesStore.Shared.AccountFor(UniverseId);
                _selectedAccount = AccountOptions.FirstOrDefault(a => a.UserId == wanted) ?? AccountOptions[0];
                OnPropertyChanged(nameof(SelectedAccount));
                OnPropertyChanged(nameof(AccountNote));
                OnPropertyChanged(nameof(AccountVisibility));
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Accounts failed: {ex.Message}");
            }
        }

        #endregion

        #region Launch setup

        public ObservableCollection<GameProfileOption> ProfileOptions { get; } = new();

        private bool _syncingProfile;

        private bool _wholeGame = true;
        public bool WholeGame
        {
            get => _wholeGame;
            set
            {
                if (_wholeGame == value)
                    return;

                _wholeGame = value;
                OnPropertyChanged(nameof(WholeGame));
                OnPropertyChanged(nameof(OnlyThisPlace));
                SyncProfileSelection();
            }
        }

        public bool OnlyThisPlace
        {
            get => !_wholeGame;
            set => WholeGame = !value;
        }

        public string ScopePlaceText => _selectedPlace is null ? "This place only" : $"{_selectedPlace.Name} only";

        private GameProfileOption? _selectedProfile;
        public GameProfileOption? SelectedProfile
        {
            get => _selectedProfile;
            set
            {
                // A null here comes from the ComboBox letting go of its items (page closing or
                // switching games), never from the user: "Your usual flags" is a real option.
                if (value is null || ReferenceEquals(_selectedProfile, value) || _cts.IsCancellationRequested)
                    return;

                _selectedProfile = value;
                OnPropertyChanged(nameof(SelectedProfile));

                if (!_syncingProfile)
                    AssignProfile(value?.Id ?? "");
            }
        }

        private string _profileNote = "";
        public string ProfileNote { get => _profileNote; private set { _profileNote = value; OnPropertyChanged(nameof(ProfileNote)); } }

        private void RebuildProfileOptions()
        {
            ProfileOptions.Clear();
            ProfileOptions.Add(new GameProfileOption { Id = "", Name = "Your usual flags" });
            foreach (FlagProfile profile in App.FlagProfiles.Prop.Profiles.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
                ProfileOptions.Add(new GameProfileOption { Id = profile.Id, Name = profile.ChangeCount == 0 ? $"{profile.Name} (empty)" : $"{profile.Name}  ·  {profile.ChangeCount} flags" });
        }

        private FlagGameRule? RuleForScope()
        {
            var rules = App.FlagProfiles.Prop.Rules;

            if (_wholeGame)
                return UniverseId > 0 ? rules.FirstOrDefault(r => r.IsWholeGame && r.UniverseId == UniverseId) : null;

            long placeId = _selectedPlace?.PlaceId ?? 0;
            return placeId > 0 ? rules.FirstOrDefault(r => r.PlaceId == placeId) : null;
        }

        private void SyncProfileSelection()
        {
            _syncingProfile = true;
            try
            {
                RebuildProfileOptions();
                string id = RuleForScope()?.ProfileId ?? "";
                _selectedProfile = ProfileOptions.FirstOrDefault(o => o.Id == id) ?? ProfileOptions[0];
                OnPropertyChanged(nameof(SelectedProfile));
            }
            finally
            {
                _syncingProfile = false;
            }

            RefreshProfileNote();
        }

        private void RefreshProfileNote()
        {
            long placeId = _selectedPlace?.PlaceId ?? 0;
            FlagProfile? effective = FlagLayers.ProfileFor(App.FlagProfiles.Prop, placeId, UniverseId);
            FlagGameRule? rule = FlagLayers.RuleFor(App.FlagProfiles.Prop, placeId, UniverseId);
            string where = _selectedPlace?.Name ?? "This place";

            if (effective is null)
                ProfileNote = $"{where} starts with your usual flags.";
            else if (rule is not null && rule.IsWholeGame && !_wholeGame)
                ProfileNote = $"{where} uses \"{effective.Name}\" from the whole game rule. Pick a profile here to give it its own.";
            else
                ProfileNote = $"{where} starts with \"{effective.Name}\" on top of your usual flags.";
        }

        private void AssignProfile(string profileId)
        {
            if (UniverseId <= 0 && _wholeGame)
            {
                ProfileNote = "This game's ID isn't known yet, so pick a single place instead.";
                return;
            }

            var data = App.FlagProfiles.Prop;
            FlagGameRule? rule = RuleForScope();

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

                rule.UniverseId = UniverseId;
                rule.RootPlaceId = RootPlaceId;
                rule.PlaceId = _wholeGame ? 0 : _selectedPlace?.PlaceId ?? 0;
                rule.PlaceName = _wholeGame ? "" : _selectedPlace?.Name ?? "";
                rule.GameName = Name;
                rule.IconUrl = IconUrl;
                rule.ProfileId = profileId;

                if (rule.PlaceId > 0)
                    GameLookup.Remember(rule.PlaceId, UniverseId);
            }

            try
            {
                // Saved straight away: a game launched from this page starts a new process that reads it from disk.
                App.FlagProfiles.Save();
                App.FlagProfiles.NotifyEdited();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Saving the profile assignment failed: {ex.Message}");
            }

            RefreshProfileNote();
            ProfileAssigned?.Invoke(this, EventArgs.Empty);
        }

        public ICommand EditFlagsCommand => new RelayCommand(() =>
            OpenPageRequested?.Invoke(this, typeof(PhasmaStrap.UI.Elements.Settings.Pages.FastFlagGamesPage)));

        public string OverlaySummary
        {
            get
            {
                long placeId = _selectedPlace?.PlaceId ?? 0;
                if (placeId <= 0 || !App.Settings.Prop.OverlayPlaceProfiles.TryGetValue(placeId.ToString(), out var profile))
                    return "Your usual overlay";

                return $"HUD {(profile.HudEnabled ? "on" : "off")}, crosshair {(profile.CrosshairEnabled ? "on" : "off")}";
            }
        }

        public string ResolutionSummary
        {
            get
            {
                long placeId = _selectedPlace?.PlaceId ?? 0;
                if (placeId <= 0 || !App.Settings.Prop.InGameResolutionPlaceProfiles.TryGetValue(placeId.ToString(), out var profile))
                    return "Your usual resolution";

                string text = $"{profile.Width} × {profile.Height}";
                if (profile.RefreshRate > 0)
                    text += $" at {profile.RefreshRate} Hz";
                return text;
            }
        }

        public ICommand EditOverlayCommand => new RelayCommand(() =>
            OpenPageRequested?.Invoke(this, typeof(PhasmaStrap.UI.Elements.Settings.Pages.OverlaysHudPage)));

        public ICommand EditResolutionCommand => new RelayCommand(() =>
            OpenPageRequested?.Invoke(this, typeof(PhasmaStrap.UI.Elements.Settings.Pages.RenderingResolutionPage)));

        #endregion
    }
}
