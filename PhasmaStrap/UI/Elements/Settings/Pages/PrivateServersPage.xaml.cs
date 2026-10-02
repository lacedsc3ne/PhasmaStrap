using System.Windows;

using Wpf.Ui.Common;

using PhasmaStrap.Integrations;
using PhasmaStrap.UI.Elements.Controls;
using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class PrivateServersPage
    {
        private readonly PrivateServersViewModel _viewModel = new();

        public PrivateServersPage()
        {
            DataContext = _viewModel;
            InitializeComponent();

            // Covers both lists, yours and the ones shared with you.
            ItemMenu.Attach<PrivateServerRow>(ServersRoot, (row, menu) => FillPrivateServerMenu(menu, row.Server, row.Title, row.IconUrl ?? "",
                join: () => _viewModel.JoinCommand.Execute(row),
                copyLink: () => _viewModel.CopyLinkCommand.Execute(row),
                replaceLink: () => _viewModel.NewLinkCommand.Execute(row),
                openOnWebsite: () => _viewModel.OpenOnRobloxCommand.Execute(row),
                hidden: row.IsHidden,
                setHidden: hide => _viewModel.SetHidden(row, hide)));
        }

        /// <summary>
        /// The menu for one private server, shared with the private servers on a game's page.
        /// Actions left null are left out of the menu.
        /// </summary>
        internal static void FillPrivateServerMenu(ItemMenu menu, PrivateServerInfo server, string title, string iconUrl,
            Action join, Action? copyLink, Action? replaceLink, Action? openOnWebsite, bool hidden, Action<bool>? setHidden)
        {
            bool canJoin = server.Active && server.PlaceId > 0;

            menu.Add("Join", SymbolRegular.Play24, join, gesture: "Enter", enabled: canJoin, bold: true)
                .Sub("Join with account", SymbolRegular.PersonSwap24, sub =>
                {
                    foreach (AccountQuickSwitch.Account account in HomeViewModel.SavedAccounts())
                        sub.Add(account.Title, SymbolRegular.Person24, () => _ = JoinAsAccountAsync(server, account));
                }, enabled: canJoin)
                .Separator();

            // Roblox only hands out the invite link to the owner.
            if (server.Owned)
            {
                if (copyLink is not null)
                    menu.Add("Copy invite link", SymbolRegular.Copy24, copyLink);

                if (replaceLink is not null)
                    menu.Add("Replace invite link", SymbolRegular.ArrowSync24, replaceLink);
            }

            menu.Sub("Launch with flag profile", SymbolRegular.Flag24, sub => HomePage.FillOneTimeProfileMenu(sub, server.UniverseId, server.PlaceId, join), enabled: canJoin)
                .Sub("Use a flag profile", SymbolRegular.Flag24, sub => HomePage.FillProfileMenu(sub, server.UniverseId, server.PlaceId, title, iconUrl), enabled: server.UniverseId > 0);

            if (openOnWebsite is not null)
                menu.Add("Manage on the website", SymbolRegular.Open24, openOnWebsite, enabled: server.PlaceId > 0);

            if (setHidden is not null)
            {
                menu.Separator();

                if (hidden)
                    menu.Add("Show in this list again", SymbolRegular.Eye24, () => setHidden(false));
                else
                    menu.Add("Hide from this list", SymbolRegular.EyeOff24, () => setHidden(true));
            }
        }

        /// <summary>Gets the server's access code with the account signed in now, then joins as another saved account.</summary>
        private static async Task JoinAsAccountAsync(PrivateServerInfo server, AccountQuickSwitch.Account account)
        {
            try
            {
                string? accessCode = await PhasmaStrap.Integrations.PrivateServers.GetAccessCodeAsync(server);
                if (accessCode is null)
                {
                    Frontend.ShowMessageBox("Roblox didn't give a way into that server. It may be inactive, or this account may no longer have access.", MessageBoxImage.Warning);
                    return;
                }

                await HomeViewModel.LaunchAsAccountAsync(account, RobloxLaunch.DeepLink(server.PlaceId, accessCode: accessCode));
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("PrivateServersPage", $"Join as {account.Title} failed: {ex.Message}");
                Frontend.ShowMessageBox($"Couldn't join as {account.Title}: {ex.Message}", MessageBoxImage.Warning);
            }
        }
    }
}
