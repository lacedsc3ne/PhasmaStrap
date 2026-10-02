using PhasmaStrap.Integrations;

namespace PhasmaStrap.Utility.Backend
{
    /// <summary>What Liam's server knows about one public server (job id). Every field may be missing.</summary>
    public sealed class ServerStats
    {
        [JsonPropertyName("job_id")]
        public string JobId { get; set; } = "";

        /// <summary>Datacenter city, for example "Frankfurt".</summary>
        [JsonPropertyName("city")]
        public string City { get; set; } = "";

        /// <summary>Two letter country code, for example "DE".</summary>
        [JsonPropertyName("country")]
        public string Country { get; set; } = "";

        /// <summary>Two letter continent code: EU, NA, SA, AS, OC or AF.</summary>
        [JsonPropertyName("continent")]
        public string Continent { get; set; } = "";

        /// <summary>The server's own frame rate as Roblox last listed it.</summary>
        [JsonPropertyName("server_fps")]
        public double? ServerFps { get; set; }

        /// <summary>Average frame rate PhasmaStrap players reported while in this server.</summary>
        [JsonPropertyName("client_fps")]
        public double? ClientFps { get; set; }

        /// <summary>Unix seconds of the first time anyone saw this server. Used as its start time.</summary>
        [JsonPropertyName("first_seen")]
        public long? FirstSeen { get; set; }

        [JsonPropertyName("reports")]
        public int Reports { get; set; }

        [JsonIgnore]
        public DateTime? FirstSeenUtc => FirstSeen is long seconds && seconds > 0 ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime : null;

        [JsonIgnore]
        public string Region => City.Length == 0 ? "" : Country.Length == 0 ? City : $"{City}, {Country}";
    }

    public sealed class ServerStatsResponse
    {
        [JsonPropertyName("servers")]
        public List<ServerStats> Servers { get; set; } = new();
    }

    /// <summary>Player and server totals for one place.</summary>
    public sealed class PlaceStats
    {
        [JsonPropertyName("place_id")]
        public long PlaceId { get; set; }

        [JsonPropertyName("playing")]
        public long Playing { get; set; }

        [JsonPropertyName("servers")]
        public long Servers { get; set; }

        [JsonPropertyName("updated_at")]
        public long UpdatedAt { get; set; }
    }

    public sealed class PlaceStatsResponse
    {
        [JsonPropertyName("places")]
        public List<PlaceStats> Places { get; set; } = new();
    }

    /// <summary>One public server as the client saw it in Roblox's list, sent along with a lookup.</summary>
    public sealed class ServerSeen
    {
        [JsonPropertyName("job_id")]
        public string JobId { get; set; } = "";

        [JsonPropertyName("playing")]
        public int Playing { get; set; }

        [JsonPropertyName("max_players")]
        public int MaxPlayers { get; set; }

        [JsonPropertyName("server_fps")]
        public double? ServerFps { get; set; }
    }

    public sealed class ServerLookupRequest
    {
        [JsonPropertyName("place_id")]
        public long PlaceId { get; set; }

        [JsonPropertyName("servers")]
        public List<ServerSeen> Servers { get; set; } = new();
    }

    /// <summary>What the client reports about the server it is playing in.</summary>
    public sealed class ServerReport
    {
        /// <summary>"join" once the region is known, "leave" when the player leaves.</summary>
        [JsonPropertyName("event")]
        public string Event { get; set; } = "";

        [JsonPropertyName("place_id")]
        public long PlaceId { get; set; }

        [JsonPropertyName("universe_id")]
        public long UniverseId { get; set; }

        [JsonPropertyName("job_id")]
        public string JobId { get; set; } = "";

        [JsonPropertyName("city")]
        public string City { get; set; } = "";

        [JsonPropertyName("country")]
        public string Country { get; set; } = "";

        [JsonPropertyName("joined_at")]
        public long JoinedAt { get; set; }

        [JsonPropertyName("left_at")]
        public long? LeftAt { get; set; }

        [JsonPropertyName("fps_avg")]
        public double? FpsAvg { get; set; }

        [JsonPropertyName("fps_samples")]
        public int FpsSamples { get; set; }
    }

    /// <summary>
    /// Server side data for the Games pages: region, uptime and frame rate per job id, players per place.
    /// Every call fails soft and returns an empty result, so the pages just leave those cells blank.
    /// </summary>
    public static class GamesApi
    {
        private const int LookupBatch = 100;

