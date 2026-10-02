using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

using CommunityToolkit.Mvvm.Input;

using PhasmaStrap.Integrations;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public sealed class FriendRow : NotifyPropertyChangedViewModel
    {
        public long UserId { get; init; }
        public string Name { get; init; } = "";

        private string? _avatarUrl;

        public string? AvatarUrl
        {
            get => _avatarUrl;
            set { _avatarUrl = value; OnPropertyChanged(nameof(AvatarUrl)); }
        }
        public FriendPresenceType Type { get; init; }
        public string StatusText { get; init; } = "";
        public bool Joinable { get; init; }
        public FriendPresence? Presence { get; init; }

        private static int Rank(FriendPresenceType type) => type switch
        {
            FriendPresenceType.InGame => 0,
            FriendPresenceType.Online => 1,
            FriendPresenceType.InStudio => 2,
            _ => 3,
        };

        public int SortRank => Rank(Type);

        public string GroupName => Type switch
        {
            FriendPresenceType.InGame => "In game",
            FriendPresenceType.Offline => "Offline",
            _ => "Online",
        };

        public int GroupRank => Type switch
        {
            FriendPresenceType.InGame => 0,
            FriendPresenceType.Offline => 2,
            _ => 1,
        };

        public Action<FriendRow>? FavouriteChanged;

        private bool _isFavourite;
        public bool IsFavourite
        {
            get => _isFavourite;
            set
            {
                if (_isFavourite == value)
                    return;

                _isFavourite = value;
                PhasmaStrap.Utility.FriendNotesStore.Shared.Set(UserId, _isFavourite, _note);
                OnPropertyChanged(nameof(IsFavourite));
                OnPropertyChanged(nameof(StarSymbol));
                OnPropertyChanged(nameof(StarFilled));
                FavouriteChanged?.Invoke(this);
            }
        }

        public Wpf.Ui.Common.SymbolRegular StarSymbol => Wpf.Ui.Common.SymbolRegular.Star24;
        public bool StarFilled => _isFavourite;

        private string _note = "";
        public string Note
        {
            get => _note;
            set
            {
                value ??= "";
                if (_note == value)
                    return;

                _note = value;
                PhasmaStrap.Utility.FriendNotesStore.Shared.Set(UserId, _isFavourite, _note);
                OnPropertyChanged(nameof(Note));
            }
        }

        public void LoadNote(bool favourite, string note)
        {
            _isFavourite = favourite;
            _note = note;
        }

        public bool Matches(string search) =>
            search.Length == 0
            || Name.Contains(search, StringComparison.OrdinalIgnoreCase)
            || Username.Contains(search, StringComparison.OrdinalIgnoreCase)
            || _note.Contains(search, StringComparison.OrdinalIgnoreCase)
            || StatusText.Contains(search, StringComparison.OrdinalIgnoreCase);

        public string Username { get; init; } = "";

        // ---- Alerts just for this friend

        private static readonly string[] AlertKeys =
        {
            PhasmaStrap.Utility.FriendNotesStore.AlertMode.Usual,
            PhasmaStrap.Utility.FriendNotesStore.AlertMode.Online,
            PhasmaStrap.Utility.FriendNotesStore.AlertMode.Game,
            PhasmaStrap.Utility.FriendNotesStore.AlertMode.Never,
        };

        /// <summary>Same order as AlertKeys.</summary>
        public static readonly string[] AlertLabels = { "Use my usual alerts", "When they come online", "When they join a game", "Never" };

        private string _alerts = "";

        /// <summary>Index into AlertLabels. Saved straight away and read by FriendActivityMonitor.</summary>
        public int AlertModeIndex
        {
            get => Math.Max(0, Array.IndexOf(AlertKeys, _alerts));
            set
            {
                if (value < 0 || value >= AlertKeys.Length || AlertKeys[value] == _alerts)
                    return;

                _alerts = AlertKeys[value];
                PhasmaStrap.Utility.FriendNotesStore.Shared.SetAlerts(UserId, _alerts);
                OnPropertyChanged(nameof(AlertModeIndex));
                OnPropertyChanged(nameof(AlertModeText));
            }
        }

        public string AlertModeText => AlertLabels[AlertModeIndex];

        public void LoadAlerts(string? mode) => _alerts = Array.IndexOf(AlertKeys, mode ?? "") < 0 ? "" : mode!;

        // ---- Friends since

        private DateTime? _friendsSinceUtc;

        /// <summary>When you became friends, from this PC's own record or PhasmaStrap's server. Null when nobody knows.</summary>
        public DateTime? FriendsSinceUtc
        {
            get => _friendsSinceUtc;
            set { _friendsSinceUtc = value; OnPropertyChanged(nameof(FriendsSinceUtc)); OnPropertyChanged(nameof(FriendsSinceSuffix)); }
        }

        public string FriendsSinceSuffix
        {
            get
            {
                if (_friendsSinceUtc is not DateTime since)
                    return "";

                DateTime local = since.ToLocalTime();
                return local.Year == DateTime.Now.Year ? $" · friends since {local:d MMMM}" : $" · friends since {local:yyyy}";
            }
        }

        // ---- Same server

        public bool CanJoinServer => Presence is { Type: FriendPresenceType.InGame } p && p.PlaceId > 0 && !string.IsNullOrEmpty(p.GameId);

        public Visibility SameServerVisibility => CanJoinServer ? Visibility.Visible : Visibility.Collapsed;

        // ---- Right now: how long they have played and which server they are on

        /// <summary>"Playing for 42 minutes", counted from when this app first saw them in this game. Empty when unknown.</summary>
        public string PlayingForText => PhasmaStrap.Utility.FriendPlaySessions.PlayingForText(UserId, Presence);

        public Visibility PlayingForVisibility => PlayingForText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>The plain "Right now" caption, shown when there is no play time to show instead.</summary>
        public Visibility RightNowCaptionVisibility => PlayingForText.Length > 0 ? Visibility.Collapsed : Visibility.Visible;

        public void RefreshPlayingFor()
        {
            OnPropertyChanged(nameof(PlayingForText));
            OnPropertyChanged(nameof(PlayingForVisibility));
            OnPropertyChanged(nameof(RightNowCaptionVisibility));
        }

        private string _serverText = "";

        /// <summary>"Frankfurt · 9 of 12 players · 24 ms from you", with unknown parts left out. Empty when nothing is known.</summary>
        public string ServerText
        {
            get => _serverText;
            set { _serverText = value ?? ""; OnPropertyChanged(nameof(ServerText)); OnPropertyChanged(nameof(ServerTextVisibility)); }
        }

        public Visibility ServerTextVisibility => _serverText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        private string _serverToolTip = "";
        public string? ServerToolTip { get => _serverToolTip.Length > 0 ? _serverToolTip : null; set { _serverToolTip = value ?? ""; OnPropertyChanged(nameof(ServerToolTip)); } }

        private string? _gameIconUrl;

        /// <summary>Icon of the game they are in now, for the tile in the Right now card.</summary>
        public string? GameIconUrl
        {
            get => _gameIconUrl;
            set { _gameIconUrl = value; OnPropertyChanged(nameof(GameIconUrl)); }
        }

        public Visibility GameTileVisibility => Type == FriendPresenceType.InGame ? Visibility.Visible : Visibility.Collapsed;
        public Visibility PresenceDotVisibility => Type == FriendPresenceType.InGame ? Visibility.Collapsed : Visibility.Visible;

        public string GameInitials
        {
            get
            {
                string game = StatusText.Split(new[] { " · ", ": " }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                string letters = string.Concat(game.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => char.ToUpperInvariant(w[0])));
                return letters.Length > 0 ? letters : "?";
            }
        }

        // ---- History together, from this PC's session history

        private List<PlayedTogetherTile> _playedTogether = new();

        public List<PlayedTogetherTile> PlayedTogether
        {
            get => _playedTogether;
            private set { _playedTogether = value; OnPropertyChanged(nameof(PlayedTogether)); OnPropertyChanged(nameof(PlayedTogetherVisibility)); OnPropertyChanged(nameof(PlayedTogetherEmptyVisibility)); }
        }

        public Visibility PlayedTogetherVisibility => _playedTogether.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility PlayedTogetherEmptyVisibility => _playedTogether.Count > 0 ? Visibility.Collapsed : Visibility.Visible;

        private string _playedTogetherEmptyText = "";
        public string PlayedTogetherEmptyText { get => _playedTogetherEmptyText; private set { _playedTogetherEmptyText = value; OnPropertyChanged(nameof(PlayedTogetherEmptyText)); } }

        private string _ranIntoText = "";
        public string RanIntoText { get => _ranIntoText; private set { _ranIntoText = value; OnPropertyChanged(nameof(RanIntoText)); } }

        public void SetHistory(List<PlayedTogetherTile> tiles, string emptyText, string ranInto)
        {
            PlayedTogetherEmptyText = emptyText;
            RanIntoText = ranInto;
            PlayedTogether = tiles;
        }
    }

    /// <summary>A game you were on the same server with a friend in, for "Played together recently".</summary>
    public sealed class PlayedTogetherTile
    {
        public string Name { get; init; } = "";
        public string? IconUrl { get; init; }
        public string When { get; init; } = "";
        public string Initials { get; init; } = "";
        public string ToolTip { get; init; } = "";
    }

    /// <summary>A saved account, for the small Accounts card on the Friends page.</summary>
    public sealed class AccountSummaryRow
    {
        public string Name { get; init; } = "";
        public string Detail { get; init; } = "";
        public string? AvatarUrl { get; init; }
        public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name.Substring(0, 1).ToUpperInvariant();
        public bool IsCurrent { get; init; }
        public Visibility CurrentVisibility => IsCurrent ? Visibility.Visible : Visibility.Collapsed;
    }

    public sealed class FriendsViewModel : NotifyPropertyChangedViewModel
    {
        private const string LOG_IDENT = "FriendsViewModel";

        private static async Task CacheAvatarsAsync(List<FriendRow> rows, Dictionary<long, string> remote)
        {
            foreach (FriendRow row in rows)
            {
                if (AvatarCache.TryGetFresh(row.UserId) is not null)
                    continue;

                if (!remote.TryGetValue(row.UserId, out string? url) || string.IsNullOrEmpty(url))
                    continue;

                string? local = await AvatarCache.DownloadAsync(row.UserId, url).ConfigureAwait(false);
                if (local is null)
                    continue;

                Application.Current?.Dispatcher.BeginInvoke(new Action(() => row.AvatarUrl = local));
            }
        }

        public ObservableCollection<FriendRow> Friends { get; } = new();

        private List<FriendRow> _all = new();

        private string _search = "";
        public string Search
        {
            get => _search;
            set { _search = value ?? ""; OnPropertyChanged(nameof(Search)); ApplyView(); }
        }

        private bool _favouritesOnly;
        public bool FavouritesOnly
        {
            get => _favouritesOnly;
            set { _favouritesOnly = value; OnPropertyChanged(nameof(FavouritesOnly)); ApplyView(); }
        }

        /// <summary>Friend alerts: a pop up when a friend comes online (for friends on their usual alerts).</summary>
        public bool AlertOnline
        {
            get => App.Settings.Prop.FriendAlertOnline;
            set { App.Settings.Prop.FriendAlertOnline = value; App.Settings.SaveDeferred(); OnPropertyChanged(nameof(AlertOnline)); }
        }

        /// <summary>Friend alerts: a pop up when a friend starts playing a game (for friends on their usual alerts).</summary>
        public bool AlertGame
        {
            get => App.Settings.Prop.FriendAlertGame;
            set { App.Settings.Prop.FriendAlertGame = value; App.Settings.SaveDeferred(); OnPropertyChanged(nameof(AlertGame)); }
        }

        public bool AlertFavouritesOnly
        {
            get => App.Settings.Prop.FriendActivityFavouritesOnly;
            set { App.Settings.Prop.FriendActivityFavouritesOnly = value; App.Settings.SaveDeferred(); OnPropertyChanged(nameof(AlertFavouritesOnly)); }
        }

        private void ApplyView()
        {
            string search = _search.Trim();
            long? selectedId = _selectedFriend?.UserId;

            List<FriendRow> rows = _all
                .Where(r => (!_favouritesOnly || r.IsFavourite) && r.Matches(search))
                .OrderBy(r => r.GroupRank)
                .ThenByDescending(r => r.IsFavourite)
                .ThenBy(r => r.SortRank)
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Friends.Clear();
            foreach (FriendRow row in rows)
                Friends.Add(row);

            SelectedFriend = selectedId is null ? null : rows.FirstOrDefault(r => r.UserId == selectedId.Value);

            OnPropertyChanged(nameof(HasFriends));
            OnPropertyChanged(nameof(EmptyStateVisibility));
        }

        private FriendRow? _selectedFriend;

        public FriendRow? SelectedFriend
        {
            get => _selectedFriend;
            set
            {
                if (ReferenceEquals(_selectedFriend, value))
                    return;

                _selectedFriend = value;
                BuildHistory(value);
                value?.RefreshPlayingFor();
                _ = LoadServerAsync(value);
                OnPropertyChanged(nameof(SelectedFriend));
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(SelectionVisibility));
                OnPropertyChanged(nameof(NoSelectionVisibility));
            }
        }

        public bool HasSelection => _selectedFriend is not null;
        public Visibility SelectionVisibility => _selectedFriend is null ? Visibility.Collapsed : Visibility.Visible;
        public Visibility NoSelectionVisibility => _selectedFriend is null ? Visibility.Visible : Visibility.Collapsed;

        private bool _loading;
        public bool Loading { get => _loading; private set { _loading = value; OnPropertyChanged(nameof(Loading)); } }

        private string _status = "";
        public string Status { get => _status; private set { _status = value; OnPropertyChanged(nameof(Status)); } }

        public bool HasFriends => Friends.Count > 0;
        public Visibility EmptyStateVisibility => !Loading && Friends.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        public ICommand RefreshCommand { get; }
        public ICommand JoinCommand { get; }
        public ICommand InviteToPartyCommand { get; }

        public bool AlertsEnabled
        {
            get => App.Settings.Prop.FriendActivityAlertsEnabled;
            set
            {
                App.Settings.Prop.FriendActivityAlertsEnabled = value;
                OnPropertyChanged(nameof(AlertsEnabled));

                if (value)
                    PhasmaStrap.Utility.FriendActivityMonitor.Start();
                else
                    PhasmaStrap.Utility.FriendActivityMonitor.Stop();
            }
        }

        public int PollSeconds
        {
            get => App.Settings.Prop.FriendActivityPollSeconds;
            set => App.Settings.Prop.FriendActivityPollSeconds = Math.Max(20, value);
        }

        public Visibility PartyInviteVisibility => App.Settings.Prop.PartyEnabled ? Visibility.Visible : Visibility.Collapsed;

        private static async Task InviteToPartyAsync(FriendRow? row)
        {
            if (row is null)
                return;

            bool sent = await PhasmaStrap.Utility.PartyService.InviteAsync(row.UserId);

            NotificationCenter.Notify(
                sent ? "Party invite sent" : "Could not invite them",
                sent
                    ? $"{row.Name} will see it in PhasmaStrap."
                    : $"{row.Name} needs PhasmaStrap with a linked Roblox account, and you need to be in a party.",
                NotificationCategory.General,
                kind: NotificationKindId.Party);
        }

        public FriendsViewModel()
        {
            RefreshCommand = new AsyncRelayCommand(RefreshAsync);
            JoinCommand = new AsyncRelayCommand<FriendRow?>(JoinAsync);
            InviteToPartyCommand = new AsyncRelayCommand<FriendRow?>(InviteToPartyAsync);
            SameServerCommand = new AsyncRelayCommand<FriendRow?>(SameServerAsync);
            StartPartyCommand = new AsyncRelayCommand(StartPartyAsync);
            JoinPartyCommand = new AsyncRelayCommand(JoinPartyAsync);
            OpenPartyCommand = new RelayCommand(() => Go(typeof(PhasmaStrap.UI.Elements.Settings.Pages.PartyPage)));
            OpenAccountsCommand = new RelayCommand(() => Go(typeof(PhasmaStrap.UI.Elements.Settings.Pages.AccountsPage)));
            OpenActivityCommand = new RelayCommand(() => Go(typeof(PhasmaStrap.UI.Elements.Settings.Pages.ActivityPage)));

            var view = CollectionViewSource.GetDefaultView(Friends);
            view.GroupDescriptions.Clear();
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(FriendRow.GroupName)));

            _ = RefreshAsync();
        }

        private async Task RefreshAsync()
        {
            Loading = true;
            OnPropertyChanged(nameof(EmptyStateVisibility));
            Status = "Loading...";

            try
            {
                RobloxCookie.RobloxAccount? me = await RobloxCookie.GetAccountAsync().ConfigureAwait(true);

                if (me is null)
                {
                    Status = "Sign into Roblox to see your friends list.";
                    _all = new();
                    Friends.Clear();
                    SelectedFriend = null;
                    return;
                }

                _accountId = me.UserId;
                LoadAccounts(me.UserId);

                List<FriendInfo> friends = await FriendsService.GetFriendsAsync(me.UserId).ConfigureAwait(true);

                if (friends.Count == 0)
                {
                    Status = "No friends found (or this account's friends list is private).";
                    _all = new();
                    Friends.Clear();
                    SelectedFriend = null;
                    return;
                }

                Dictionary<long, FriendPresence> presence = await FriendsService.GetPresenceAsync(friends.Select(f => f.UserId)).ConfigureAwait(true);
                PhasmaStrap.Utility.FriendPlaySessions.Observe(presence);
                Dictionary<long, string> avatars = await FriendsService.GetAvatarsAsync(friends.Select(f => f.UserId)).ConfigureAwait(true);

                var rows = friends.Select(f =>
                {
                    presence.TryGetValue(f.UserId, out FriendPresence? p);
                    avatars.TryGetValue(f.UserId, out string? avatar);
                    FriendPresenceType type = p?.Type ?? FriendPresenceType.Offline;

                    string statusText = type switch
                    {
                        FriendPresenceType.InGame => string.IsNullOrEmpty(p?.LastLocation) ? "In a game" : p!.LastLocation,
                        FriendPresenceType.InStudio => "In Roblox Studio",
                        FriendPresenceType.Online => "Online",
                        _ => PhasmaStrap.Utility.FriendPlaySessions.LastSeenText(f.UserId) is { Length: > 0 } seen ? seen : "Offline",
                    };

                    PhasmaStrap.Utility.FriendNotesStore.Entry mine = PhasmaStrap.Utility.FriendNotesStore.Shared.Get(f.UserId);

                    var row = new FriendRow
                    {
                        UserId = f.UserId,
                        Username = f.Username ?? "",
                        Name = string.IsNullOrWhiteSpace(f.DisplayName) ? f.Username : f.DisplayName,
                        AvatarUrl = AvatarCache.TryGetFresh(f.UserId) ?? avatar,
                        Type = type,
                        StatusText = statusText,
                        Joinable = p?.Joinable ?? false,
                        Presence = p,
                    };

                    row.LoadNote(mine.Favourite, mine.Note);
                    row.LoadAlerts(mine.Alerts);
                    row.FavouriteChanged = _ => ApplyView();
                    return row;
                })
                .ToList();

                _sessions = SessionStore.Shared.Load();
                _all = rows;
                ApplyView();
                BuildHistory(_selectedFriend);

                _ = CacheAvatarsAsync(rows, avatars);
                _ = FillFriendsSinceAsync(me.UserId, rows);

                int online = rows.Count(r => r.Type != FriendPresenceType.Offline);
                int starred = rows.Count(r => r.IsFavourite);
                Status = $"{online} of {rows.Count} friend(s) online." + (starred > 0 ? $"  {starred} favourite(s)." : "");
            }
            catch (Exception ex)
            {
                Status = $"Error: {ex.Message}";
                App.Logger.WriteLine(LOG_IDENT, $"RefreshAsync failed: {ex.Message}");
            }
            finally
            {
                Loading = false;
                OnPropertyChanged(nameof(HasFriends));
                OnPropertyChanged(nameof(EmptyStateVisibility));
            }
        }

        // ---- Friend details: history together, friends since, same server

        private SessionData _sessions = new();
        private long _accountId;

        private static string WhenText(DateTime utc)
        {
            DateTime local = utc.ToLocalTime().Date, today = DateTime.Now.Date;
            int days = (int)(today - local).TotalDays;

            return days switch
            {
                <= 0 => "Today",
                1 => "Yesterday",
                < 7 => $"{days} days ago",
                < 14 => "Last week",
                _ => local.Year == today.Year ? local.ToString("d MMMM") : local.ToString("d MMMM yyyy"),
            };
        }

        private static string InSentence(string when) => when is "Today" or "Yesterday" or "Last week" ? when.ToLowerInvariant() : when;

        private static string Initials(string name)
        {
            string[] words = name.Split(new[] { ' ', '-', ':', '_' }, StringSplitOptions.RemoveEmptyEntries);
            string letters = string.Concat(words.Take(2).Select(w => char.ToUpperInvariant(w[0])));
            return letters.Length > 0 ? letters : "?";
        }

        private void BuildHistory(FriendRow? row)
        {
            if (row is null)
                return;

            try
            {
                List<SessionStats.GameTotal> games = SessionStats.GamesWith(_sessions, row.UserId, 4);
                List<ServerVisit> visits = SessionStats.VisitsWith(_sessions, row.UserId);

                var tiles = games.Select(g => new PlayedTogetherTile
                {
                    Name = g.Name,
                    IconUrl = g.IconUrl.Length > 0 ? g.IconUrl : null,
                    When = WhenText(g.LastPlayedUtc),
                    Initials = Initials(g.Name),
                    ToolTip = $"{g.Name}: {g.Visits} time{(g.Visits == 1 ? "" : "s")}, {SessionStats.Duration(g.Minutes)} together",
                }).ToList();

                bool tracking = App.Settings.Prop.SessionTrackFriends && App.Settings.Prop.SessionHistoryEnabled;
                string emptyText = tracking
                    ? $"No games with {row.Name} yet. They show up here after you share a server."
                    : "Turn on \"Note which friends share my server\" on the Activity page to see games you played together.";

                DateTime monthAgo = DateTime.UtcNow.AddDays(-30);
                List<ServerVisit> recent = visits.Where(v => v.JoinedUtc >= monthAgo).ToList();
                string ranInto;

                if (recent.Count > 0)
                {
                    List<string> top = recent
                        .GroupBy(v => v.UniverseId > 0 ? $"u{v.UniverseId}" : $"p{v.PlaceId}")
                        .OrderByDescending(g => g.Count())
                        .Select(g => g.FirstOrDefault(v => v.GameName.Length > 0)?.GameName ?? $"Place {g.First().PlaceId}")
                        .Take(2)
                        .ToList();

                    string times = recent.Count == 1 ? "once" : $"{recent.Count} times";
                    string where = top.Count == 1 ? (recent.Count == 1 ? $"in {top[0]}" : $"all in {top[0]}") : $"mostly {top[0]} and {top[1]}";
                    ServerVisit last = recent[0];
                    ranInto = $"Same server {times} in the last month, {where}. Last time was {InSentence(WhenText(last.JoinedUtc))}.";
                }
                else if (visits.Count > 0)
                {
                    ServerVisit last = visits[0];
                    string game = last.GameName.Length > 0 ? last.GameName : $"Place {last.PlaceId}";
                    ranInto = $"Not in the last month. The last time was {last.JoinedUtc.ToLocalTime():d MMMM yyyy}, in {game}.";
                }
                else
                {
                    ranInto = tracking
                        ? "You haven't been on the same server yet, as far as this PC knows."
                        : "Nothing counted yet. This PC only notes it while \"Note which friends share my server\" is on.";
                }

                row.SetHistory(tiles, emptyText, ranInto);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not read the history with {row.Name}: {ex.Message}");
            }
        }

        private async Task FillFriendsSinceAsync(long accountId, List<FriendRow> rows)
        {
            try
            {
                List<(long UserId, DateTime FirstSeenUtc)> added = PhasmaStrap.Utility.FriendHistoryStore.Shared.Observe(accountId, rows.Select(r => r.UserId));

                if (added.Count > 0)
                    _ = PhasmaStrap.Utility.Backend.SocialApi.ReportNewFriendsAsync(accountId, added);

                foreach (FriendRow row in rows)
                    row.FriendsSinceUtc = PhasmaStrap.Utility.FriendHistoryStore.Shared.KnownSince(accountId, row.UserId);

                // Roblox doesn't say when a friendship started, so ask PhasmaStrap's server for the rest
                Dictionary<long, DateTime> remote = await PhasmaStrap.Utility.Backend.SocialApi.GetFriendsSinceAsync(
                    accountId,
                    rows.OrderByDescending(r => r.IsFavourite).ThenBy(r => r.GroupRank).Select(r => r.UserId)).ConfigureAwait(true);

                foreach (FriendRow row in rows)
                {
                    if (remote.TryGetValue(row.UserId, out DateTime since) && (row.FriendsSinceUtc is null || since < row.FriendsSinceUtc))
                        row.FriendsSinceUtc = since;
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not work out friends since dates: {ex.Message}");
            }
        }

        // ---- Right now: the friend's server (region, players, ping)

        private static readonly Dictionary<string, (DateTime At, string Text, string ToolTip, string? Icon)> _serverInfo = new();

        /// <summary>
        /// Fills "Frankfurt · 9 of 12 players · 24 ms from you" for the friend's server: players from Roblox's public server list,
        /// region from your own session history or PhasmaStrap's server (GamesApi.FactsAsync), ping estimated from your distance to
        /// that datacenter the same way the server browser does. Unknown parts are left out.
        /// </summary>
        private static async Task LoadServerAsync(FriendRow? row)
        {
            if (row is null)
                return;

            row.ServerText = "";
            row.ServerToolTip = "";

            if (row.Presence is not { Type: FriendPresenceType.InGame } p)
                return;

            long placeId = p.PlaceId > 0 ? p.PlaceId : p.RootPlaceId;
            long iconPlaceId = p.RootPlaceId > 0 ? p.RootPlaceId : placeId;

            if (placeId <= 0)
                return;

            string key = $"{placeId}:{p.GameId}";

            if (_serverInfo.TryGetValue(key, out var cached) && DateTime.UtcNow - cached.At < TimeSpan.FromMinutes(2))
            {
                row.ServerText = cached.Text;
                row.ServerToolTip = cached.ToolTip;
                row.GameIconUrl = cached.Icon;
                return;
            }

            string? icon = null;
            var parts = new List<string>();
            string toolTip = "";

            try
            {
                Dictionary<long, string> icons = await GameCatalog.GetPlaceIconsAsync(new[] { iconPlaceId });
                if (icons.TryGetValue(iconPlaceId, out string? found))
                {
                    icon = found;
                    row.GameIconUrl = icon;
                }

                if (!string.IsNullOrEmpty(p.GameId))
                {
                    List<ServerListItem> servers = await ServerBrowser.ListPublicServersAsync(placeId);
                    ServerListItem server = servers.FirstOrDefault(s => s.JobId.Equals(p.GameId, StringComparison.OrdinalIgnoreCase))
                        ?? new ServerListItem { JobId = p.GameId };

                    Dictionary<string, PhasmaStrap.Utility.Backend.ServerStats> facts =
                        await PhasmaStrap.Utility.Backend.GamesApi.FactsAsync(placeId, new[] { server });

                    string city = "", country = "";
                    if (facts.TryGetValue(server.JobId, out PhasmaStrap.Utility.Backend.ServerStats? stats))
                    {
                        city = stats.City;
                        country = stats.Country;
                    }

                    if (city.Length > 0)
                        parts.Add(city);

                    if (server.MaxPlayers > 0)
                        parts.Add($"{server.Playing} of {server.MaxPlayers} players");

                    int estimate = await EstimatePingAsync(city, country);
                    if (estimate > 0)
                    {
                        parts.Add($"{estimate} ms from you");
                        toolTip = "Ping is estimated from your distance to that datacenter.";
                    }
                    else if (server.Ping > 0)
                    {
                        parts.Add($"{server.Ping} ms");
                        toolTip = "Average ping of the players on that server, as Roblox reports it.";
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not look up {row.Name}'s server: {ex.Message}");
            }

            string text = string.Join(" · ", parts);
            _serverInfo[key] = (DateTime.UtcNow, text, toolTip, icon);

            row.ServerText = text;
            row.ServerToolTip = toolTip;
        }

        /// <summary>Same estimate as the server browser's datacenter list: your distance to the datacenter. -1 when unknown.</summary>
        private static async Task<int> EstimatePingAsync(string city, string country)
        {
            if (city.Length == 0)
                return -1;

            try
            {
                RobloxDatacenter? dc = RobloxDatacenterMap.AllDatacenters()
                    .Where(d => d.City.Equals(city, StringComparison.OrdinalIgnoreCase) && (d.Lat != 0.0 || d.Lon != 0.0))
                    .OrderByDescending(d => country.Length > 0 && PhasmaStrap.Utility.ServerFacts.CountryCode(d.Country).Equals(country, StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault();

                if (dc is null)
                    return -1;

                UserGeo? geo = await Matchmaker.GetUserGeoAsync();
                if (geo is null)
                    return -1;

                return Matchmaker.EstimatePingMs(Matchmaker.HaversineKm(geo.Lat, geo.Lon, dc.Lat, dc.Lon));
            }
            catch
            {
                return -1;
            }
        }

        private System.Windows.Threading.DispatcherTimer? _playingForTimer;

        public ICommand SameServerCommand { get; }

        /// <summary>Joins the exact server the friend is in, not just their game.</summary>
        private static async Task SameServerAsync(FriendRow? row)
        {
            if (row is null)
                return;

            FriendPresence? now = await FreshPresenceAsync(row);

            if (now is null || now.Type != FriendPresenceType.InGame)
            {
                Frontend.ShowMessageBox($"{row.Name} is not in a game any more.", MessageBoxImage.Information);
                return;
            }

            long placeId = now.PlaceId > 0 ? now.PlaceId : now.RootPlaceId;

            if (placeId <= 0 || string.IsNullOrEmpty(now.GameId))
            {
                Frontend.ShowMessageBox(
                    $"{row.Name} is in a game, but Roblox is not saying which server. That happens when their joins are set to friends only or off, or when the game blocks joining.",
                    MessageBoxImage.Information);
                return;
            }

            if (!RobloxLaunch.Join(placeId, now.GameId))
                Frontend.ShowMessageBox("Could not start Roblox.", MessageBoxImage.Error);
        }

        // ---- Right rail: party and saved accounts

        public ICommand StartPartyCommand { get; }
        public ICommand JoinPartyCommand { get; }
        public ICommand OpenPartyCommand { get; }
        public ICommand OpenAccountsCommand { get; }
        public ICommand OpenActivityCommand { get; }

        private static void Go(Type page)
        {
            Application.Current?.Windows.OfType<PhasmaStrap.UI.Elements.Settings.MainWindow>().FirstOrDefault()?.Navigate(page);
        }

        private bool PartyUsable => App.Settings.Prop.PartyEnabled && PhasmaStrap.Utility.PhasmaAccount.SignedIn;

        public string PartyTagText
        {
            get
            {
                if (!App.Settings.Prop.PartyEnabled)
                    return "Off";

                if (!PhasmaStrap.Utility.PhasmaAccount.SignedIn)
                    return "Signed out";

                if (!PhasmaStrap.Utility.PartyService.InParty)
                    return "Not in one";

                return PhasmaStrap.Utility.PartyService.IsLeader ? "Leading" : "In a party";
            }
        }

        public string PartyText
        {
            get
            {
                if (!App.Settings.Prop.PartyEnabled)
                    return "Parties are turned off. Turn them on in the Party tab.";

                if (!PhasmaStrap.Utility.PhasmaAccount.SignedIn)
                    return "Sign in to your PhasmaStrap account to start or join a party.";

                if (!PhasmaStrap.Utility.PartyService.InParty)
                    return "Everyone in your party follows you into whatever you launch.";

                PhasmaStrap.Utility.PartyState party = PhasmaStrap.Utility.PartyService.Current;
                int count = party.Members.Count;
                string members = $"{count} member{(count == 1 ? "" : "s")}";

                return PhasmaStrap.Utility.PartyService.IsLeader
                    ? $"You lead, {members}. Everyone follows you into what you launch."
                    : $"{(string.IsNullOrEmpty(party.LeaderName) ? "Someone" : party.LeaderName)} leads, {members}. You follow them in.";
            }
        }

        public string PartyCode => PhasmaStrap.Utility.PartyService.Current.Code;

        public Visibility PartyStartVisibility => PartyUsable && !PhasmaStrap.Utility.PartyService.InParty ? Visibility.Visible : Visibility.Collapsed;
        public Visibility PartyInVisibility => PartyUsable && PhasmaStrap.Utility.PartyService.InParty ? Visibility.Visible : Visibility.Collapsed;
        /// <summary>Signed out or parties off: only a way to the Party tab.</summary>
        public Visibility PartyOpenVisibility => PartyUsable ? Visibility.Collapsed : Visibility.Visible;

        private string _partyJoinCode = "";
        public string PartyJoinCode
        {
            get => _partyJoinCode;
            set { _partyJoinCode = (value ?? "").Trim().ToUpperInvariant(); OnPropertyChanged(nameof(PartyJoinCode)); }
        }

        private string _partyMessage = "";
        public string PartyMessage
        {
            get => _partyMessage;
            private set { _partyMessage = value; OnPropertyChanged(nameof(PartyMessage)); OnPropertyChanged(nameof(PartyMessageVisibility)); }
        }

        public Visibility PartyMessageVisibility => _partyMessage.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        private async Task StartPartyAsync()
        {
            PartyMessage = "";
            PhasmaStrap.Utility.PartyService.Start();
            await PhasmaStrap.Utility.PartyService.CreateAsync();

            if (!PhasmaStrap.Utility.PartyService.InParty)
                PartyMessage = "Could not start a party right now.";
        }

        private async Task JoinPartyAsync()
        {
            if (_partyJoinCode.Length == 0)
            {
                PartyMessage = "Type the code someone gave you first.";
                return;
            }

            PartyMessage = "";
            PhasmaStrap.Utility.PartyService.Start();

            if (await PhasmaStrap.Utility.PartyService.JoinAsync(_partyJoinCode))
                PartyJoinCode = "";
            else
                PartyMessage = "That code did not work. Check it and try again.";
        }

        private void RaisePartyChanged()
        {
            OnPropertyChanged(nameof(PartyTagText));
            OnPropertyChanged(nameof(PartyText));
            OnPropertyChanged(nameof(PartyCode));
            OnPropertyChanged(nameof(PartyStartVisibility));
            OnPropertyChanged(nameof(PartyInVisibility));
            OnPropertyChanged(nameof(PartyOpenVisibility));
            OnPropertyChanged(nameof(PartyInviteVisibility));
        }

        private void OnPartyChanged(object? sender, EventArgs e) => Application.Current?.Dispatcher.BeginInvoke(new Action(RaisePartyChanged));

        /// <summary>Called when the page shows, so the party card follows the party live.</summary>
        public void Attach()
        {
            PhasmaStrap.Utility.PartyService.Changed -= OnPartyChanged;
            PhasmaStrap.Utility.PartyService.Changed += OnPartyChanged;
            PhasmaStrap.Utility.PhasmaAccount.Changed -= OnPartyChanged;
            PhasmaStrap.Utility.PhasmaAccount.Changed += OnPartyChanged;

            if (PartyUsable)
            {
                PhasmaStrap.Utility.PartyService.Start();
                _ = PhasmaStrap.Utility.PartyService.RefreshAsync();
            }

            RaisePartyChanged();

            if (_accountId > 0)
                LoadAccounts(_accountId);

            // Keeps "Playing for 42 minutes" ticking while the page is open
            _playingForTimer ??= new System.Windows.Threading.DispatcherTimer(TimeSpan.FromSeconds(30), System.Windows.Threading.DispatcherPriority.Background,
                (_, _) => _selectedFriend?.RefreshPlayingFor(), Application.Current.Dispatcher);
            _playingForTimer.Start();
        }

        public void Detach()
        {
            _playingForTimer?.Stop();

            PhasmaStrap.Utility.PartyService.Changed -= OnPartyChanged;
            PhasmaStrap.Utility.PhasmaAccount.Changed -= OnPartyChanged;
        }

        public ObservableCollection<AccountSummaryRow> SavedAccounts { get; } = new();

        private int _savedAccountCount;
        public string AccountsMoreText => _savedAccountCount > SavedAccounts.Count ? $"and {_savedAccountCount - SavedAccounts.Count} more" : "";
        public Visibility AccountsMoreVisibility => _savedAccountCount > SavedAccounts.Count ? Visibility.Visible : Visibility.Collapsed;
        public Visibility AccountsEmptyVisibility => SavedAccounts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        private sealed class SavedAccountMeta
        {
            public long UserId { get; set; }
            public string Username { get; set; } = "";
            public string DisplayName { get; set; } = "";
            public string Note { get; set; } = "";
            public string DatFile { get; set; } = "";
            public DateTime LastUsedUtc { get; set; }
        }

        /// <summary>Reads the account switcher's saved list (read only) for the Accounts card.</summary>
        private void LoadAccounts(long currentUserId)
        {
            SavedAccounts.Clear();
            _savedAccountCount = 0;

            try
            {
                string folder = Paths.AccountBackups;
                string meta = Path.Combine(folder, "accounts.json");

                if (File.Exists(meta))
                {
                    List<SavedAccountMeta> saved = (JsonSerializer.Deserialize<List<SavedAccountMeta>>(File.ReadAllText(meta)) ?? new())
                        .Where(a => a.UserId > 0 && !string.IsNullOrEmpty(a.DatFile) && File.Exists(Path.Combine(folder, a.DatFile)))
                        .OrderByDescending(a => a.UserId == currentUserId)
                        .ThenByDescending(a => a.LastUsedUtc)
                        .ToList();

                    _savedAccountCount = saved.Count;

                    foreach (SavedAccountMeta account in saved.Take(3))
                    {
                        bool current = account.UserId == currentUserId;
                        string note = account.Note.Trim();
                        string detail = current
                            ? (note.Length > 0 ? $"{note} · in use" : "In use")
                            : (note.Length > 0 ? note : $"@{account.Username}");

                        SavedAccounts.Add(new AccountSummaryRow
                        {
                            Name = string.IsNullOrWhiteSpace(account.DisplayName) ? account.Username : account.DisplayName,
                            Detail = detail,
                            AvatarUrl = AvatarCache.TryGetFresh(account.UserId),
                            IsCurrent = current,
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not read the saved accounts: {ex.Message}");
            }

            OnPropertyChanged(nameof(AccountsMoreText));
            OnPropertyChanged(nameof(AccountsMoreVisibility));
            OnPropertyChanged(nameof(AccountsEmptyVisibility));
        }

        public string[] AlertModeOptions => FriendRow.AlertLabels;

        private static async Task JoinAsync(FriendRow? row)
        {
            if (row is null)
                return;

            FriendPresence? now = await FreshPresenceAsync(row);

            if (now is null || now.Type != FriendPresenceType.InGame)
            {
                Frontend.ShowMessageBox($"{row.Name} is not in a game any more.", MessageBoxImage.Information);
                return;
            }

            if (!now.Joinable)
            {
                Frontend.ShowMessageBox(
                    $"{row.Name} is in a game, but Roblox is not saying which server. That happens when their joins are set to friends only or off, or when the game blocks joining.",
                    MessageBoxImage.Information);
                return;
            }

            try
            {
                Process.Start(Paths.Process, $"-player \"{FriendsService.GetJoinDeeplink(now)}\"");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Join failed: {ex.Message}");
                Frontend.ShowMessageBox($"Could not start Roblox: {ex.Message}", MessageBoxImage.Error);
            }
        }

        private static async Task<FriendPresence?> FreshPresenceAsync(FriendRow row)
        {
            try
            {
                Dictionary<long, FriendPresence> presence = await FriendsService.GetPresenceAsync(new[] { row.UserId });
                PhasmaStrap.Utility.FriendPlaySessions.Observe(presence);
                return presence.TryGetValue(row.UserId, out FriendPresence? found) ? found : row.Presence;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not re-check where {row.Name} is: {ex.Message}");
                return row.Presence;
            }
        }
    }
}
