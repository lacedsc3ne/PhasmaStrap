using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

using CommunityToolkit.Mvvm.Input;

using PhasmaStrap.Integrations;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public sealed class PrivateServerRow : NotifyPropertyChangedViewModel
    {
        public PrivateServerInfo Server { get; init; } = null!;

        public string Title => Server.GameName.Length > 0 ? Server.GameName : Server.Name.Length > 0 ? Server.Name : "Private server";

        public string Subtitle
        {
            get
            {
                var parts = new List<string>();
                parts.Add(Server.Name.Length > 0 ? $"Server \"{Server.Name}\"" : "Private server");
                if (!Server.Owned && Server.OwnerName.Length > 0)
                    parts.Add($"owned by {Server.OwnerName}");
                return string.Join("  ·  ", parts);
            }
        }

        public string StatusText
        {
            get
            {
                if (!Server.Active)
                    return "Inactive";
                if (Server.Expires is not DateTime expires)
                    return Server.Owned ? "Free" : "";

                if (expires > DateTime.UtcNow.AddYears(20))
                    return "Doesn't expire";

                string when = expires.ToLocalTime().ToString("d MMM yyyy");
                return Server.WillRenew ? $"Renews {when}" : $"Ends {when}";
            }
        }

        private string? _icon;
        public string? IconUrl { get => _icon; set { _icon = value; OnPropertyChanged(nameof(IconUrl)); } }

        private bool _isFriend;
        /// <summary>The owner is one of your Roblox friends.</summary>
        public bool IsFriend
        {
            get => _isFriend;
            set { if (_isFriend == value) return; _isFriend = value; OnPropertyChanged(nameof(IsFriend)); OnPropertyChanged(nameof(FriendVisibility)); }
        }

        public Visibility FriendVisibility => _isFriend && !Server.Owned ? Visibility.Visible : Visibility.Collapsed;

        private bool _isHidden;
        /// <summary>Hidden from the list on this PC. Only shown while "Show hidden" is on.</summary>
        public bool IsHidden
        {
            get => _isHidden;
            set { if (_isHidden == value) return; _isHidden = value; OnPropertyChanged(nameof(IsHidden)); OnPropertyChanged(nameof(HiddenVisibility)); }
        }

        public Visibility HiddenVisibility => _isHidden ? Visibility.Visible : Visibility.Collapsed;

        public Visibility OwnerVisibility => Server.Owned ? Visibility.Visible : Visibility.Collapsed;

        public bool CanJoin => Server.Active && Server.PlaceId > 0;

        public DateTime? EndsAt => Server.Expires is DateTime e && e < DateTime.UtcNow.AddYears(20) ? e : null;

        /// <summary>The server's own name, shown as the row title.</summary>
        public string ServerName => Server.Name.Length > 0 ? Server.Name : "Private server";

        /// <summary>The game and whose server it is, under the title.</summary>
        public string GameLine
        {
            get
            {
                string owner = Server.Owned ? "You" : Server.OwnerName;
                return owner.Length > 0 ? $"{Title}  ·  {owner}" : Title;
            }
        }

        /// <summary>Short state for the tag: inactive, ending soon, or open.</summary>
        public string TagText
        {
            get
            {
                if (!Server.Active)
                    return "Inactive";

                if (EndsAt is DateTime ends && !Server.WillRenew)
                {
                    int days = (int)Math.Ceiling((ends - DateTime.UtcNow).TotalDays);
                    if (days >= 0 && days <= 30)
                        return days == 1 ? "Ends in 1 day" : $"Ends in {days} days";
                }

                return CanJoin ? "Can join now" : "";
            }
        }
    }

    public sealed record PrivateServerFilterOption(string Label, long Id)
    {
        public override string ToString() => Label;
    }

    public sealed class PrivateServersViewModel : NotifyPropertyChangedViewModel
    {
        public ObservableCollection<PrivateServerRow> Owned { get; } = new();

        public ObservableCollection<PrivateServerRow> Shared { get; } = new();

        private string _status = "Shows the private servers of the Roblox account signed in on this PC. Nothing is fetched until you press Load.";
        public string Status { get => _status; private set { _status = value; OnPropertyChanged(nameof(Status)); } }

        private bool _busy;
        public bool IsBusy { get => _busy; private set { _busy = value; OnPropertyChanged(nameof(IsBusy)); OnPropertyChanged(nameof(NotBusy)); } }
        public bool NotBusy => !_busy;

        private bool _loaded;
        public Visibility ListsVisibility => _loaded ? Visibility.Visible : Visibility.Collapsed;
        public Visibility NotLoadedVisibility => _loaded ? Visibility.Collapsed : Visibility.Visible;
        public Visibility NoOwnedVisibility => _loaded && !_all.Any(r => r.Server.Owned) ? Visibility.Visible : Visibility.Collapsed;
        public Visibility NoSharedVisibility => _loaded && !_all.Any(r => !r.Server.Owned) ? Visibility.Visible : Visibility.Collapsed;

        private List<PrivateServerRow> _all = new();
        private HashSet<long> _friendIds = new();
        private HashSet<long> _hidden = PhasmaStrap.Utility.GamesStore.Shared.HiddenPrivateServers();

        private bool _showHidden;
        /// <summary>Also lists the servers you hid, marked as hidden.</summary>
        public bool ShowHidden
        {
            get => _showHidden;
            set { if (_showHidden == value) return; _showHidden = value; OnPropertyChanged(nameof(ShowHidden)); ApplyFilters(); }
        }

        public int HiddenCount => _all.Count(r => r.IsHidden);

        public string HiddenText => HiddenCount == 1 ? "1 hidden" : $"{HiddenCount} hidden";

        public Visibility HiddenToggleVisibility => _loaded && (HiddenCount > 0 || _showHidden) ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Hides a server from this list on this PC, or brings it back.</summary>
        public void SetHidden(PrivateServerRow? row, bool hidden)
        {
            if (row is null)
                return;

            PhasmaStrap.Utility.GamesStore.Shared.SetPrivateServerHidden(row.Server.Id, hidden);
            _hidden = PhasmaStrap.Utility.GamesStore.Shared.HiddenPrivateServers();
            row.IsHidden = hidden;

            if (!hidden && HiddenCount == 0)
                _showHidden = false;

            OnPropertyChanged(nameof(ShowHidden));
            ApplyFilters();
        }

        public ICommand HideCommand => new RelayCommand<PrivateServerRow>(row => SetHidden(row, true));

        public ICommand UnhideCommand => new RelayCommand<PrivateServerRow>(row => SetHidden(row, false));

        public ICommand ShowAllHiddenCommand => new RelayCommand(() =>
        {
            PhasmaStrap.Utility.GamesStore.Shared.ShowAllPrivateServers();
            _hidden = new HashSet<long>();
            foreach (PrivateServerRow row in _all)
                row.IsHidden = false;

            _showHidden = false;
            OnPropertyChanged(nameof(ShowHidden));
            ApplyFilters();
        });

        public const long AnyOwner = 0;
        public const long FriendsOnly = -1;

        public ObservableCollection<PrivateServerFilterOption> OwnerOptions { get; } = new();
        public ObservableCollection<PrivateServerFilterOption> GameOptions { get; } = new();

        private string _search = "";
        public string Search { get => _search; set { if (_search == value) return; _search = value; OnPropertyChanged(nameof(Search)); ApplyFilters(); } }

        private PrivateServerFilterOption? _owner;
        public PrivateServerFilterOption? Owner { get => _owner; set { if (Equals(_owner, value) || value is null) return; _owner = value; OnPropertyChanged(nameof(Owner)); ApplyFilters(); } }

        private PrivateServerFilterOption? _game;
        public PrivateServerFilterOption? Game { get => _game; set { if (Equals(_game, value) || value is null) return; _game = value; OnPropertyChanged(nameof(Game)); ApplyFilters(); } }

        private int _showIndex;
        public int ShowIndex { get => _showIndex; set { if (_showIndex == value) return; _showIndex = value; OnPropertyChanged(nameof(ShowIndex)); ApplyFilters(); } }

        private int _statusIndex;
        public int StatusIndex { get => _statusIndex; set { if (_statusIndex == value) return; _statusIndex = value; OnPropertyChanged(nameof(StatusIndex)); ApplyFilters(); } }

        private int _sortIndex;
        public int SortIndex { get => _sortIndex; set { if (_sortIndex == value) return; _sortIndex = value; OnPropertyChanged(nameof(SortIndex)); ApplyFilters(); } }

        private string _filterSummary = "";
        public string FilterSummary { get => _filterSummary; private set { _filterSummary = value; OnPropertyChanged(nameof(FilterSummary)); } }

        public Visibility YoursVisibility => ShowIndex != 2 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility SharedVisibility => ShowIndex != 1 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility NoMatchesVisibility => _loaded && _all.Count > 0 && Owned.Count + Shared.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        public ICommand ClearFiltersCommand => new RelayCommand(() =>
        {
            _search = "";
            _owner = OwnerOptions.FirstOrDefault();
            _game = GameOptions.FirstOrDefault();
            _showIndex = _statusIndex = _sortIndex = 0;
            OnPropertyChanged(nameof(Search));
            OnPropertyChanged(nameof(Owner));
            OnPropertyChanged(nameof(Game));
            OnPropertyChanged(nameof(ShowIndex));
            OnPropertyChanged(nameof(StatusIndex));
            OnPropertyChanged(nameof(SortIndex));
            ApplyFilters();
        });

        private bool Matches(PrivateServerRow row)
        {
            PrivateServerInfo s = row.Server;

            if (row.IsHidden && !_showHidden)
                return false;

            string search = _search.Trim();
            if (search.Length > 0
                && !s.GameName.Contains(search, StringComparison.OrdinalIgnoreCase)
                && !s.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                && !s.OwnerName.Contains(search, StringComparison.OrdinalIgnoreCase))
                return false;

            long owner = _owner?.Id ?? AnyOwner;
            if (owner == FriendsOnly && (s.Owned || !_friendIds.Contains(s.OwnerId)))
                return false;
            if (owner > 0 && (s.Owned || s.OwnerId != owner))
                return false;

            if (_game is { Id: > 0 } game && s.UniverseId != game.Id)
                return false;

            return _statusIndex switch
            {
                1 => row.CanJoin,
                2 => !s.Active,
                3 => s.Active && row.EndsAt is DateTime ends && ends < DateTime.UtcNow.AddDays(30),
                _ => true,
            };
        }

        private void ApplyFilters()
        {
            IEnumerable<PrivateServerRow> rows = _all.Where(Matches);

            rows = _sortIndex switch
            {
                1 => rows.OrderBy(r => r.Server.Owned ? "" : r.Server.OwnerName, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase),
                2 => rows.OrderBy(r => r.EndsAt ?? DateTime.MaxValue).ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase),
                _ => rows.OrderByDescending(r => r.Server.Active).ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase),
            };

            Owned.Clear();
            Shared.Clear();
            foreach (PrivateServerRow row in rows)
            {
                if (row.Server.Owned && _showIndex != 2)
                    Owned.Add(row);
                else if (!row.Server.Owned && _showIndex != 1)
                    Shared.Add(row);
            }

            int shown = Owned.Count + Shared.Count;
            FilterSummary = shown == _all.Count ? $"Showing all {_all.Count}." : $"Showing {shown} of {_all.Count}.";

            OnPropertyChanged(nameof(HiddenCount));
            OnPropertyChanged(nameof(HiddenText));
            OnPropertyChanged(nameof(HiddenToggleVisibility));

            OnPropertyChanged(nameof(YoursVisibility));
            OnPropertyChanged(nameof(SharedVisibility));
            OnPropertyChanged(nameof(NoMatchesVisibility));
        }

        private void BuildFilterOptions()
        {
            long ownerId = _owner?.Id ?? AnyOwner;
            long gameId = _game?.Id ?? 0;

            OwnerOptions.Clear();
            OwnerOptions.Add(new PrivateServerFilterOption("Anyone", AnyOwner));

            var owners = _all.Where(r => !r.Server.Owned && r.Server.OwnerId > 0)
                .GroupBy(r => r.Server.OwnerId)
                .Select(g => (Id: g.Key, Name: g.First().Server.OwnerName, Count: g.Count(), Friend: _friendIds.Contains(g.Key)))
                .OrderByDescending(o => o.Friend).ThenByDescending(o => o.Count).ThenBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            int friendCount = owners.Count(o => o.Friend);
            if (friendCount > 0)
                OwnerOptions.Add(new PrivateServerFilterOption($"Friends only ({owners.Where(o => o.Friend).Sum(o => o.Count)})", FriendsOnly));

            foreach (var o in owners)
                OwnerOptions.Add(new PrivateServerFilterOption($"{(o.Name.Length > 0 ? o.Name : o.Id.ToString())}{(o.Friend ? "  · friend" : "")}  ({o.Count})", o.Id));

            GameOptions.Clear();
            GameOptions.Add(new PrivateServerFilterOption("All games", 0));
            foreach (var g in _all.Where(r => r.Server.UniverseId > 0).GroupBy(r => r.Server.UniverseId)
                         .Select(g => (Id: g.Key, Name: g.First().Title, Count: g.Count()))
                         .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
                GameOptions.Add(new PrivateServerFilterOption($"{g.Name}  ({g.Count})", g.Id));

            _owner = OwnerOptions.FirstOrDefault(o => o.Id == ownerId) ?? OwnerOptions[0];
            _game = GameOptions.FirstOrDefault(o => o.Id == gameId) ?? GameOptions[0];
            OnPropertyChanged(nameof(Owner));
            OnPropertyChanged(nameof(Game));
        }

        private async Task LoadFriendsAsync()
        {
            try
            {
                RobloxCookie.RobloxAccount? me = await RobloxCookie.GetAccountAsync();
                if (me is null)
                    return;

                List<FriendInfo> friends = await FriendsService.GetFriendsAsync(me.UserId);
                _friendIds = friends.Select(f => f.UserId).ToHashSet();
                foreach (PrivateServerRow row in _all)
                    row.IsFriend = !row.Server.Owned && _friendIds.Contains(row.Server.OwnerId);
                BuildFilterOptions();
                ApplyFilters();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("PrivateServersViewModel", $"Friends list not loaded: {ex.Message}");
            }
        }

        public ICommand LoadCommand => new AsyncRelayCommand(LoadAsync);

        private async Task LoadAsync()
        {
            IsBusy = true;
            Status = "Loading...";

            try
            {
                List<PrivateServerInfo> servers = await PrivateServers.ListAsync();

                _hidden = PhasmaStrap.Utility.GamesStore.Shared.HiddenPrivateServers();
                var rows = servers.Select(s => new PrivateServerRow
                {
                    Server = s,
                    IsHidden = _hidden.Contains(s.Id),
                    IsFriend = !s.Owned && _friendIds.Contains(s.OwnerId),
                }).ToList();
                _all = rows;
                _loaded = true;
                BuildFilterOptions();
                ApplyFilters();

                Status = servers.Count == 0 ? "This account has no private servers." : $"{rows.Count(r => r.Server.Owned)} of your own, {rows.Count(r => !r.Server.Owned)} shared with you.";
                _ = LoadFriendsAsync();

                var games = await PhasmaStrap.Utility.GameLookup.WithIconsAsync(servers.Where(s => s.UniverseId > 0).Select(s => new PhasmaStrap.Utility.GameInfo(s.UniverseId, s.PlaceId, s.GameName)).DistinctBy(g => g.UniverseId).ToList());
                foreach (PrivateServerRow row in rows)
                    row.IconUrl = games.FirstOrDefault(g => g.UniverseId == row.Server.UniverseId)?.IconUrl is { Length: > 0 } icon ? icon : null;
            }
            catch (PrivateServers.NotSignedInException)
            {
                Status = "Roblox isn't signed in on this PC - sign in to Roblox (or pick an account on the Accounts page), then try again.";
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("PrivateServersViewModel::Load", ex);
                Status = $"Couldn't load your private servers: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
                OnPropertyChanged(nameof(ListsVisibility));
                OnPropertyChanged(nameof(NotLoadedVisibility));
                OnPropertyChanged(nameof(NoOwnedVisibility));
                OnPropertyChanged(nameof(NoSharedVisibility));
                OnPropertyChanged(nameof(HiddenToggleVisibility));
            }
        }

        public ICommand JoinCommand => new AsyncRelayCommand<PrivateServerRow>(async row =>
        {
            if (row is null)
                return;

            try
            {
                Status = $"Joining {row.Title}...";
                Status = await PrivateServers.JoinAsync(row.Server)
                    ? $"Starting Roblox in {row.Title}."
                    : $"Roblox didn't give a way into {row.Title} - it may be inactive, or you may no longer have access.";
            }
            catch (Exception ex)
            {
                Status = $"Couldn't join: {ex.Message}";
            }
        });

        public ICommand CopyLinkCommand => new AsyncRelayCommand<PrivateServerRow>(async row =>
        {
            if (row is null)
                return;

            try
            {
                string? link = await PrivateServers.GetLinkAsync(row.Server);
                if (link is null)
                {
                    Status = $"{row.Title} has no invite link right now - use New link to make one.";
                    return;
                }

                PhasmaStrap.Utility.ClipboardShare.CopyText(link);
                Status = $"Invite link for {row.Title} copied. Anyone with it can join.";
            }
            catch (Exception ex)
            {
                Status = $"Couldn't get the link: {ex.Message}";
            }
        });

        public ICommand NewLinkCommand => new AsyncRelayCommand<PrivateServerRow>(async row =>
        {
            if (row is null)
                return;

            var answer = Frontend.ShowMessageBox(
                $"Make a new invite link for \"{row.Title}\"?\n\nThe current link stops working, so anyone you gave it to can't use it to join any more. People already added to the server keep access.",
                MessageBoxImage.Question, MessageBoxButton.YesNo);

            if (answer != MessageBoxResult.Yes)
                return;

            try
            {
                string? link = await PrivateServers.NewLinkAsync(row.Server);
                if (link is null)
                {
                    Status = "Roblox made a new link but didn't send it back - use Copy link.";
                    return;
                }

                PhasmaStrap.Utility.ClipboardShare.CopyText(link);
                Status = $"New invite link for {row.Title} copied. The old one no longer works.";
            }
            catch (Exception ex)
            {
                Status = $"Couldn't make a new link: {ex.Message}";
            }
        });

        public ICommand OpenOnRobloxCommand => new RelayCommand<PrivateServerRow>(row =>
        {
            if (row is not null && row.Server.PlaceId > 0)
                Utilities.ShellExecute($"https://www.roblox.com/games/{row.Server.PlaceId}#!/game-instances");
        });
    }
}
