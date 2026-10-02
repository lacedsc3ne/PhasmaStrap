using System.Windows;
using System.Windows.Input;

using PhasmaStrap.Integrations;
using PhasmaStrap.Utility.Backend;

namespace PhasmaStrap.UI.Elements.Dialogs
{
    /// <summary>What a list already knows about one public server when it opens the information window.</summary>
    public sealed class ServerInfoRequest
    {
        public long PlaceId { get; init; }
        public string JobId { get; init; } = "";

        /// <summary>"Game · Place". Looked up when left empty.</summary>
        public string GameTitle { get; init; } = "";

        public int Playing { get; init; }
        public int MaxPlayers { get; init; }
        public int Ping { get; init; } = -1;
        public double ServerFps { get; init; }
        public string Region { get; init; } = "";
        public DateTime? FirstSeenUtc { get; init; }

        /// <summary>Names of friends known to be in the server, or null to look them up.</summary>
        public IReadOnlyList<string>? Friends { get; init; }

        public Action? Join { get; init; }
        public bool CanJoin { get; init; } = true;
    }

    /// <summary>A small window with everything known about one public server, opened from a server's right click menu.</summary>
    public partial class ServerInfoWindow
    {
        private const string LOG_IDENT = "ServerInfoWindow";

        private readonly ServerInfoRequest _request;

        public ServerInfoWindow(ServerInfoRequest request)
        {
            _request = request;
            InitializeComponent();

            GameText.Text = request.GameTitle.Length > 0 ? request.GameTitle : $"Place {request.PlaceId}";
            JobIdText.Text = request.JobId;
            JobIdText.ToolTip = request.JobId;

            PlayersText.Text = request.MaxPlayers > 0 ? $"{request.Playing} / {request.MaxPlayers}" : request.Playing.ToString();
            PingText.Text = request.Ping > 0 ? $"{request.Ping} ms" : "Not reported";
            PingDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, ServerFacts.PingTone(request.Ping) switch
            {
                "good" => "PhasmaGoodBrush",
                "warn" => "PhasmaWarnBrush",
                "bad" => "PhasmaBadBrush",
                _ => "TextFillColorTertiaryBrush",
            });

            bool full = request.MaxPlayers > 0 && request.Playing >= request.MaxPlayers;
            int space = Math.Max(0, request.MaxPlayers - request.Playing);
            StateText.Text = full ? "This server is full right now." : request.MaxPlayers > 0 ? $"Room for {space} more." : "";

            JoinButton.IsEnabled = request.Join is not null && request.CanJoin && !full;

            ShowRegion(request.Region);
            UptimeText.Text = Fallback(ServerFacts.Uptime(request.FirstSeenUtc));
            ServerFpsText.Text = request.ServerFps > 0 ? Math.Round(request.ServerFps).ToString() : "Not known";
            ClientFpsText.Text = "Not known";
            FriendsText.Text = request.Friends is null ? "Looking..." : FriendsLine(request.Friends);
            NoteText.Text = "Region, uptime and players' FPS come from PhasmaStrap players who were in this server. Ping is what Roblox reports.";

            Loaded += (_, _) => _ = FillAsync();
        }

        /// <summary>Opens the window over <paramref name="owner"/>.</summary>
        public static void Open(DependencyObject? owner, ServerInfoRequest request)
        {
            var window = new ServerInfoWindow(request);

            Window? parent = owner is null ? null : GetWindow(owner);
            if (parent is not null)
                window.Owner = parent;
            else
                window.WindowStartupLocation = WindowStartupLocation.CenterScreen;

            window.Show();
        }

        private static string Fallback(string text) => text.Length > 0 ? text : "Not known";

        private void ShowRegion(string region)
        {
            (string city, string country) = ServerFacts.SplitRegion(region);
            RegionText.Text = city.Length == 0 ? "Not known" : country.Length > 0 ? $"{city}, {country}" : city;
            RegionText.ToolTip = region.Length > 0 ? region : null;
        }

        private static string FriendsLine(IReadOnlyList<string> friends) => friends.Count switch
        {
            0 => "None of your friends are in this server.",
            1 => friends[0],
            _ => string.Join(", ", friends.Take(friends.Count - 1)) + " and " + friends[^1],
        };

        private async Task FillAsync()
        {
            Task title = FillTitleAsync();
            Task facts = FillFactsAsync();
            Task friends = _request.Friends is null ? FillFriendsAsync() : Task.CompletedTask;

            try
            {
                await Task.WhenAll(title, facts, friends);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Filling in failed: {ex.Message}");
            }
        }

        private async Task FillTitleAsync()
        {
            if (_request.GameTitle.Length > 0 || _request.PlaceId <= 0)
                return;

            GameInfo? game = await GameLookup.FromPlaceAsync(_request.PlaceId);
            if (game is not null && IsLoaded)
                GameText.Text = PlaceNames.Display(game.Name, _request.PlaceId);
        }

        private async Task FillFactsAsync()
        {
            if (_request.PlaceId <= 0 || _request.JobId.Length == 0)
                return;

            var item = new ServerListItem { JobId = _request.JobId, Playing = _request.Playing, MaxPlayers = _request.MaxPlayers, Ping = _request.Ping, Fps = _request.ServerFps };
            Dictionary<string, ServerStats> facts = await GamesApi.FactsAsync(_request.PlaceId, new[] { item });

            if (!IsLoaded || !facts.TryGetValue(_request.JobId, out ServerStats? stats))
                return;

            if (_request.Region.Length == 0 && stats.Region.Length > 0)
                ShowRegion(stats.Region);

            if (_request.FirstSeenUtc is null && stats.FirstSeenUtc is DateTime first)
                UptimeText.Text = Fallback(ServerFacts.Uptime(first));

            if (_request.ServerFps <= 0 && stats.ServerFps is double serverFps && serverFps > 0)
                ServerFpsText.Text = Math.Round(serverFps).ToString();

            if (stats.ClientFps is double clientFps && clientFps > 0)
                ClientFpsText.Text = Math.Round(clientFps).ToString();
        }

        private async Task FillFriendsAsync()
        {
            try
            {
                RobloxCookie.RobloxAccount? me = await RobloxCookie.GetAccountAsync();
                if (me is null)
                {
                    FriendsText.Text = "Sign in to Roblox to see friends here.";
                    return;
                }

                List<FriendInfo> friends = await FriendsService.GetFriendsAsync(me.UserId);
                Dictionary<long, FriendPresence> presence = await FriendsService.GetPresenceAsync(friends.Select(f => f.UserId));

                var here = friends
                    .Where(f => presence.TryGetValue(f.UserId, out FriendPresence? p) && string.Equals(p.GameId, _request.JobId, StringComparison.OrdinalIgnoreCase))
                    .Select(f => string.IsNullOrWhiteSpace(f.DisplayName) ? f.Username : f.DisplayName)
                    .ToList();

                if (IsLoaded)
                    FriendsText.Text = FriendsLine(here);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Friends lookup failed: {ex.Message}");
                FriendsText.Text = "Couldn't check your friends right now.";
            }
        }

        private void CopyJobId_Click(object sender, RoutedEventArgs e) => ClipboardShare.CopyText(_request.JobId);

        private void Join_Click(object sender, RoutedEventArgs e)
        {
            _request.Join?.Invoke();
            Close();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        }
    }
}
