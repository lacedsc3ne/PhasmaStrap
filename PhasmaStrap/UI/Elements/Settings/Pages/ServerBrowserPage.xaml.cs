using Wpf.Ui.Common;

using PhasmaStrap.UI.Elements.Controls;
using PhasmaStrap.UI.Elements.Dialogs;
using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class ServerBrowserPage
    {
        private readonly ServerBrowserViewModel _viewModel = new();

        public ServerBrowserPage()
        {
            DataContext = _viewModel;
            InitializeComponent();

            ItemMenu.Attach<ServerListItem>(ServerList, (server, menu) =>
            {
                long placeId = long.TryParse(_viewModel.PlaceId.Trim(), out long id) ? id : 0;
                long listedPlace = _viewModel.SearchedPlaceId > 0 ? _viewModel.SearchedPlaceId : placeId;

                FillServerMenu(menu, placeId, server.JobId, () => _viewModel.JoinCommand.Execute(server), info: () => ServerInfoWindow.Open(this, new ServerInfoRequest
                {
                    PlaceId = listedPlace,
                    JobId = server.JobId,
                    GameTitle = _viewModel.GameTitle,
                    Playing = server.Playing,
                    MaxPlayers = server.MaxPlayers,
                    Ping = server.Ping,
                    ServerFps = server.Fps,
                    Region = server.Region,
                    FirstSeenUtc = server.FirstSeenUtc,
                    Join = () => _viewModel.JoinCommand.Execute(server),
                }));
            });
        }

        /// <summary>The menu for one public server, shared with the server list on a game's page.</summary>
        internal static void FillServerMenu(ItemMenu menu, long placeId, string jobId, Action join, bool canJoin = true, Action? info = null)
        {
            bool known = placeId > 0 && jobId.Length > 0;
            string? invite = known ? RobloxLaunch.DeepLink(placeId, jobId) : null;

            menu.Add("Join", SymbolRegular.Play24, join, gesture: "Enter", enabled: known && canJoin, bold: true)
                .Sub("Join with account", SymbolRegular.PersonSwap24, sub => HomePage.FillAccountMenu(sub, invite ?? ""), enabled: known && canJoin);

            // Only the party leader can send the party somewhere.
            if (PartyService.LeaderFromMarker())
                menu.Add("Invite my party here", SymbolRegular.PeopleTeam24, () => _ = PartyService.ReportLaunchAsync(placeId, jobId), enabled: known);

            menu.Separator()
                .Copy("Copy invite link", invite)
                .Copy("Copy server ID", jobId);

            if (info is not null)
                menu.Separator()
                    .Add("Server information", SymbolRegular.Info24, info, enabled: jobId.Length > 0);
        }
    }
}
