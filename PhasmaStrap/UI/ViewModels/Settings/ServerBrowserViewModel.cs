using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using PhasmaStrap.Integrations;
using PhasmaStrap.Models;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public class ServerBrowserViewModel : NotifyPropertyChangedViewModel
    {
        private string _placeId = "";
        private bool _isSearching;
        private string _statusText = Strings.Menu_ServerBrowser_Status_EnterPlaceId;

        public string PlaceId
        {
            get => _placeId;
            set { _placeId = value; OnPropertyChanged(nameof(PlaceId)); }
        }

        public bool IsSearching
        {
            get => _isSearching;
            private set { _isSearching = value; OnPropertyChanged(nameof(IsSearching)); }
        }

        public string StatusText
        {
            get => _statusText;
            private set { _statusText = value; OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(HeaderDetail)); }
        }

        public ObservableCollection<ServerListItem> Servers { get; } = new();

        private string _gameTitle = "";
        /// <summary>"Game · Place" for the place last searched, empty until it is known.</summary>
        public string GameTitle
        {
            get => _gameTitle;
            private set { _gameTitle = value; OnPropertyChanged(nameof(GameTitle)); OnPropertyChanged(nameof(HeaderTitle)); }
        }

        private string _gameIconUrl = "";
        public string GameIconUrl
        {
            get => _gameIconUrl;
            private set { _gameIconUrl = value ?? ""; OnPropertyChanged(nameof(GameIconUrl)); OnPropertyChanged(nameof(HasGameIcon)); }
        }

        public bool HasGameIcon => _gameIconUrl.Length > 0;

        public string HeaderTitle => _gameTitle.Length > 0 ? _gameTitle : Strings.Menu_ServerBrowser_Browser_Title;

        private long _searchedPlaceId;

        /// <summary>The place the list below belongs to, which may differ from what is typed in the box.</summary>
        public long SearchedPlaceId => _searchedPlaceId;

        /// <summary>Place ID and how many servers, or the status while searching.</summary>
        public string HeaderDetail => _searchedPlaceId > 0 && !_isSearching && Servers.Count > 0
            ? $"Place ID {_searchedPlaceId}  ·  {GameCatalog.Compact(Servers.Count)} public servers"
            : _statusText;

        public bool HasRegions => Servers.Any(s => s.Region.Length > 0);

        private async Task FillGameAsync(long placeId)
        {
            try
            {
                PhasmaStrap.Utility.GameInfo? game = await PhasmaStrap.Utility.GameLookup.FromPlaceAsync(placeId);
                if (game is null || placeId != _searchedPlaceId)
                    return;

                GameTitle = PhasmaStrap.Utility.PlaceNames.Display(game.Name, placeId);
                GameIconUrl = game.IconUrl;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("ServerBrowserViewModel", $"Game lookup failed: {ex.Message}");
            }
        }

        /// <summary>Adds region, uptime and FPS to the listed servers as they become known.</summary>
        private async Task FillFactsAsync(long placeId)
        {
            try
            {
                List<ServerListItem> listed = Servers.ToList();
                Dictionary<string, PhasmaStrap.Utility.Backend.ServerStats> facts = await PhasmaStrap.Utility.Backend.GamesApi.FactsAsync(placeId, listed);
                if (placeId != _searchedPlaceId || facts.Count == 0)
                    return;

                for (int i = 0; i < Servers.Count; i++)
                {
                    ServerListItem server = Servers[i];
                    if (facts.TryGetValue(server.JobId, out PhasmaStrap.Utility.Backend.ServerStats? stats))
                        Servers[i] = server.WithFacts(stats.Region, stats.FirstSeenUtc, stats.ServerFps ?? 0);
                }

                OnPropertyChanged(nameof(HasRegions));
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("ServerBrowserViewModel", $"Server facts failed: {ex.Message}");
            }
        }

        public ServerBrowserViewModel()
        {
            foreach (DatacenterExclusion exclusion in DatacenterExclusions)
                exclusion.PropertyChanged += (_, _) => OnPropertyChanged(nameof(ExcludedDatacenterSummary));

            _ = LoadDatacenterDistancesAsync();
        }

        public string ExcludedDatacenterSummary
        {
            get
            {
                int count = App.Settings.Prop.MatchmakerDisabledDatacenters.Count;

                if (count == 0)
                    return "No datacenters are excluded.";

                return count == 1
                    ? "1 datacenter is excluded."
                    : $"{count} datacenters are excluded.";
            }
        }

        public bool MatchmakerEnabled
        {
            get => App.Settings.Prop.MatchmakerEnabled;
            set { App.Settings.Prop.MatchmakerEnabled = value; OnPropertyChanged(nameof(MatchmakerEnabled)); }
        }

        public bool MatchmakerPreferEmpty
        {
            get => App.Settings.Prop.MatchmakerPreferEmpty;
            set => App.Settings.Prop.MatchmakerPreferEmpty = value;
        }

        public sealed record DatacenterChoice(string Display, string Key);

        public IEnumerable<DatacenterChoice> DatacenterChoices { get; } =
            new[] { new DatacenterChoice(Strings.Menu_ServerBrowser_ClosestAvailable, "") }
                .Concat(RobloxDatacenterMap.AllDatacenters()
                    .OrderBy(dc => dc.City)
                    .Select(dc => new DatacenterChoice($"{dc.City}, {dc.Country}", Matchmaker.DatacenterKey(dc))));

        public string PreferredDatacenter
        {
            get => App.Settings.Prop.MatchmakerPreferredDatacenter;
            set => App.Settings.Prop.MatchmakerPreferredDatacenter = value ?? "";
        }

        public bool MatchmakerAutoCandidates
        {
            get => App.Settings.Prop.MatchmakerAutoCandidates;
            set { App.Settings.Prop.MatchmakerAutoCandidates = value; OnPropertyChanged(nameof(MatchmakerAutoCandidates)); }
        }

        public int MatchmakerMaxCandidates
        {
            get => App.Settings.Prop.MatchmakerMaxCandidates;
            set => App.Settings.Prop.MatchmakerMaxCandidates = Math.Clamp(value, Matchmaker.MinCandidateCount, Matchmaker.MaxCandidateCount);
        }

        public sealed class DatacenterExclusion : NotifyPropertyChangedViewModel
        {
            public string Display { get; init; } = "";
            public string Key { get; init; } = "";
            public double Lat { get; init; }
            public double Lon { get; init; }

            private double _distanceKm = -1.0;
            private string _pingDisplay = "...";

            public double DistanceKm
            {
                get => _distanceKm;
                set
                {
                    if (_distanceKm == value)
                        return;
                    _distanceKm = value;
                    OnPropertyChanged(nameof(DistanceKm));
                    OnPropertyChanged(nameof(DistanceDisplay));
                }
            }

            public string DistanceDisplay => _distanceKm < 0.0 ? "" : $"{(int)_distanceKm} km away";

            public string PingDisplay
            {
                get => _pingDisplay;
                set
                {
                    if (_pingDisplay == value)
                        return;
                    _pingDisplay = value;
                    OnPropertyChanged(nameof(PingDisplay));
                }
            }

            public bool IsAllowed
            {
                get => !IsBlocked;
                set => IsBlocked = !value;
            }

            public bool IsBlocked
            {
                get => App.Settings.Prop.MatchmakerDisabledDatacenters.Contains(Key);
                set
                {
                    var blocked = App.Settings.Prop.MatchmakerDisabledDatacenters;
                    if (value && !blocked.Contains(Key))
                        blocked.Add(Key);
                    else if (!value)
                        blocked.Remove(Key);

                    OnPropertyChanged(nameof(IsBlocked));
                    OnPropertyChanged(nameof(IsAllowed));
                }
            }
        }

        public ObservableCollection<DatacenterExclusion> DatacenterExclusions { get; } = new(
            RobloxDatacenterMap.AllDatacenters()
                .OrderBy(dc => dc.City)
                .Select(dc => new DatacenterExclusion { Display = $"{dc.City}, {dc.Country}", Key = Matchmaker.DatacenterKey(dc), Lat = dc.Lat, Lon = dc.Lon }));

        private async Task LoadDatacenterDistancesAsync()
        {
            try
            {
                UserGeo? geo = await Matchmaker.GetUserGeoAsync();
                if (geo is null)
                    return;

                foreach (DatacenterExclusion dc in DatacenterExclusions)
                {
                    if (dc.Lat == 0.0 && dc.Lon == 0.0)
                        continue;

                    double km = Matchmaker.HaversineKm(geo.Lat, geo.Lon, dc.Lat, dc.Lon);
                    dc.DistanceKm = km;
                    dc.PingDisplay = $"~{Matchmaker.EstimatePingMs(km)} ms";
                }
            }
            catch
            {
            }
        }

        public sealed record GamejoinApiOption(string Display, int Value);

        public IEnumerable<GamejoinApiOption> GamejoinApiOptions { get; } = new[]
        {
            new GamejoinApiOption("V1 (stable)", 1),
            new GamejoinApiOption("V2 (newer)", 2),
        };

        public int MatchmakerGamejoinApiVersion
        {
            get => App.Settings.Prop.MatchmakerGamejoinApiVersion;
            set => App.Settings.Prop.MatchmakerGamejoinApiVersion = value;
        }

        public ObservableCollection<string> ExcludedPlaces { get; } = new(App.Settings.Prop.MatchmakerExcludedPlaces);

        private string _excludePlaceId = "";

        public string ExcludePlaceId
        {
            get => _excludePlaceId;
            set { _excludePlaceId = value; OnPropertyChanged(nameof(ExcludePlaceId)); }
        }

        public ICommand AddExcludedPlaceCommand => new RelayCommand(() =>
        {
            string id = ExcludePlaceId.Trim();

            if (!long.TryParse(id, out _) || ExcludedPlaces.Contains(id))
                return;

            ExcludedPlaces.Add(id);
            App.Settings.Prop.MatchmakerExcludedPlaces.Add(id);
            ExcludePlaceId = "";
        });

        public ICommand RemoveExcludedPlaceCommand => new RelayCommand<string>(id =>
        {
            if (id is null)
                return;

            ExcludedPlaces.Remove(id);
            App.Settings.Prop.MatchmakerExcludedPlaces.Remove(id);
        });

        public ICommand SearchCommand => new AsyncRelayCommand(SearchAsync);

        public ICommand JoinCommand => new RelayCommand<ServerListItem>(server =>
        {
            if (server is null || !long.TryParse(PlaceId.Trim(), out long placeId))
                return;

            ServerBrowser.JoinServer(placeId, server.JobId);
        });

        public ICommand JoinFastestCommand => new RelayCommand(() =>
        {
            if (!long.TryParse(PlaceId.Trim(), out long placeId))
                return;

            ServerListItem? fastest = Servers.Where(s => s.Ping > 0).OrderBy(s => s.Ping).FirstOrDefault();
            if (fastest is null)
                return;

            ServerBrowser.JoinServer(placeId, fastest.JobId);
        });

        public bool HasPingedServers => Servers.Any(s => s.Ping > 0);

        public bool AutoRejoinOnCrash
        {
            get => App.Settings.Prop.AutoRejoinOnCrash;
            set => App.Settings.Prop.AutoRejoinOnCrash = value;
        }

        public int AutoRejoinMaxAttempts
        {
            get => App.Settings.Prop.AutoRejoinMaxAttempts;
            set => App.Settings.Prop.AutoRejoinMaxAttempts = value;
        }

        public int AutoRejoinDelaySeconds
        {
            get => App.Settings.Prop.AutoRejoinDelaySeconds;
            set => App.Settings.Prop.AutoRejoinDelaySeconds = value;
        }

        private async Task SearchAsync()
        {
            if (!long.TryParse(PlaceId.Trim(), out long placeId) || placeId <= 0)
            {
                StatusText = Strings.Menu_ServerBrowser_Status_InvalidPlaceId;
                return;
            }

            IsSearching = true;
            StatusText = Strings.Menu_ServerBrowser_Status_Searching;
            Servers.Clear();

            if (placeId != _searchedPlaceId)
            {
                GameTitle = "";
                GameIconUrl = "";
            }

            _searchedPlaceId = placeId;
            _ = FillGameAsync(placeId);

            try
            {
                List<ServerListItem> servers = await ServerBrowser.ListPublicServersAsync(placeId);

                foreach (ServerListItem server in servers)
                    Servers.Add(server);

                StatusText = servers.Count > 0
                    ? string.Format(Strings.Menu_ServerBrowser_Status_FoundServers, servers.Count)
                    : Strings.Menu_ServerBrowser_Status_NoServersFound;
            }
            catch (Exception ex)
            {
                StatusText = string.Format(Strings.Menu_ServerBrowser_Status_SearchFailed, ex.Message);
            }
            finally
            {
                IsSearching = false;
                OnPropertyChanged(nameof(HasPingedServers));
                OnPropertyChanged(nameof(HeaderDetail));
            }

            if (Servers.Count > 0)
                await FillFactsAsync(placeId);
        }
    }
}
