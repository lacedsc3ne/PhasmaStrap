using System.Windows;
using System.Windows.Controls;

using Wpf.Ui.Common;

using PhasmaStrap.Integrations;
using PhasmaStrap.UI.Elements.Controls;
using PhasmaStrap.UI.Elements.Dialogs;
using PhasmaStrap.UI.Elements.Settings.Pages;
using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Games
{
    public partial class GameView : UserControl
    {
        public GameView()
        {
            InitializeComponent();

            ItemMenu.Attach<ServerRow>(ServerList, (row, menu) =>
            {
                if (DataContext is not GameViewModel game)
                    return;

                long placeId = game.SelectedPlace?.PlaceId ?? 0;
                ServerBrowserPage.FillServerMenu(menu, placeId, row.Server.JobId, () => game.JoinServerCommand.Execute(row), row.CanJoin,
                    info: () => ServerInfoWindow.Open(this, new ServerInfoRequest
                    {
                        PlaceId = placeId,
                        JobId = row.Server.JobId,
                        GameTitle = game.SelectedPlace is PlaceItem place && game.Places.Count > 1 ? $"{game.Name} · {place.Name}" : game.Name,
                        Playing = row.Playing,
                        MaxPlayers = row.MaxPlayers,
                        Ping = row.Server.Ping,
                        ServerFps = row.Fps,
                        Region = row.Region,
                        FirstSeenUtc = row.FirstSeenUtc,
                        Friends = row.Friends.Select(f => f.Name).ToList(),
                        Join = () => game.JoinServerCommand.Execute(row),
                        CanJoin = row.CanJoin,
                    }));
            });

            // The private server list and the places rail share the Private servers page's menu.
            ItemMenu.Attach<GamePrivateServerRow>(PrivateList, BuildPrivateMenu);
            ItemMenu.Attach<GamePrivateServerRow>(PrivateRail, BuildPrivateMenu);
        }

        private void BuildPrivateMenu(GamePrivateServerRow row, ItemMenu menu)
        {
            if (DataContext is not GameViewModel game)
                return;

            PrivateServersPage.FillPrivateServerMenu(menu, row.Info, row.Title, game.IconUrl,
                join: () => game.JoinPrivateCommand.Execute(row),
                copyLink: () => game.CopyPrivateLinkCommand.Execute(row),
                replaceLink: () => game.NewPrivateLinkCommand.Execute(row),
                openOnWebsite: () => game.OpenPrivateOnWebsiteCommand.Execute(row),
                hidden: false,
                setHidden: hide =>
                {
                    if (hide)
                        game.HidePrivateServer(row);
                });
        }

        /// <summary>The arrow next to Play: every way to launch this game.</summary>
        private void PlayMenuButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not GameViewModel game)
                return;

            long placeId = game.LaunchPlaceId;
            bool known = placeId > 0;
            var menu = new ItemMenu();

            menu.Add(game.PlayText, SymbolRegular.Play24, () => game.PlayCommand.Execute(null), enabled: known, bold: true)
                .Add("Pick a server", SymbolRegular.Server24, () => game.ShowTabCommand.Execute(GameViewModel.TabServers), enabled: known)
                .Separator()
                .Sub("Launch with account", SymbolRegular.PersonSwap24, sub => HomePage.FillAccountMenu(sub, RobloxLaunch.DeepLink(placeId)), enabled: known)
                .Sub("Launch with flag profile", SymbolRegular.Flag24, sub => HomePage.FillOneTimeProfileMenu(sub, game.UniverseId, placeId, () => game.PlayCommand.Execute(null)), enabled: known);

            if (game.Places.Count > 1)
            {
                menu.Sub("Launch another place", SymbolRegular.Grid24, sub =>
                {
                    foreach (PlaceItem place in game.Places.Take(25))
                        sub.Add(place.IsRoot ? $"{place.Name} (start place)" : place.Name, SymbolRegular.Play24, () => game.PlayPlaceCommand.Execute(place), bold: place.PlaceId == placeId);
                });
            }

            HomePage.OpenBelow(menu, PlayMenuButton);
        }

        /// <summary>The "..." button in the banner.</summary>
        private void MoreButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not GameViewModel game)
                return;

            long placeId = game.LaunchPlaceId;
            bool known = placeId > 0;
            var menu = new ItemMenu();

            menu.Add(game.IsFavourite ? "Remove from favourites" : "Add to favourites", SymbolRegular.Heart24, () => game.ToggleFavouriteCommand.Execute(null))
                .Separator()
                .Copy("Copy place ID", known ? placeId.ToString() : null)
                .Add("Create desktop shortcut", SymbolRegular.Desktop24, () => _ = CreateShortcutAsync(placeId), enabled: known)
                .Add("Manage its flags", SymbolRegular.Flag24, () => game.EditFlagsCommand.Execute(null))
                .Separator()
                .Add("Open on the website", SymbolRegular.Open24, () => game.OpenWebsiteCommand.Execute(null));

            HomePage.OpenBelow(menu, MoreButton);
        }

        private static async Task CreateShortcutAsync(long placeId)
        {
            GameShortcutCreator.Result result = await GameShortcutCreator.CreateAsync(placeId.ToString(), Paths.Desktop);

            NotificationCenter.Notify(result.Success ? "Shortcut added to your desktop" : "Couldn't make the shortcut", result.Message, NotificationCategory.General);
        }
    }
}
