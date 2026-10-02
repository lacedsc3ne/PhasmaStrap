using System.Net.Http;

namespace PhasmaStrap.Utility
{
    public sealed class PartyMember
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("leader")]
        public bool Leader { get; set; }

        [JsonPropertyName("avatar")]
        public string Avatar { get; set; } = "";

        [JsonPropertyName("game")]
        public string Game { get; set; } = "";

        [JsonPropertyName("together")]
        public bool Together { get; set; }

        /// <summary>Server side id for this member. Older servers don't send it, which turns off leader transfer and removing people.</summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("you")]
        public bool You { get; set; }

        /// <summary>The place this member is in right now, from what their PhasmaStrap reports on each poll. Empty when not in a game.</summary>
        [JsonPropertyName("place_id")]
        public string PlaceId { get; set; } = "";

        /// <summary>The server (job ID) this member is in. Empty when unknown.</summary>
        [JsonPropertyName("job_id")]
        public string JobId { get; set; } = "";
    }

    /// <summary>The leader asked someone to take over. Nothing changes until that person accepts.</summary>
    public sealed class PartyLeaderOffer
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("from_name")]
        public string FromName { get; set; } = "";

        [JsonPropertyName("to_id")]
        public string ToId { get; set; } = "";

        [JsonPropertyName("to_name")]
        public string ToName { get; set; } = "";

        /// <summary>True when the offer is for the person reading this state.</summary>
        [JsonPropertyName("for_you")]
        public bool ForYou { get; set; }
    }

    public sealed class PartyInvite
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("code")]
        public string Code { get; set; } = "";

        [JsonPropertyName("from")]
        public string From { get; set; } = "";

        [JsonPropertyName("members")]
        public int Members { get; set; }
    }

    public sealed class PartyJoin
    {
        [JsonPropertyName("place_id")]
        public string PlaceId { get; set; } = "";

        [JsonPropertyName("job_id")]
        public string JobId { get; set; } = "";
    }

    public sealed class PartyState
    {
        [JsonPropertyName("in_party")]
        public bool InParty { get; set; }

        [JsonPropertyName("code")]
        public string Code { get; set; } = "";

        [JsonPropertyName("leader")]
        public bool Leader { get; set; }

        [JsonPropertyName("leader_name")]
        public string LeaderName { get; set; } = "";

        [JsonPropertyName("members")]
        public List<PartyMember> Members { get; set; } = new();

        [JsonPropertyName("join")]
        public PartyJoin? Join { get; set; }

        [JsonPropertyName("invites")]
        public List<PartyInvite> Invites { get; set; } = new();

        [JsonPropertyName("leader_offer")]
        public PartyLeaderOffer? LeaderOffer { get; set; }

        /// <summary>
        /// What happens when the leader leaves: PartyService.LeaderLeaves values. Set by the leader for the whole party.
        /// Empty when the server doesn't support it yet.
        /// </summary>
        [JsonPropertyName("on_leader_leave")]
        public string OnLeaderLeave { get; set; } = "";
    }

    public static class PartyService
    {
        private const string LOG_IDENT = "PartyService";

        private static string MarkerPath => Path.Combine(Paths.Base, "Party.json");

        public static PartyState Current { get; private set; } = new();

        public static event EventHandler? Changed;

        public static event EventHandler<PartyJoin>? JoinRequested;

        /// <summary>Raised once per offer when the leader asks you to take over.</summary>
        public static event EventHandler<PartyLeaderOffer>? LeaderOffered;

        /// <summary>Raised when you become leader of the party you were already in.</summary>
        public static event EventHandler? BecameLeader;

        private static string _lastOfferId = "";

        public static bool InParty => Current.InParty;

        public static bool IsLeader => Current.InParty && Current.Leader;

        /// <summary>The party server sends member ids, so the leader can be passed on and people can be removed.</summary>
        public static bool CanManageMembers => IsLeader && Current.Members.Any(m => !string.IsNullOrEmpty(m.Id));

        private static async Task<PartyState?> SendAsync(HttpMethod method, string path, object? body = null)
        {
            if (!PhasmaAccount.SignedIn)
                return null;

            try
            {
                using var request = new HttpRequestMessage(method, $"{App.ServerBase}{path}");
                PhasmaAccount.Authorize(request);

                if (body is not null)
                    request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

                using HttpResponseMessage response = await App.HttpClient.SendAsync(request);
                string text = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"{path} answered {(int)response.StatusCode}: {text}");
                    return null;
                }

                return JsonSerializer.Deserialize<PartyState>(text);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"{path} failed: {ex.Message}");
                return null;
            }
        }

        private static void Adopt(PartyState? state)
        {
            if (state is null)
                return;

            PartyState previous = Current;
            Current = state;
            WriteMarker(state);

            Changed?.Invoke(null, EventArgs.Empty);

            PartyLeaderOffer? offer = state.LeaderOffer;
            if (offer is not null && offer.ForYou && offer.Id.Length > 0 && offer.Id != _lastOfferId)
            {
                _lastOfferId = offer.Id;
                LeaderOffered?.Invoke(null, offer);
            }

            if (previous.InParty && state.InParty && previous.Code == state.Code && !previous.Leader && state.Leader)
                BecameLeader?.Invoke(null, EventArgs.Empty);

            if (state.Join is not null && !string.IsNullOrEmpty(state.Join.PlaceId))
                JoinRequested?.Invoke(null, state.Join);
        }

        private static void WriteMarker(PartyState state)
        {
            try
            {
                if (!state.InParty)
                {
                    if (File.Exists(MarkerPath))
                        File.Delete(MarkerPath);

                    return;
                }

                File.WriteAllText(MarkerPath, JsonSerializer.Serialize(new { in_party = true, leader = state.Leader, code = state.Code }));
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not write the party marker: {ex.Message}");
            }
        }

        public static bool LeaderFromMarker()
        {
            try
            {
                if (!File.Exists(MarkerPath))
                    return false;

                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(MarkerPath));
                return document.RootElement.TryGetProperty("leader", out JsonElement leader) && leader.GetBoolean();
            }
            catch
            {
                return false;
            }
        }

        private static string PresencePath => Path.Combine(Paths.Base, "PartyPresence.json");

        public static void RecordPresence(long placeId, string jobId, string game)
        {
            try
            {
                if (placeId <= 0)
                {
                    if (File.Exists(PresencePath))
                        File.Delete(PresencePath);

                    return;
                }

                File.WriteAllText(PresencePath, JsonSerializer.Serialize(new { place = placeId.ToString(), job = jobId ?? "", game = game ?? "" }));
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not record where you are: {ex.Message}");
            }
        }

        public static (long Place, string Job) WhereYouAre()
        {
            try
            {
                if (!File.Exists(PresencePath))
                    return (0, "");

                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(PresencePath));
                JsonElement root = document.RootElement;

                string Read(string key) => root.TryGetProperty(key, out JsonElement value) ? value.GetString() ?? "" : "";

                return (long.TryParse(Read("place"), out long place) ? place : 0, Read("job"));
            }
            catch
            {
                return (0, "");
            }
        }

        private static string PresenceQuery()
        {
            try
            {
                if (!File.Exists(PresencePath))
                    return "";

                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(PresencePath));
                JsonElement root = document.RootElement;

                string Read(string key) => root.TryGetProperty(key, out JsonElement value) ? value.GetString() ?? "" : "";

                return $"?place={Uri.EscapeDataString(Read("place"))}&job={Uri.EscapeDataString(Read("job"))}&game={Uri.EscapeDataString(Read("game"))}";
            }
            catch
            {
                return "";
            }
        }

        public static async Task RefreshAsync() => Adopt(await SendAsync(HttpMethod.Get, "/v1/party" + PresenceQuery()));

        public static async Task CreateAsync() => Adopt(await SendAsync(HttpMethod.Post, "/v1/party/create"));

        public static async Task<bool> JoinAsync(string code)
        {
            PartyState? state = await SendAsync(HttpMethod.Post, "/v1/party/join", new { code });
            Adopt(state);
            return state is not null;
        }

        public static async Task LeaveAsync() => Adopt(await SendAsync(HttpMethod.Post, "/v1/party/leave"));

        public static async Task DeclineInviteAsync(string id) => Adopt(await SendAsync(HttpMethod.Post, "/v1/party/invite/decline", new { id }));

        public static async Task<bool> InviteAsync(long robloxUserId)
        {
            if (!PhasmaAccount.SignedIn)
                return false;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{App.ServerBase}/v1/party/invite");
                PhasmaAccount.Authorize(request);
                request.Content = new StringContent(JsonSerializer.Serialize(new { roblox_id = robloxUserId.ToString() }), Encoding.UTF8, "application/json");

                using HttpResponseMessage response = await App.HttpClient.SendAsync(request);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Inviting {robloxUserId} failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>Asks a member to take over as leader. They have to accept before anything changes.</summary>
        public static async Task<bool> OfferLeaderAsync(string memberId)
        {
            PartyState? state = await SendAsync(HttpMethod.Post, "/v1/party/leader/offer", new { member_id = memberId });
            Adopt(state);
            return state is not null;
        }

        public static async Task<bool> CancelLeaderOfferAsync()
        {
            PartyState? state = await SendAsync(HttpMethod.Post, "/v1/party/leader/cancel");
            Adopt(state);
            return state is not null;
        }

        public static async Task<bool> AnswerLeaderOfferAsync(string offerId, bool accept)
        {
            PartyState? state = await SendAsync(HttpMethod.Post, accept ? "/v1/party/leader/accept" : "/v1/party/leader/decline", new { id = offerId });
            Adopt(state);
            return state is not null;
        }

        public static async Task<bool> RemoveMemberAsync(string memberId)
        {
            PartyState? state = await SendAsync(HttpMethod.Post, "/v1/party/remove", new { member_id = memberId });
            Adopt(state);
            return state is not null;
        }

        public static async Task ReportLaunchAsync(long placeId, string jobId)
        {
            if (!PhasmaAccount.SignedIn || !LeaderFromMarker())
                return;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{App.ServerBase}/v1/party/launch");
                PhasmaAccount.Authorize(request);
                request.Content = new StringContent(JsonSerializer.Serialize(new { place_id = placeId.ToString(), job_id = jobId ?? "" }), Encoding.UTF8, "application/json");

                using HttpResponseMessage response = await App.HttpClient.SendAsync(request);
                App.Logger.WriteLine(LOG_IDENT, response.IsSuccessStatusCode
                    ? $"Told the party to join {placeId}/{jobId}"
                    : $"The party server would not take the launch ({(int)response.StatusCode})");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Reporting the launch failed: {ex.Message}");
            }
        }

        /// <summary>The values of PartyState.OnLeaderLeave.</summary>
        public static class LeaderLeaves
        {
            /// <summary>The member who has been in the party longest becomes leader.</summary>
            public const string Pass = "pass";

            /// <summary>The party closes and everyone is let go.</summary>
            public const string End = "end";
        }

        /// <summary>The leader picks what happens when they leave. Only the leader can change it.</summary>
        public static async Task<bool> SetLeaderLeavesAsync(string mode)
        {
            if (!IsLeader)
                return false;

            PartyState? state = await SendAsync(HttpMethod.Post, "/v1/party/settings", new { on_leader_leave = mode });
            Adopt(state);
            return state is not null;
        }

        private static CancellationTokenSource? _cts;
        private static Task? _loop;

        private static bool _following;

        public static void FollowJoins()
        {
            if (_following)
                return;

            _following = true;
            JoinRequested += (_, join) => PartyLauncher.Follow(join);
            LeaderOffered += (_, offer) => ShowLeaderOffer(offer);
            BecameLeader += (_, _) => UI.NotificationCenter.Notify("You're leading the party now", "Everyone follows you into the games you launch.", UI.NotificationCategory.General);
        }

        private static void ShowLeaderOffer(PartyLeaderOffer offer)
        {
            string from = string.IsNullOrEmpty(offer.FromName) ? "The leader" : offer.FromName;

            string code = Current.Code;

            UI.NotificationCenter.Notify(
                $"{from} wants you to lead the party",
                string.IsNullOrEmpty(code) ? "You would pick the games. You can also answer on the Party page." : $"Party {code} · you would pick the games",
                UI.NotificationCategory.General,
                durationSeconds: 30,
                actionText: "Become leader",
                action: () => _ = AnswerLeaderOfferAsync(offer.Id, true));
        }

        public static void Start()
        {
            FollowJoins();

            if (_cts is not null)
                return;

            _cts = new CancellationTokenSource();
            _loop = Task.Run(() => LoopAsync(_cts.Token));
        }

        public static void Stop()
        {
            _cts?.Cancel();
            _cts = null;
            _loop = null;
        }

        private static async Task LoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (PhasmaAccount.SignedIn)
                        await RefreshAsync();
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Poll failed: {ex.Message}");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(App.Settings.Prop.PartyPollSeconds, 2, 30)), token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }
}
