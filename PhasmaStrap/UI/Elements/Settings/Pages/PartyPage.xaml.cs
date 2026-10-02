using System.Windows;

using Wpf.Ui.Common;

using PhasmaStrap.UI.Elements.Controls;
using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class PartyPage
    {
        private PartyViewModel ViewModel => (PartyViewModel)DataContext;

        public PartyPage()
        {
            DataContext = new PartyViewModel();
            InitializeComponent();

            ItemMenu.Attach<PartyMemberRow>(MembersList, FillMemberMenu);
        }

        private void Page_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            ((PartyViewModel)DataContext).Attach();
        }

        private void Page_Unloaded(object sender, System.Windows.RoutedEventArgs e)
        {
            ((PartyViewModel)DataContext).Detach();
        }

        private void FillMemberMenu(PartyMemberRow row, ItemMenu menu)
        {
            if (row.CanManage)
                menu.Add("Make party leader…", SymbolRegular.PersonSwap24, () => _ = ViewModel.MakeLeaderAsync(row), bold: true);

            if (!row.You)
                menu.Add("Join their game", SymbolRegular.Play24, () => ViewModel.JoinMemberGame(row), enabled: row.CanJoinGame);

            menu.Separator()
                .Copy("Copy username", row.Name);

            if (row.CanManage)
            {
                menu.Separator()
                    .Add("Remove from party…", SymbolRegular.Dismiss24, () => _ = ViewModel.RemoveMemberAsync(row), danger: true);
            }
        }

        private void MemberMore_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: PartyMemberRow row } button)
                return;

            var menu = new ItemMenu();
            FillMemberMenu(row, menu);
            menu.Open(button);
        }
    }
}
