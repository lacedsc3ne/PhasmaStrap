using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using PhasmaStrap.Integrations;
using PhasmaStrap.Models;
using PhasmaStrap.Utility;

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

        public string Caption => IsRoot ? "Start place" : $"Place {PlaceId}";
    }

    public sealed class ServerRow
    {
        public ServerListItem Server { get; init; } = new();

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

    public sealed class GamePrivateServerRow
    {
        public PrivateServerInfo Info { get; init; } = new();
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

        public const string TabServers = "servers";
        public const string TabAbout = "about";
        public const string TabPrivate = "private";

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
        public string Name { get => _name; private set { _name = value; OnPropertyChanged(nameof(Name)); } }

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
            }
        }

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

            if (tab == TabPrivate && !_privateLoaded)
                _ = LoadPrivateServersAsync();
        }

        public Visibility ServersTabVisibility => _tab == TabServers ? Visibility.Visible : Visibility.Collapsed;
        public Visibility AboutTabVisibility => _tab == TabAbout ? Visibility.Visible : Visibility.Collapsed;
        public Visibility PrivateTabVisibility => _tab == TabPrivate ? Visibility.Visible : Visibility.Collapsed;

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
                Status = "";
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

        private void SelectPlace(PlaceItem place)
        {
            _selectedPlace = place;
            OnPropertyChanged(nameof(SelectedPlace));
            OnPropertyChanged(nameof(PlayText));
            OnPropertyChanged(nameof(PlaceHint));
            OnPropertyChanged(nameof(OverlaySummary));
            OnPropertyChanged(nameof(ResolutionSummary));
            OnPropertyChanged(nameof(ScopePlaceText));

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
                ApplyServerView();
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

            view = _sortMode switch
            {
                "players" => view.OrderByDescending(s => s.Playing),
                "space" => view.OrderByDescending(s => s.MaxPlayers - s.Playing).ThenBy(s => s.Ping <= 0 ? int.MaxValue : s.Ping),
                _ => view.OrderBy(s => s.Ping <= 0 ? int.MaxValue : s.Ping),
            };

            Servers.Clear();
            foreach (ServerListItem server in view.Take(150))
                Servers.Add(new ServerRow { Server = server });

            int hidden = _allServers.Count - Servers.Count;
            ServerStatus = _allServers.Count == 0
                ? "No public servers are running for this place right now."
                : $"{_allServers.Count} servers running" + (hidden > 0 ? $", {hidden} hidden" : "");
        }

        public ICommand JoinServerCommand => new RelayCommand<ServerRow>(row =>
        {
            if (row is null || _selectedPlace is null)
                return;

            ServerBrowser.JoinServer(_selectedPlace.PlaceId, row.Server.JobId);
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
                ServerBrowser.JoinServer(_selectedPlace.PlaceId, best.JobId);
        });

        public ICommand PlayCommand => new RelayCommand(Play);

        private void Play()
        {
            long placeId = _selectedPlace?.PlaceId ?? (RootPlaceId > 0 ? RootPlaceId : _requestedPlaceId);
            if (placeId > 0)
                HomeViewModel.LaunchUri(RobloxLaunch.DeepLink(placeId));
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

                foreach (PrivateServerInfo info in all.Where(s => (UniverseId > 0 && s.UniverseId == UniverseId) || placeIds.Contains(s.PlaceId))
                                                      .OrderByDescending(s => s.Owned).ThenByDescending(s => s.Active))
                    PrivateServers.Add(new GamePrivateServerRow { Info = info });

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
