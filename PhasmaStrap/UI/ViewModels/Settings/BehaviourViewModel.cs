using System.Windows;
using System.Windows.Threading;

using PhasmaStrap.RobloxInterfaces;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public class BehaviourViewModel : NotifyPropertyChangedViewModel
    {
        public BehaviourViewModel()
        {
            Matchmaker.PropertyChanged += (_, _) => RefreshSummaries();

            OpenChannelCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => Go(typeof(PhasmaStrap.UI.Elements.Settings.Pages.ChannelPage)));
            OpenVersionsCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => Go(typeof(PhasmaStrap.UI.Elements.Settings.Pages.RobloxVersionsPage)));
            OpenModsCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => Go(typeof(PhasmaStrap.UI.Elements.Settings.Pages.ModsPage)));
            OpenModsFolderCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() =>
            {
                try
                {
                    Directory.CreateDirectory(Paths.Modifications);
                    Process.Start("explorer.exe", Paths.Modifications);
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine("BehaviourViewModel", $"Could not open the mods folder: {ex.Message}");
                }
            });
            CheckRobloxCommand = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(() => CheckRobloxAsync(force: true));

            LoadModSummary();
            _ = CheckRobloxAsync(force: false);
        }

        private static void Go(Type page) =>
            Application.Current?.Windows.OfType<PhasmaStrap.UI.Elements.Settings.MainWindow>().FirstOrDefault()?.Navigate(page);

        // ---- Roblox status: installed version, channel, and whether a newer one is out

        public System.Windows.Input.ICommand OpenChannelCommand { get; }
        public System.Windows.Input.ICommand OpenVersionsCommand { get; }
        public System.Windows.Input.ICommand CheckRobloxCommand { get; }

        private static DateTime? _lastCheckUtc;
        private static string _lastLatestGuid = "";
        private static string _lastCheckChannel = "";

        private static string ChannelName
        {
            get
            {
                string channel = App.Settings.Prop.RobloxChannel?.Trim() ?? "";
                return channel.Length == 0 ? Deployment.DefaultChannel : channel.ToLowerInvariant();
            }
        }

        /// <summary>production shows as LIVE, as Roblox calls it.</summary>
        private static string ChannelLabel => ChannelName.Equals(Deployment.DefaultChannel, StringComparison.OrdinalIgnoreCase) ? "LIVE" : ChannelName;

        public string ChannelButtonText => $"Channel: {ChannelLabel}";

        private bool _checking;
        public bool IsCheckingRoblox { get => _checking; private set { _checking = value; OnPropertyChanged(nameof(IsCheckingRoblox)); OnPropertyChanged(nameof(CanCheckRoblox)); } }
        public bool CanCheckRoblox => !_checking;

        private string _robloxStatusTitle = "Roblox";
        public string RobloxStatusTitle { get => _robloxStatusTitle; private set { _robloxStatusTitle = value; OnPropertyChanged(nameof(RobloxStatusTitle)); } }

        private string _robloxStatusDetail = "";
        public string RobloxStatusDetail { get => _robloxStatusDetail; private set { _robloxStatusDetail = value; OnPropertyChanged(nameof(RobloxStatusDetail)); } }

        /// <summary>good when up to date, warn when a newer one is out or nothing is installed, none while unknown.</summary>
        private string _robloxStatusTone = "none";
        public string RobloxStatusTone { get => _robloxStatusTone; private set { _robloxStatusTone = value; OnPropertyChanged(nameof(RobloxStatusTone)); } }

        private static string Ago(DateTime utc)
        {
            TimeSpan ago = DateTime.UtcNow - utc;
            if (ago.TotalMinutes < 1)
                return "just now";
            if (ago.TotalHours < 1)
                return (int)ago.TotalMinutes == 1 ? "1 minute ago" : $"{(int)ago.TotalMinutes} minutes ago";
            return (int)ago.TotalHours == 1 ? "1 hour ago" : $"{(int)ago.TotalHours} hours ago";
        }

        private void ShowRobloxStatus()
        {
            string installed = App.PlayerState.Prop.VersionGuid ?? "";
            string channel = ChannelLabel;
            string checkedText = _lastCheckUtc is DateTime at ? $" · checked {Ago(at)}" : "";
            bool sameChannel = _lastCheckChannel == ChannelName;

            OnPropertyChanged(nameof(ChannelButtonText));

            if (installed.Length == 0)
            {
                RobloxStatusTitle = "Roblox is not installed yet";
                RobloxStatusDetail = $"It installs from the {channel} channel the first time you press Play";
                RobloxStatusTone = "warn";
                return;
            }

            string mode = App.Settings.Prop.RobloxVersionMode;
            if (mode == RobloxVersions.ModePin || mode == RobloxVersions.ModeHold)
            {
                RobloxStatusTitle = mode == RobloxVersions.ModePin ? "Roblox is pinned to a version" : "Roblox is held on this version";
                RobloxStatusDetail = $"{installed} on the {channel} channel{checkedText}";
                RobloxStatusTone = "none";
                return;
            }

            if (!sameChannel || _lastLatestGuid.Length == 0)
            {
                RobloxStatusTitle = "Roblox is installed";
                RobloxStatusDetail = $"{installed} on the {channel} channel";
                RobloxStatusTone = "none";
                return;
            }

            if (_lastLatestGuid == installed)
            {
                RobloxStatusTitle = "Roblox is up to date";
                RobloxStatusDetail = $"{installed} on the {channel} channel{checkedText}";
                RobloxStatusTone = "good";
            }
            else
            {
                RobloxStatusTitle = "A newer Roblox is out";
                RobloxStatusDetail = $"{_lastLatestGuid} on the {channel} channel. It installs the next time you press Play{checkedText}";
                RobloxStatusTone = "warn";
            }
        }

        /// <summary>Asks Roblox's client settings service for the newest build on your channel (at most every 10 minutes unless forced).</summary>
        private async Task CheckRobloxAsync(bool force)
        {
            ShowRobloxStatus();

            if (_checking)
                return;

            if (!force && _lastCheckUtc is DateTime at && DateTime.UtcNow - at < TimeSpan.FromMinutes(10) && _lastCheckChannel == ChannelName)
                return;

            IsCheckingRoblox = true;

            try
            {
                string channel = ChannelName;
                string path = channel.Equals(Deployment.DefaultChannel, StringComparison.OrdinalIgnoreCase)
                    ? "/v2/client-version/WindowsPlayer"
                    : $"/v2/client-version/WindowsPlayer/channel/{Uri.EscapeDataString(channel)}";

                ClientVersion latest = await Http.GetJson<ClientVersion>("https://clientsettingscdn.roblox.com" + path);

                _lastLatestGuid = latest?.VersionGuid ?? "";
                _lastCheckChannel = channel;
                _lastCheckUtc = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("BehaviourViewModel", $"Could not check the latest Roblox: {ex.Message}");
                RobloxStatusDetail = $"{(App.PlayerState.Prop.VersionGuid ?? "")} on the {ChannelLabel} channel · could not check right now".TrimStart();
                return;
            }
            finally
            {
                IsCheckingRoblox = false;
            }

            ShowRobloxStatus();
        }

        // ---- When you press Play

        public string[] SettingsWindowOnLaunchOptions { get; } = { "Close it", "Minimise to tray", "Keep it open" };

        private static readonly string[] SettingsWindowOnLaunchKeys = { "Close", "Tray", "KeepOpen" };

        /// <summary>"Close PhasmaStrap": what this window does when you press Launch. Close it is how it always worked.</summary>
        public string SettingsWindowOnLaunch
        {
            get => SettingsWindowOnLaunchOptions[Math.Max(0, Array.IndexOf(SettingsWindowOnLaunchKeys, App.Settings.Prop.SettingsWindowOnLaunch))];
            set
            {
                int index = Array.IndexOf(SettingsWindowOnLaunchOptions, value);
                if (index < 0)
                    return;

                App.Settings.Prop.SettingsWindowOnLaunch = SettingsWindowOnLaunchKeys[index];
                OnPropertyChanged(nameof(SettingsWindowOnLaunch));
                OnPropertyChanged(nameof(SettingsWindowOnLaunchDescription));
            }
        }

        public string SettingsWindowOnLaunchDescription => App.Settings.Prop.SettingsWindowOnLaunch switch
        {
            "Tray" => "Stays in the tray while you play",
            "KeepOpen" => "Stays open next to the game",
            _ => "Closes when Roblox starts",
        };

        /// <summary>"Allow more than one Roblox": holds Roblox's singleton mutex so a new launch does not close the one running.</summary>
        public bool AllowMultipleRoblox
        {
            get => App.Settings.Prop.AllowMultipleRoblox;
            set { App.Settings.Prop.AllowMultipleRoblox = value; OnPropertyChanged(nameof(AllowMultipleRoblox)); RefreshSummaries(); }
        }

        /// <summary>"Ask which account": the saved accounts picker before each launch from PhasmaStrap.</summary>
        public bool AskAccountOnLaunch
        {
            get => App.Settings.Prop.AskAccountOnLaunch;
            set { App.Settings.Prop.AskAccountOnLaunch = value; OnPropertyChanged(nameof(AskAccountOnLaunch)); }
        }

        /// <summary>"Confirm before joining": asks before a link from the Roblox website starts a game.</summary>
        public bool ConfirmWebsiteJoins
        {
            get => App.Settings.Prop.ConfirmWebsiteJoins;
            set { App.Settings.Prop.ConfirmWebsiteJoins = value; OnPropertyChanged(nameof(ConfirmWebsiteJoins)); }
        }

        /// <summary>"Fullscreen on launch": turns on Roblox's Fullscreen setting before it starts.</summary>
        public bool FullscreenOnLaunch
        {
            get => App.Settings.Prop.FullscreenOnLaunch;
            set { App.Settings.Prop.FullscreenOnLaunch = value; OnPropertyChanged(nameof(FullscreenOnLaunch)); }
        }

        /// <summary>
        /// "Remove the 60 Hz cap on menus": the same FastFlag preset as the frame rate limit on Performance (Rendering.Framerate,
        /// DFIntTaskSchedulerTargetFps). On sets it to this display's refresh rate (at least 120, at most 240); off clears it.
        /// </summary>
        public bool RemoveFrameRateCap
        {
            get => int.TryParse(App.FastFlags.GetPreset("Rendering.Framerate"), out int fps) && fps > 60;
            set
            {
                if (value == RemoveFrameRateCap)
                    return;

                if (value)
                {
                    int refresh = 0;
                    try { refresh = DisplaySystem.GetCurrentMode(null)?.RefreshRate ?? 0; } catch { }

                    int target = Math.Clamp(refresh, 120, 240);
                    App.FastFlags.SetPreset("Rendering.Framerate", target.ToString());
                    App.FastFlags.SetPreset("Rendering.LimitFramerate", null);
                }
                else
                {
                    App.FastFlags.SetPreset("Rendering.Framerate", null);
                    App.FastFlags.SetPreset("Rendering.LimitFramerate", null);
                }

                OnPropertyChanged(nameof(RemoveFrameRateCap));
                OnPropertyChanged(nameof(FrameRateCapDescription));
            }
        }

        public string FrameRateCapDescription => int.TryParse(App.FastFlags.GetPreset("Rendering.Framerate"), out int fps) && fps > 0
            ? $"Smoother loading screens. Capped at {fps} FPS now."
            : "Smoother loading screens";

        // ---- Mods summary: what is swapped in every time Roblox starts (read only, the Mods page changes them)

        public System.Windows.Input.ICommand OpenModsCommand { get; }
        public System.Windows.Input.ICommand OpenModsFolderCommand { get; }

        public string ModFontText { get; private set; } = "Roblox default";
        public string ModCursorText { get; private set; } = "Roblox default";
        public string ModDeathSoundText { get; private set; } = "Roblox default";

        private void LoadModSummary()
        {
            try
            {
                if (File.Exists(Paths.CustomFont))
                {
                    string family = "";
                    try
                    {
                        family = System.Windows.Media.Fonts.GetFontFamilies(new Uri(Paths.CustomFont)).FirstOrDefault()?.FamilyNames.Values.FirstOrDefault() ?? "";
                    }
                    catch { }

                    ModFontText = family.Length > 0 ? family : "Custom font";
                }

                string cursorFolder = Path.Combine(Paths.Modifications, @"content\textures\Cursors\KeyboardMouse");
                if (File.Exists(Path.Combine(cursorFolder, "ArrowCursor.png")))
                {
                    if (new Models.Entities.ModPresetFileData(@"content\textures\Cursors\KeyboardMouse\ArrowCursor.png", "Cursor.From2013.ArrowCursor.png").HashMatches())
                        ModCursorText = "2013 classic";
                    else if (new Models.Entities.ModPresetFileData(@"content\textures\Cursors\KeyboardMouse\ArrowCursor.png", "Cursor.From2006.ArrowCursor.png").HashMatches())
                        ModCursorText = "2006 classic";
                    else
                        ModCursorText = "Custom cursor";
                }

                if (File.Exists(Paths.CustomDeathSound))
                    ModDeathSoundText = "Custom sound";
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("BehaviourViewModel", $"Could not read the mods summary: {ex.Message}");
            }
        }

        public bool ConfirmLaunches
        {
            get => App.Settings.Prop.ConfirmLaunches;
            set
            {
                App.Settings.Prop.ConfirmLaunches = value;
                RefreshSummaries();
            }
        }

        public IEnumerable<CleanerOptions> CleanerScheduleOptions { get; } = Enum.GetValues(typeof(CleanerOptions)).Cast<CleanerOptions>();

        public CleanerOptions CleanerSchedule
        {
            get => App.Settings.Prop.CleanerOptions;
            set
            {
                App.Settings.Prop.CleanerOptions = value;
                RefreshSummaries();
            }
        }

        public IEnumerable<string> CleanerAvailableDirectories { get; } = Cleaner.Directories.Keys;

        public bool CleanerLogsEnabled
        {
            get => App.Settings.Prop.CleanerDirectories.Contains("PhasmaStrapLogs");
            set => SetCleanerDirectory("PhasmaStrapLogs", value);
        }

        public bool CleanerCacheEnabled
        {
            get => App.Settings.Prop.CleanerDirectories.Contains("PhasmaStrapCache");
            set => SetCleanerDirectory("PhasmaStrapCache", value);
        }

        public bool CleanerRobloxLogsEnabled
        {
            get => App.Settings.Prop.CleanerDirectories.Contains("RobloxLogs");
            set => SetCleanerDirectory("RobloxLogs", value);
        }

        public bool CleanerRobloxCacheEnabled
        {
            get => App.Settings.Prop.CleanerDirectories.Contains("RobloxCache");
            set => SetCleanerDirectory("RobloxCache", value);
        }

        private static void SetCleanerDirectory(string key, bool enabled)
        {
            var directories = App.Settings.Prop.CleanerDirectories;

            if (enabled && !directories.Contains(key))
                directories.Add(key);
            else if (!enabled)
                directories.Remove(key);
        }

        public bool OptimizeRoblox
        {
            get => App.Settings.Prop.OptimizeRoblox;
            set
            {
                App.Settings.Prop.OptimizeRoblox = value;
                RefreshSummaries();
            }
        }

        public bool RobloxEfficiencyMode
        {
            get => App.Settings.Prop.RobloxEfficiencyMode;
            set
            {
                App.Settings.Prop.RobloxEfficiencyMode = value;
                RefreshSummaries();
            }
        }

        public bool ReduceMemoryOutOfFocus
        {
            get => App.Settings.Prop.ReduceMemoryOutOfFocus;
            set
            {
                App.Settings.Prop.ReduceMemoryOutOfFocus = value;
                RefreshSummaries();
            }
        }

        public IEnumerable<string> CpuPriorityOptions => BuildCpuPriorityOptions();

        public string SelectedCpuPriority
        {
            get => App.Settings.Prop.SelectedCpuPriority;
            set
            {
                App.Settings.Prop.SelectedCpuPriority = value;
                RefreshSummaries();
            }
        }

        public string[] RobloxPriorityLimitOptions { get; } = { "Idle", "Below Normal", "Normal", "Above Normal", "High", "Realtime" };

        public string RobloxPriorityLimit
        {
            get => App.Settings.Prop.RobloxPriorityLimit;
            set
            {
                string previous = App.Settings.Prop.RobloxPriorityLimit;
                App.Settings.Prop.RobloxPriorityLimit = value;
                RefreshSummaries();

                if (value.Equals("Realtime", StringComparison.OrdinalIgnoreCase))
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        MessageBoxResult result = Frontend.ShowMessageBox(
                            "Realtime priority makes Roblox preempt almost everything else on your system, including your mouse and keyboard driver. If Roblox spikes CPU usage even briefly, your whole system can freeze or become unresponsive until it settles down. This is not recommended unless you know exactly what you're doing.\n\nSet Roblox to Realtime priority anyway?",
                            MessageBoxImage.Warning,
                            MessageBoxButton.YesNo,
                            MessageBoxResult.No);

                        if (result != MessageBoxResult.Yes && App.Settings.Prop.RobloxPriorityLimit.Equals("Realtime", StringComparison.OrdinalIgnoreCase))
                        {
                            App.Settings.Prop.RobloxPriorityLimit = previous;
                            OnPropertyChanged(nameof(RobloxPriorityLimit));
                            RefreshSummaries();
                        }
                    }), DispatcherPriority.Background);
                }
            }
        }

        public bool LauncherMemoryManagerEnabled
        {
            get => App.Settings.Prop.LauncherMemoryManagerEnabled;
            set
            {
                App.Settings.Prop.LauncherMemoryManagerEnabled = value;
                if (value)
                    MemoryManager.Start();
                else
                    MemoryManager.Shutdown();
            }
        }

        public bool SoftwareRenderingEnabled
        {
            get => App.Settings.Prop.WPFSoftwareRender;
            set
            {
                App.Settings.Prop.WPFSoftwareRender = value;
                RenderAcceleration.ApplyProcess();
            }
        }

        private static IEnumerable<string> BuildCpuPriorityOptions()
        {
            List<string> options = new() { "Automatic" };
            int processorCount = Environment.ProcessorCount;
            if (processorCount <= IntPtr.Size * 8)
            {
                for (int i = 1; i <= processorCount; i++)
                    options.Add($"{i} Core{(i > 1 ? "s" : "")}");
            }
            return options;
        }

        public string[] EnginePresetNames => Integrations.EnginePresets.PresetNames;

        public string SelectedEnginePreset
        {
            get => "";
            set
            {
                if (string.IsNullOrEmpty(value) || !Integrations.EnginePresets.Presets.TryGetValue(value, out var preset))
                    return;

                Integrations.EnginePresets.Apply(preset, App.Settings.Prop);

                OnPropertyChanged(nameof(OptimizeRoblox));
                OnPropertyChanged(nameof(RobloxEfficiencyMode));
                OnPropertyChanged(nameof(ReduceMemoryOutOfFocus));
                OnPropertyChanged(nameof(SelectedCpuPriority));
                OnPropertyChanged(nameof(RobloxPriorityLimit));
                RefreshSummaries();
            }
        }

        public bool DisableRobloxCrashHandler
        {
            get => App.Settings.Prop.DisableRobloxCrashHandler;
            set
            {
                App.Settings.Prop.DisableRobloxCrashHandler = value;
                RefreshSummaries();
            }
        }

        public bool BackgroundUpdates
        {
            get => App.Settings.Prop.BackgroundUpdatesEnabled;
            set
            {
                App.Settings.Prop.BackgroundUpdatesEnabled = value;
                RefreshSummaries();
            }
        }

        public bool IsRobloxInstallationMissing => !App.IsPlayerInstalled && !App.IsStudioInstalled;

        public bool ForceRobloxReinstallation
        {
            get => App.State.Prop.ForceReinstall || IsRobloxInstallationMissing;
            set
            {
                App.State.Prop.ForceReinstall = value;
                RefreshSummaries();
            }
        }

        public ServerBrowserViewModel Matchmaker { get; } = new();

        public string RobloxTitle
        {
            get => App.Settings.Prop.RobloxTitle;
            set
            {
                App.Settings.Prop.RobloxTitle = value ?? "";
                RefreshSummaries();
            }
        }

        public bool CycleTitleWithGameName
        {
            get => App.Settings.Prop.CycleTitleWithGameName;
            set => App.Settings.Prop.CycleTitleWithGameName = value;
        }

        public bool ShowServerInfoInTitle
        {
            get => App.Settings.Prop.ShowServerInfoInTitle;
            set => App.Settings.Prop.ShowServerInfoInTitle = value;
        }

        public bool UseGameIconForRobloxWindow
        {
            get => App.Settings.Prop.UseGameIconForRobloxWindow;
            set => App.Settings.Prop.UseGameIconForRobloxWindow = value;
        }

        public string CurrentEnginePresetName
        {
            get
            {
                Integrations.EnginePresetValues current = Integrations.EnginePresets.FromSettings(App.Settings.Prop);

                foreach (KeyValuePair<string, Integrations.EnginePresetValues> preset in Integrations.EnginePresets.Presets)
                {
                    if (preset.Value == current)
                        return preset.Key;
                }

                return "Custom";
            }
        }

        public string LaunchSummaryConfirm => AllowMultipleRoblox
            ? "A new launch opens another Roblox next to the one already running."
            : ConfirmLaunches
                ? "You will be asked to confirm before each launch."
                : "Launches start straight away, with no confirmation prompt.";

        public string LaunchSummaryUpdates
        {
            get
            {
                if (ForceRobloxReinstallation)
                    return "Roblox will be reinstalled from scratch before the game opens.";

                return BackgroundUpdates
                    ? "Roblox will start on the version you already have, and a newer one is fetched in the background."
                    : "Roblox will be brought up to date first if a newer version is out.";
            }
        }

        public string LaunchSummaryMatchmaker
        {
            get
            {
                if (!Matchmaker.MatchmakerEnabled)
                    return "The matchmaker is off, so Roblox picks the server for you.";

                string key = Matchmaker.PreferredDatacenter;
                string region = String.IsNullOrEmpty(key)
                    ? "closest to you"
                    : $"in {Matchmaker.DatacenterChoices.FirstOrDefault(choice => choice.Key == key)?.Display ?? key}";

                string candidates = Matchmaker.MatchmakerAutoCandidates
                    ? "as many candidates as it needs"
                    : $"up to {Matchmaker.MatchmakerMaxCandidates} candidates";

                string empty = Matchmaker.MatchmakerPreferEmpty ? ", preferring emptier ones" : "";

                return $"The matchmaker will pick a server {region}, looking at {candidates}{empty}.";
            }
        }

        public string LaunchSummaryProcess =>
            $"Roblox will run with the {CurrentEnginePresetName.ToLowerInvariant()} process preset, at {RobloxPriorityLimit.ToLowerInvariant()} priority.";

        public string LaunchSummaryRejoin => Matchmaker.AutoRejoinOnCrash
            ? $"If Roblox crashes, PhasmaStrap will rejoin up to {Matchmaker.AutoRejoinMaxAttempts} times, {Matchmaker.AutoRejoinDelaySeconds} seconds apart."
            : "If Roblox crashes, PhasmaStrap will not rejoin for you.";

        public string LaunchingGroupSummary =>
            $"Launch confirmation and the Roblox crash handler. Confirmation is {(ConfirmLaunches ? "on" : "off")}, the crash handler is {(DisableRobloxCrashHandler ? "disabled" : "left alone")}.";

        public string UpdatesGroupSummary =>
            $"Background updates, forced reinstalls, the install location, installed versions and the update channel. Background updates are {(BackgroundUpdates ? "on" : "off")}.";

        public string CleanupGroupSummary =>
            $"Which logs and caches are deleted, and how often. {(CleanerSchedule == CleanerOptions.Never ? "Nothing is cleaned automatically" : "Cleaning runs on a schedule")}.";

        public string ProcessGroupSummary =>
            $"Process preset, CPU priority, memory handling and software rendering. Currently {CurrentEnginePresetName.ToLowerInvariant()}, at {RobloxPriorityLimit.ToLowerInvariant()} priority.";

        public string MatchmakerGroupSummary =>
            $"Server picking, excluded datacenters and places, and rejoining after a crash. The matchmaker is {(Matchmaker.MatchmakerEnabled ? "on" : "off")}.";

        public string WindowTitleGroupSummary =>
            $"The Roblox window title, game name cycling, server info and the window icon. Title: {(String.IsNullOrWhiteSpace(RobloxTitle) ? "Roblox" : RobloxTitle)}.";

        private void RefreshSummaries()
        {
            OnPropertyChanged(nameof(CurrentEnginePresetName));
            OnPropertyChanged(nameof(LaunchSummaryConfirm));
            OnPropertyChanged(nameof(LaunchSummaryUpdates));
            OnPropertyChanged(nameof(LaunchSummaryMatchmaker));
            OnPropertyChanged(nameof(LaunchSummaryProcess));
            OnPropertyChanged(nameof(LaunchSummaryRejoin));
            OnPropertyChanged(nameof(LaunchingGroupSummary));
            OnPropertyChanged(nameof(UpdatesGroupSummary));
            OnPropertyChanged(nameof(CleanupGroupSummary));
            OnPropertyChanged(nameof(ProcessGroupSummary));
            OnPropertyChanged(nameof(MatchmakerGroupSummary));
            OnPropertyChanged(nameof(WindowTitleGroupSummary));
        }
    }
}
