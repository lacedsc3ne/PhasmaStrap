using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

using Wpf.Ui.Common;

using PhasmaStrap.UI.Elements.Controls;
using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class FriendsPage
    {
        public FriendsPage()
        {
            var vm = new FriendsViewModel();
            DataContext = vm;
            InitializeComponent();

            // The party and accounts cards follow the party live while the page is open
            Loaded += (_, _) => vm.Attach();
            Unloaded += (_, _) => vm.Detach();

            ItemMenu.Attach<FriendRow>(FriendsList, (row, menu) =>
            {
                menu.Add("Join", SymbolRegular.Play24, () => vm.JoinCommand.Execute(row), gesture: "Enter", enabled: row.Joinable, bold: true);

                if (row.CanJoinServer)
                    menu.Add("Join their server", SymbolRegular.Server24, () => vm.SameServerCommand.Execute(row));

                if (vm.PartyInviteVisibility == Visibility.Visible)
                    menu.Add("Invite to party", SymbolRegular.PeopleTeam24, () => vm.InviteToPartyCommand.Execute(row));

                menu.Add(row.IsFavourite ? "Remove from favourites" : "Add to favourites", SymbolRegular.Star24, () => row.IsFavourite = !row.IsFavourite)
                    .Separator()
                    .Add("Edit note", SymbolRegular.Edit24, () => FocusNote(vm, row))
                    .Copy("Copy username", row.Username)
                    .Copy("Copy user ID", row.UserId.ToString())
                    .Link("Open profile on the website", $"https://www.roblox.com/users/{row.UserId}/profile")
                    .Separator()
                    .Sub("Alerts for this friend", SymbolRegular.Alert24, sub =>
                    {
                        for (int i = 0; i < FriendRow.AlertLabels.Length; i++)
                        {
                            int index = i;
                            sub.Add(FriendRow.AlertLabels[i], index == row.AlertModeIndex ? SymbolRegular.Checkmark24 : SymbolRegular.Circle24,
                                () => row.AlertModeIndex = index, enabled: vm.AlertsEnabled, bold: index == row.AlertModeIndex);
                        }
                    });
            });

            FriendsList.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter && vm.SelectedFriend is { Joinable: true } friend)
                {
                    vm.JoinCommand.Execute(friend);
                    e.Handled = true;
                }
            };
        }

        private void FocusNote(FriendsViewModel vm, FriendRow row)
        {
            vm.SelectedFriend = row;

            // The details panel needs a layout pass before its note box can take focus
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                FriendNoteBox.Focus();
                FriendNoteBox.SelectAll();
            }));
        }
    }
}
