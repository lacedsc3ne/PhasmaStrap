using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

using Wpf.Ui.Common;

using PhasmaStrap.Integrations;
using PhasmaStrap.UI.Elements.Controls;
using PhasmaStrap.UI.ViewModels.ContextMenu;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class AccountsPage
    {
        public AccountsPage()
        {
            var vm = new AccountSwitcherViewModel();
            DataContext = vm;
            InitializeComponent();

            GuardRoot.DataContext = new ViewModels.Settings.AccountGuardViewModel();

            ItemMenu.Attach<SwitcherAccount>(AccountsList, (row, menu) =>
            {
                menu.Add("Switch to this account", SymbolRegular.ArrowSwap24, () => vm.SwitchCommand.Execute(row), enabled: !row.IsCurrent, bold: true)
                    .Sub("Launch a game with it", SymbolRegular.Play24, sub =>
                    {
                        foreach (PlayTimeEntry game in PlayTimeStore.GetAll().Where(g => g.PlaceId > 0).Take(8))
                        {
                            long placeId = game.PlaceId;
                            sub.Add(game.DisplayName, SymbolRegular.Games24, () => _ = vm.SwitchAndLaunchAsync(row, placeId));
                        }
                    })
                    .Separator()
                    .Add("Edit note", SymbolRegular.Edit24, () => FocusNote(row))
                    .Copy("Copy username", row.Username)
                    .Copy("Copy user ID", row.UserId.ToString())
                    .Add("Sign in again…", SymbolRegular.Globe24, () => vm.LoginWithBrowserCommand.Execute(null))
                    .Link("Open profile on the website", $"https://www.roblox.com/users/{row.UserId}/profile")
                    .Separator()
                    .Add("Remove saved account", SymbolRegular.Delete24, () => vm.DeleteCommand.Execute(row), danger: true);
            });
        }

        private void FocusNote(SwitcherAccount row)
        {
            if (AccountsList.ItemContainerGenerator.ContainerFromItem(row) is not ContentPresenter presenter)
                return;

            // Wait for the menu to close so focus is not taken back
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (presenter.ContentTemplate?.FindName("NoteBox", presenter) is TextBoxBase box)
                {
                    box.Focus();
                    box.SelectAll();
                }
            }));
        }

        private void Page_Unloaded(object sender, System.Windows.RoutedEventArgs e)
        {
            ((AccountSwitcherViewModel)DataContext).Dispose();
        }
    }
}
