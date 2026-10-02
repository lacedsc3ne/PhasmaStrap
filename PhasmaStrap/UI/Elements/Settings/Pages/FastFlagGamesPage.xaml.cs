using System.Windows;
using System.Windows.Input;

using Wpf.Ui.Common;

using PhasmaStrap.UI.Elements.Controls;
using PhasmaStrap.UI.ViewModels.Settings;
using PhasmaStrap.Utility;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class FastFlagGamesPage
    {
        private readonly FastFlagGamesViewModel _viewModel = new();

        public FastFlagGamesPage()
        {
            DataContext = _viewModel;
            _viewModel.OpenEditorRequested += OpenEditor;

            InitializeComponent();

            ItemMenu.Attach<GameRuleRow>(RulesList, (row, menu) =>
            {
                long placeId = row.Rule.PlaceId > 0 ? row.Rule.PlaceId : row.Rule.RootPlaceId;

                menu.Add("Edit flags", SymbolRegular.Edit24, () => _viewModel.EditProfileCommand.Execute(row), bold: true)
                    .Add("See every flag it starts with", SymbolRegular.Eye24, () => _viewModel.PreviewCommand.Execute(row))
                    .Sub("Use another profile", SymbolRegular.Flag24, sub =>
                    {
                        foreach (FlagProfile profile in App.FlagProfiles.Prop.Profiles.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
                        {
                            string id = profile.Id;
                            bool current = id == row.ProfileId;
                            sub.Add(profile.Name, current ? SymbolRegular.Checkmark24 : SymbolRegular.Flag24, () => row.ProfileId = id, enabled: !current);
                        }
                    })
                    .Separator()
                    .Add("Launch", SymbolRegular.Play24, () => RobloxLaunch.Join(placeId), enabled: placeId > 0)
                    .Copy("Copy game link", placeId > 0 ? $"https://www.roblox.com/games/{placeId}" : null)
                    .Separator()
                    .Add("Stop giving it its own flags", SymbolRegular.Delete24, () => _viewModel.RemoveCommand.Execute(row), danger: true);
            });
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            App.FlagProfiles.Edited -= OnProfilesEdited;
            App.FlagProfiles.Edited += OnProfilesEdited;

            _viewModel.Reload();
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e) => App.FlagProfiles.Edited -= OnProfilesEdited;

        private void OnProfilesEdited(object? sender, EventArgs e)
        {
            if (!IsKeyboardFocusWithin)
                _viewModel.Reload();
            else
                foreach (GameRuleRow row in _viewModel.Rules)
                    row.Refresh();
        }

        private void OpenEditor(string profileId)
        {
            FlagEditorRequest.ProfileToOpen = profileId;
            FastFlagSettingsPage.SelectTab(this, "FastFlagEditorPage");
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && _viewModel.SearchCommand.CanExecute(null))
                _viewModel.SearchCommand.Execute(null);
        }
    }
}
