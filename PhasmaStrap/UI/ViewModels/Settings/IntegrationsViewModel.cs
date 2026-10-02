using System.Collections.ObjectModel;
using System.Windows.Input;

using Microsoft.Win32;

using CommunityToolkit.Mvvm.Input;

using PhasmaStrap.Integrations;
using PhasmaStrap.Models;
using PhasmaStrap.UI.Elements.ContextMenu;
using PhasmaStrap.UI.Elements.Dialogs;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public class IntegrationsViewModel : NotifyPropertyChangedViewModel
    {
        public ICommand AddIntegrationCommand => new RelayCommand(AddIntegration);

        public ICommand DeleteIntegrationCommand => new RelayCommand(DeleteIntegration);

        public ICommand BrowseIntegrationLocationCommand => new RelayCommand(BrowseIntegrationLocation);

        public ICommand AddRPCTemplateCommand => new RelayCommand(AddRPCTemplate);

        public ICommand DeleteRPCTemplateCommand => new RelayCommand(DeleteRPCTemplate);

        public bool StudioRichPresenceEnabled
        {
            get => App.Settings.Prop.StudioRichPresenceEnabled;
            set
            {
                App.Settings.Prop.StudioRichPresenceEnabled = value;

                if (value)
                {
                    App.StudioRichPresence ??= new PhasmaStrap.StudioRichPresence();
                }
                else
                {
                    App.StudioRichPresence?.Dispose();
                    App.StudioRichPresence = null;
                }
            }
        }

        public bool RpcAutoTranslate
        {
            get => App.Settings.Prop.RpcAutoTranslate;
            set => App.Settings.Prop.RpcAutoTranslate = value;
        }

        public ICommand OpenCustomRPCCommand => new RelayCommand(() => new RPCTemplatesWindow(this).Show());

        public ICommand AccountWindowCommand => new RelayCommand(() => new AccountSwitcherWindow().Show());

        private readonly List<PlayTimeEntry> _previewEntries = PlayTimeStore.GetAll()
            .OrderByDescending(x => x.LastPlayed)
            .Take(5)
            .ToList();

        private int _previewIndex;

        public bool HasPreviewData => _previewEntries.Count > 0;
        public string PreviewGame => HasPreviewData ? _previewEntries[_previewIndex].Name : "";
        public string PreviewIconUrl => HasPreviewData ? _previewEntries[_previewIndex].IconUrl : "";
        public string PreviewStateText => HasPreviewData
            ? $"{_previewEntries[_previewIndex].TotalTimeText} played • last played {_previewEntries[_previewIndex].LastPlayedText}"
            : "";

        public ICommand CyclePreviewCommand => new RelayCommand(CyclePreview);

        private void CyclePreview()
        {
            if (_previewEntries.Count == 0)
                return;

            _previewIndex = (_previewIndex + 1) % _previewEntries.Count;

            OnPropertyChanged(nameof(PreviewGame));
            OnPropertyChanged(nameof(PreviewIconUrl));
            OnPropertyChanged(nameof(PreviewStateText));
        }

        private void AddIntegration()
        {
            CustomIntegrations.Add(new CustomIntegration()
            {
                Name = Strings.Menu_Integrations_Custom_NewIntegration
            });

            SelectedCustomIntegrationIndex = CustomIntegrations.Count - 1;

            OnPropertyChanged(nameof(SelectedCustomIntegrationIndex));
            OnPropertyChanged(nameof(IsCustomIntegrationSelected));
        }

        private void DeleteIntegration()
        {
            if (SelectedCustomIntegration is null)
                return;

            CustomIntegrations.Remove(SelectedCustomIntegration);

            if (CustomIntegrations.Count > 0)
            {
                SelectedCustomIntegrationIndex = CustomIntegrations.Count - 1;
                OnPropertyChanged(nameof(SelectedCustomIntegrationIndex));
            }

            OnPropertyChanged(nameof(IsCustomIntegrationSelected));
        }

        private void BrowseIntegrationLocation()
        {
            if (SelectedCustomIntegration is null)
                return;

            var dialog = new OpenFileDialog
            {
                Filter = $"{Strings.Menu_AllFiles}|*.*"
            };

            if (dialog.ShowDialog() != true)
                return;

            SelectedCustomIntegration.Name = dialog.SafeFileName;
            SelectedCustomIntegration.Location = dialog.FileName;
            OnPropertyChanged(nameof(SelectedCustomIntegration));
        }

        public bool WatchExternalLaunchesEnabled
        {
            get => App.Settings.Prop.WatchExternalLaunches;
            set
            {
                App.Settings.Prop.WatchExternalLaunches = value;

                if (value)
                    PhasmaStrap.Utility.RobloxSessionWatch.Start();

                OnPropertyChanged(nameof(WatchExternalLaunchesEnabled));
            }
        }

        public bool ActivityTrackingEnabled
        {
            get => App.Settings.Prop.EnableActivityTracking;
            set
            {
                App.Settings.Prop.EnableActivityTracking = value;

                if (!value)
                {
                    ShowServerDetailsEnabled = value;
                    DisableAppPatchEnabled = value;
                    DiscordActivityEnabled = value;
                    DiscordActivityJoinEnabled = value;

                    OnPropertyChanged(nameof(ShowServerDetailsEnabled));
                    OnPropertyChanged(nameof(DisableAppPatchEnabled));
                    OnPropertyChanged(nameof(DiscordActivityEnabled));
                    OnPropertyChanged(nameof(DiscordActivityJoinEnabled));
                }
            }
        }

        public bool ShowServerDetailsEnabled
        {
            get => App.Settings.Prop.ShowServerDetails;
            set => App.Settings.Prop.ShowServerDetails = value;
        }

        public bool DiscordActivityEnabled
        {
            get => App.Settings.Prop.UseDiscordRichPresence;
            set
            {
                App.Settings.Prop.UseDiscordRichPresence = value;

                if (!value)
                {
                    DiscordActivityJoinEnabled = value;
                    DiscordAccountOnProfile = value;
                    OnPropertyChanged(nameof(DiscordActivityJoinEnabled));
                    OnPropertyChanged(nameof(DiscordAccountOnProfile));
                }

                RefreshJoinPreview();
            }
        }

        public bool DiscordActivityJoinEnabled
        {
            get => !App.Settings.Prop.HideRPCButtons;
            set
            {
                App.Settings.Prop.HideRPCButtons = !value;
                OnPropertyChanged(nameof(DiscordActivityJoinEnabled));
                RefreshJoinPreview();
            }
        }

        public bool DiscordNativeJoin
        {
            get => App.Settings.Prop.DiscordNativeJoin;
            set
            {
                App.Settings.Prop.DiscordNativeJoin = value;
                RefreshJoinPreview();
            }
        }

        /// <summary>True when friends would see a Join button on your Discord profile right now.</summary>
        public bool JoinPreviewActive => DiscordActivityEnabled && DiscordActivityJoinEnabled;

        /// <summary>Discord's own button says "Ask to Join"; the PhasmaStrap one says "Join".</summary>
        public string JoinPreviewText => DiscordNativeJoin ? "Ask to Join" : "Join";

        /// <summary>Explains what friends see and what pressing the button does, since this preview cannot join anything itself.</summary>
        public string JoinPreviewTooltip
        {
            get
            {
                if (!DiscordActivityEnabled)
                    return "Your game activity is not shown on Discord, so friends see no Join button.";

                if (!DiscordActivityJoinEnabled)
                    return "Joining is off. Friends see what you are playing, but no Join button.";

                return DiscordNativeJoin
                    ? "Friends see Discord's own Ask to Join button. When you accept, Discord starts PhasmaStrap on their PC and it joins your server, so they need PhasmaStrap too."
                    : "Friends see a Join button on your profile. Pressing it opens Roblox straight into your server. This is only a preview, it does not join anything.";
            }
        }

        private void RefreshJoinPreview()
        {
            OnPropertyChanged(nameof(JoinPreviewActive));
            OnPropertyChanged(nameof(JoinPreviewText));
            OnPropertyChanged(nameof(JoinPreviewTooltip));
        }

        public bool DiscordAccountOnProfile
        {
            get => App.Settings.Prop.ShowAccountOnRichPresence;
            set => App.Settings.Prop.ShowAccountOnRichPresence = value;
        }

        public bool DiscordShowAsPhasmaStrap
        {
            get => App.Settings.Prop.DiscordShowAsPhasmaStrap;
            set { App.Settings.Prop.DiscordShowAsPhasmaStrap = value; OnPropertyChanged(nameof(DiscordShowAsPhasmaStrap)); }
        }

        public bool DisableAppPatchEnabled
        {
            get => App.Settings.Prop.UseDisableAppPatch;
            set => App.Settings.Prop.UseDisableAppPatch = value;
        }
        public ObservableCollection<CustomIntegration> CustomIntegrations
        {
            get => App.Settings.Prop.CustomIntegrations;
            set => App.Settings.Prop.CustomIntegrations = value;
        }

        public CustomIntegration? SelectedCustomIntegration { get; set; }
        public int SelectedCustomIntegrationIndex { get; set; }
        public bool IsCustomIntegrationSelected => SelectedCustomIntegration is not null;

        public ObservableCollection<RPCTemplate> RPCTemplates
        {
            get => App.Settings.Prop.RPCTemplates;
            set => App.Settings.Prop.RPCTemplates = value;
        }

        public RPCTemplate? SelectedRPCTemplate { get; set; }
        public int SelectedRPCTemplateIndex { get; set; }
        public bool IsRPCTemplateSelected => SelectedRPCTemplate is not null;

        private void AddRPCTemplate()
        {
            RPCTemplates.Add(new RPCTemplate());

            SelectedRPCTemplateIndex = RPCTemplates.Count - 1;

            OnPropertyChanged(nameof(SelectedRPCTemplateIndex));
            OnPropertyChanged(nameof(IsRPCTemplateSelected));
        }

        private void DeleteRPCTemplate()
        {
            if (SelectedRPCTemplate is null)
                return;

            RPCTemplates.Remove(SelectedRPCTemplate);

            if (RPCTemplates.Count > 0)
            {
                SelectedRPCTemplateIndex = RPCTemplates.Count - 1;
                OnPropertyChanged(nameof(SelectedRPCTemplateIndex));
            }

            OnPropertyChanged(nameof(IsRPCTemplateSelected));
        }
    }
}