        private static readonly TimeSpan StatsTtl = TimeSpan.FromMinutes(3);

        private static readonly Dictionary<string, (DateTime At, ServerStats? Stats)> _stats = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object _lock = new();

        /// <summary>Region, uptime and FPS for the given servers of one place, keyed by job id. Missing servers are simply absent.</summary>
        public static async Task<Dictionary<string, ServerStats>> LookupServersAsync(long placeId, IEnumerable<ServerListItem> servers, CancellationToken ct = default)
        {
            var result = new Dictionary<string, ServerStats>(StringComparer.OrdinalIgnoreCase);
            var wanted = new List<ServerListItem>();

            lock (_lock)
            {
                foreach (ServerListItem server in servers)
                {
                    if (server.JobId.Length == 0)
                        continue;

                    if (_stats.TryGetValue(server.JobId, out var hit) && DateTime.UtcNow - hit.At < StatsTtl)
                    {
                        if (hit.Stats is not null)
                            result[server.JobId] = hit.Stats;
                    }
                    else
                    {
                        wanted.Add(server);
                    }
                }
            }

            if (placeId <= 0 || wanted.Count == 0)
                return result;

            foreach (ServerListItem[] batch in wanted.Chunk(LookupBatch))
            {
                if (ct.IsCancellationRequested)
                    break;

                var request = new ServerLookupRequest
                {
                    PlaceId = placeId,
                    Servers = batch.Select(s => new ServerSeen { JobId = s.JobId, Playing = s.Playing, MaxPlayers = s.MaxPlayers, ServerFps = s.Fps > 0 ? s.Fps : null }).ToList(),
                };

                ServerStatsResponse? response = await PhasmaApi.PostAsync<ServerStatsResponse>("/v1/games/servers/lookup", request, signedInOnly: false);
                if (response is null)
                    break;

                lock (_lock)
                {
                    var answered = response.Servers.Where(s => s.JobId.Length > 0).GroupBy(s => s.JobId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                    foreach (ServerListItem server in batch)
                    {
                        answered.TryGetValue(server.JobId, out ServerStats? stats);
                        _stats[server.JobId] = (DateTime.UtcNow, stats);

                        if (stats is not null)
                            result[server.JobId] = stats;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Everything known about these servers: regions you have seen yourself first (session history),
        /// then whatever PhasmaStrap's server adds (region, uptime, FPS). Keyed by job id.
        /// </summary>
        public static async Task<Dictionary<string, ServerStats>> FactsAsync(long placeId, IReadOnlyCollection<ServerListItem> servers, CancellationToken ct = default)
        {
            var facts = new Dictionary<string, ServerStats>(StringComparer.OrdinalIgnoreCase);

            Dictionary<string, string> seen = ServerFacts.HistoryRegions();
            foreach (ServerListItem server in servers)
            {
                if (seen.TryGetValue(server.JobId, out string? region))
                {
                    (string city, string country) = ServerFacts.SplitRegion(region);
                    facts[server.JobId] = new ServerStats { JobId = server.JobId, City = city, Country = country, Continent = ServerFacts.ContinentOf(country) };
                }
            }

            Dictionary<string, ServerStats> remote = await LookupServersAsync(placeId, servers, ct);
            foreach ((string jobId, ServerStats stats) in remote)
            {
                if (facts.TryGetValue(jobId, out ServerStats? local) && stats.City.Length == 0)
                {
                    stats.City = local.City;
                    stats.Country = local.Country;
                }

                stats.Country = ServerFacts.CountryCode(stats.Country);
                stats.Continent = ServerFacts.ContinentOf(stats.Country, stats.Continent);
                facts[jobId] = stats;
            }

            return facts;
        }

        /// <summary>Players and servers per place, keyed by place id. Empty when the server doesn't answer.</summary>
        public static async Task<Dictionary<long, PlaceStats>> GetPlaceStatsAsync(IEnumerable<long> placeIds)
        {
            List<long> ids = placeIds.Where(id => id > 0).Distinct().OrderBy(id => id).Take(50).ToList();
            if (ids.Count == 0)
                return new Dictionary<long, PlaceStats>();

            PlaceStatsResponse? response = await PhasmaApi.GetAsync<PlaceStatsResponse>($"/v1/games/places?ids={string.Join(",", ids)}", cacheFor: TimeSpan.FromMinutes(2));

            return response?.Places.Where(p => p.PlaceId > 0).GroupBy(p => p.PlaceId).ToDictionary(g => g.Key, g => g.First())
                ?? new Dictionary<long, PlaceStats>();
        }

        /// <summary>Sends what the client knows about the server it is in. Only for signed in users, never throws.</summary>
        public static Task<bool> ReportServerAsync(ServerReport report) => PhasmaApi.PostAsync("/v1/games/servers/report", report);
    }

    /// <summary>
    /// Runs in the watcher: reports the public server you are playing in (region once known, then FPS when you leave),
    /// so the server browser can show region, uptime and FPS for servers nobody else has looked up.
    /// </summary>
    public static class GameServerReporter
    {
        private const string LOG_IDENT = "GameServerReporter";
        private const int SampleSeconds = 10;

        private sealed class Visit
        {
            public long PlaceId { get; init; }
            public long UniverseId { get; init; }
            public string JobId { get; init; } = "";
            public DateTime JoinedUtc { get; init; }
            public List<double> Fps { get; } = new();
            public bool JoinSent { get; set; }
        }

        private static readonly object _lock = new();
        private static Visit? _visit;
        private static System.Threading.Timer? _timer;
        private static bool _following;

        public static void Follow(ActivityWatcher watcher)
        {
            lock (_lock)
            {
                if (_following)
                    return;

                _following = true;
            }

            watcher.OnGameJoin += (_, _) => Begin(watcher.Data);
            watcher.OnGameLeave += (_, _) => End();
            ServerRegion.Changed += OnRegionChanged;

            _timer = new System.Threading.Timer(_ => Sample(), null, SampleSeconds * 1000, SampleSeconds * 1000);
        }

        private static void Begin(ActivityData data)
        {
            End();

            // Only public servers show up in the server browser, so the rest are never reported.
            if (data.PlaceId <= 0 || string.IsNullOrEmpty(data.JobId) || data.ServerType != ServerType.Public)
                return;

            lock (_lock)
            {
                _visit = new Visit
                {
                    PlaceId = data.PlaceId,
                    UniverseId = data.UniverseId,
                    JobId = data.JobId,
                    JoinedUtc = DateTime.UtcNow,
                };
            }

            // The region arrives a moment later through ServerRegion.Changed, which sends the join report.
        }

        private static void Sample()
        {
            double fps = FpsFeed.Latest;
            if (fps <= 0)
                return;

            lock (_lock)
                _visit?.Fps.Add(fps);
        }

        private static void OnRegionChanged()
        {
            string region = ServerRegion.Current;
            if (region.Length == 0)
                return;

            ServerReport? report = null;

            lock (_lock)
            {
                if (_visit is null || _visit.JoinSent)
                    return;

                _visit.JoinSent = true;
                report = Build(_visit, "join", region, null);
            }

            Send(report);
        }

        private static void End()
        {
            ServerReport? report = null;

            lock (_lock)
            {
                if (_visit is null)
                    return;

                report = Build(_visit, "leave", ServerRegion.Current, DateTime.UtcNow);
                _visit = null;
            }

            Send(report);
        }

        private static ServerReport Build(Visit visit, string kind, string region, DateTime? leftUtc)
        {
            (string city, string country) = ServerFacts.SplitRegion(region);

            return new ServerReport
            {
                Event = kind,
                PlaceId = visit.PlaceId,
                UniverseId = visit.UniverseId,
                JobId = visit.JobId,
                City = city,
                Country = country,
                JoinedAt = new DateTimeOffset(visit.JoinedUtc).ToUnixTimeSeconds(),
                LeftAt = leftUtc is DateTime left ? new DateTimeOffset(left).ToUnixTimeSeconds() : null,
                FpsAvg = visit.Fps.Count > 0 ? Math.Round(visit.Fps.Average(), 1) : null,
                FpsSamples = visit.Fps.Count,
            };
        }

        private static void Send(ServerReport? report)
        {
            if (report is null || !PhasmaAccount.SignedIn)
                return;

            _ = Task.Run(async () =>
            {
                try
                {
                    if (!await GamesApi.ReportServerAsync(report))
                        App.Logger.WriteLine(LOG_IDENT, $"The {report.Event} report for {report.JobId} was not taken");
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Report failed: {ex.Message}");
                }
            });
        }
    }
}
