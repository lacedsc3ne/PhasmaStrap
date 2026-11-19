using PhasmaStrap.AppData;
using PhasmaStrap.Integrations;
using PhasmaStrap.Integrations.GameChat;
using PhasmaStrap.Integrations.Overlays;
using PhasmaStrap.Models;
using PhasmaStrap.UI;
using PhasmaStrap.Utility;

namespace PhasmaStrap
{
    public class Watcher : IDisposable
    {
        private readonly InterProcessLock _lock = new("Watcher");

        private readonly WatcherData? _watcherData;

        private readonly NotifyIconWrapper? _notifyIcon;

        public readonly ActivityWatcher? ActivityWatcher;

        public readonly DiscordRichPresence? RichPresence;

        public readonly IntegrationWatcher? IntegrationWatcher;

        public readonly PlayTimeWatcher? PlayTimeWatcher;

        public readonly SessionTracker? SessionTracker;

        private readonly PerformanceMeasurer? _performanceMeasurer;

        public readonly GameChatIntegration? GameChat;

        private readonly GlobalHotkeyManager? _hotkeys;

        private readonly InstantReplayRecorder _instantReplay = new();

        private string _replaySignature = ReplaySignature();

        private EnginePresetValues? _optimizerValues;

        private RobloxProcessOptimizer? _processOptimizer;

        public Watcher()
        {
            const string LOG_IDENT = "Watcher";

            if (!_lock.IsAcquired)
            {
                App.Logger.WriteLine(LOG_IDENT, "Watcher instance already exists");
                return;
            }

            string? watcherDataArg = App.LaunchSettings.WatcherFlag.Data;

            if (String.IsNullOrEmpty(watcherDataArg))
            {
#if DEBUG
                string path = new RobloxPlayerData().ExecutablePath;
                if (!File.Exists(path))
                    throw new ApplicationException("Roblox player is not been installed");

                using var gameClientProcess = Process.Start(path);

                _watcherData = new() { ProcessId = gameClientProcess.Id };
#else
                throw new Exception("Watcher data not specified");
#endif
            }
            else
            {
                _watcherData = JsonSerializer.Deserialize<WatcherData>(Encoding.UTF8.GetString(Convert.FromBase64String(watcherDataArg)));
            }

            if (_watcherData is null)
                throw new Exception("Watcher data is invalid");

            if (App.Settings.Prop.EnableActivityTracking)
            {
                ActivityWatcher = new(_watcherData.LogFile);
                NowPlaying.Follow(ActivityWatcher);
                Utility.Backend.GameServerReporter.Follow(ActivityWatcher);
                Utility.PerformanceSessionRecorder.Follow(ActivityWatcher);

                ActivityWatcher.OnGameJoin += (sender, _) => OverlayHub.OnGameJoin((sender as ActivityWatcher)?.Data.PlaceId ?? 0);
                ActivityWatcher.OnGameLeave += delegate { OverlayHub.OnGameLeave(); };

                ActivityWatcher.OnGameJoin += (sender, _) =>
                {
                    if (App.Settings.Prop.OverlayHudShowPing || App.Settings.Prop.OverlayHudShowRegion)
                        ServerPingMonitor.Start((sender as ActivityWatcher)?.Data.MachineAddress);
                };
                ActivityWatcher.OnGameLeave += (_, _) => ServerPingMonitor.Stop();

                ActivityWatcher.OnGameJoin += (sender, args) =>
                {
                    if (sender is ActivityWatcher joined && App.Settings.Prop.PartyEnabled)
                    {
                        Utility.PartyService.RecordPresence(joined.Data.PlaceId, joined.Data.JobId, NowPlaying.GameName());
                        _ = Utility.PartyService.ReportLaunchAsync(joined.Data.PlaceId, joined.Data.JobId);
                    }
                };

                ActivityWatcher.OnConnectionDropped += (_, dropped) =>
                {
                    Task rejoin = RejoinAfterDropAsync(dropped);

                    if (!rejoin.IsCompleted)
                        _rejoinAfterDropTask = rejoin;
                };
                Utility.ServerRegion.Uptime = () => ActivityWatcher.Data.ServerUptimeText;

                NowPlaying.NameResolved += (_, _) =>
                {
                    if (!App.Settings.Prop.PartyEnabled || !ActivityWatcher.InGame)
                        return;

                    Utility.PartyService.RecordPresence(ActivityWatcher.Data.PlaceId, ActivityWatcher.Data.JobId, NowPlaying.GameName());
                };

                ActivityWatcher.OnGameJoin += (sender, _) => ServerRegion.OnGameJoin((sender as ActivityWatcher)?.Data.MachineAddress);
                ActivityWatcher.OnGameLeave += (_, _) => ServerRegion.OnGameLeave();
                ActivityWatcher.OnGameLeave += (_, _) => Utility.PartyService.RecordPresence(0, "", "");

                if (App.Settings.Prop.UseDisableAppPatch)
                {
                    ActivityWatcher.OnAppClose += delegate
                    {
                        App.Logger.WriteLine(LOG_IDENT, "Received desktop app exit, closing Roblox");

                        try
                        {
                            using var process = Process.GetProcessById(_watcherData.ProcessId);
                            process.CloseMainWindow();
                        }
                        catch (Exception ex)
                        {
                            App.Logger.WriteLine(LOG_IDENT, $"Could not close Roblox ({_watcherData.ProcessId}): {ex.Message}");
                        }
                    };
                }

                if (App.Settings.Prop.UseDiscordRichPresence)
                    RichPresence = new(ActivityWatcher);

                if (App.Settings.Prop.GameChatEnabled)
                    GameChat = new(ActivityWatcher, _watcherData.ProcessId);

                if (App.Settings.Prop.CustomIntegrations.Count > 0)
                    IntegrationWatcher = new(ActivityWatcher);

                PlayTimeWatcher = new(ActivityWatcher);

                if (App.Settings.Prop.SessionHistoryEnabled)
                    SessionTracker = new(ActivityWatcher);

                _performanceMeasurer = new(ActivityWatcher);

                ActivityWatcher.OnGameJoin += (_, _) => FakeExclusiveFullscreen.OnGameJoin();
                ActivityWatcher.OnGameLeave += (_, _) => FakeExclusiveFullscreen.OnGameLeave();

                ActivityWatcher.OnGameJoin += (_, _) =>
                {
                    if (App.Settings.Prop.DuckRobloxAudioOnUnfocus)
                        AudioDucker.Start();
                };
                ActivityWatcher.OnGameLeave += (_, _) => AudioDucker.Stop();

                ActivityWatcher.OnGameJoin += (_, _) => HeadsetAudio.ApplyFromSettings();
                ActivityWatcher.OnGameLeave += (_, _) => HeadsetAudio.Stop();

                ActivityWatcher.OnGameJoin += (_, _) =>
                {
                    if (App.Settings.Prop.InstantReplayEnabled)
                        _instantReplay.Start();
                };
                ActivityWatcher.OnGameLeave += (_, _) => _instantReplay.Stop();

                _ = Task.Run(() =>
                {
                    try { Utility.RobloxVersions.ShowPendingFlagNotice(); }
                    catch (Exception ex) { App.Logger.WriteLine("Watcher", $"Flag notice failed: {ex.Message}"); }

                    Utility.AccountGuard.ScanAndNotify();
                });

                ActivityWatcher.OnGameJoin += (sender, _) =>
                {
                    if (sender is ActivityWatcher watcher)
                        Utility.FlagProfileSession.OnGameJoined(watcher.Data);
                };

                ActivityWatcher.OnGameJoin += (sender, _) => ForcedResolution.OnGameJoin((sender as ActivityWatcher)?.Data.PlaceId ?? 0);
                ActivityWatcher.OnGameLeave += (_, _) => ForcedResolution.OnGameLeave();

                ActivityWatcher.OnGameJoin += (_, _) => StartProcessOptimizer();
                ActivityWatcher.OnGameLeave += (_, _) => StopProcessOptimizer();

                if (App.Settings.Prop.LauncherMemoryManagerEnabled)
                {
                    MemoryManager.Start();
                    ActivityWatcher.OnGameJoin += (_, _) => MemoryManager.SetGameplayActive(true);
                    ActivityWatcher.OnGameLeave += (_, _) => MemoryManager.SetGameplayActive(false);
                }

                ActivityWatcher.OnGameJoin += (_, _) => SystemPerformanceBoost.OnGameJoin();
                ActivityWatcher.OnGameLeave += (_, _) => SystemPerformanceBoost.OnGameLeave();
            }

            if (App.Settings.Prop.DisableRobloxCrashHandler)
                _ = Utility.CrashHandlerStopper.RunAsync(Utility.PartyLauncher.RobloxRunning);

            if (App.Settings.Prop.SoftKeyEnabled)
            {
                _softKey = new SoftKey(_watcherData.ProcessId, App.Settings.Prop.SoftKeyProfile);
                _softKey.Start();
            }

            if (RobloxWindowCustomizer.IsEnabled)
                RobloxWindowCustomizer.Start(ActivityWatcher);

            _hotkeys = new GlobalHotkeyManager();
            _hotkeys.RegisterAction(HotkeyActions.CleanRamNow, CleanRamNow);
            _hotkeys.RegisterAction(HotkeyActions.ToggleHeadsetAudio, ToggleHeadsetAudio);
            _hotkeys.RegisterAction(HotkeyActions.TakeScreenshot, TakeScreenshot);
            _hotkeys.RegisterAction(HotkeyActions.SaveInstantReplay, SaveInstantReplay);
            _hotkeys.RegisterAction(HotkeyActions.ToggleOverlayFocusMode, ToggleOverlayFocusMode);
            _hotkeys.ApplyBindings();

            Utility.SettingsHotReload.Reloaded += (_, _) => OnSettingsReloaded();

            _notifyIcon = new(this);

            LauncherThrottle.StartForSession(_watcherData.ProcessId, () =>
                _instantReplay.IsRunning || HeadsetAudio.IsRunning || LauncherThrottle.CompositorDrawingGame());
        }

        private void OnSettingsReloaded()
        {
            const string LOG_IDENT = "Watcher::SettingsReloaded";

            void Try(string what, Action action)
            {
                try { action(); }
                catch (Exception ex) { App.Logger.WriteLine(LOG_IDENT, $"{what} failed: {ex.Message}"); }
            }

            Try("Hotkey re-apply", () => App.Current.Dispatcher.BeginInvoke(() => _hotkeys?.ApplyBindings()));

            if (ActivityWatcher is null)
                return;

            Settings settings = App.Settings.Prop;
            bool inGame = ActivityWatcher.InGame;

            Try("Overlay refresh", () => OverlayHub.Refresh());

            Try("Instant Replay state update", () =>
            {
                string signature = ReplaySignature();
                bool changed = signature != _replaySignature;
                _replaySignature = signature;

                if (!settings.InstantReplayEnabled || !inGame)
                {
                    if (_instantReplay.IsRunning)
                        _instantReplay.Stop();
                    return;
                }

                if (_instantReplay.IsRunning && changed)
                    _instantReplay.Stop();

                if (!_instantReplay.IsRunning)
                    _instantReplay.Start();
            });

            if (!inGame)
                return;

            Try("Ping monitor update", () =>
            {
                if (settings.OverlayHudShowPing || settings.OverlayHudShowRegion)
                {
                    if (!ServerPingMonitor.IsRunning)
                        ServerPingMonitor.Start(ActivityWatcher.Data.MachineAddress);
                }
                else
                    ServerPingMonitor.Stop();
            });

            Try("Fullscreen update", () =>
            {
                if (FakeExclusiveFullscreen.Enabled != FakeExclusiveFullscreen.IsApplied)
                    FakeExclusiveFullscreen.OnGameJoin();
            });

            Try("Audio ducking update", () =>
            {
                if (settings.DuckRobloxAudioOnUnfocus)
                {
                    if (!AudioDucker.IsRunning)
                        AudioDucker.Start();
                }
                else if (AudioDucker.IsRunning)
                {
                    AudioDucker.Stop();
                }
            });

            Try("Headset audio update", HeadsetAudio.ApplyFromSettings);

            Try("System boost update", () =>
            {
                if (settings.BoostTimerResolution || settings.UseHighPerformancePowerPlan)
                    SystemPerformanceBoost.OnGameJoin();
                else
                    SystemPerformanceBoost.EndSession();
            });

            Try("Process optimizer update", () =>
            {
                if (_processOptimizer is null || _optimizerValues != EffectivePreset())
                {
                    StopProcessOptimizer();
                    StartProcessOptimizer();
                }
                else if (!RobloxProcessOptimizer.ShouldRun(settings) && settings.EnginePlaceProfiles.Count == 0)
                {
                    StopProcessOptimizer();
                }
            });
        }

        private static string ReplaySignature()
        {
            Settings settings = App.Settings.Prop;
            return string.Join("|", typeof(Settings).GetProperties()
                .Where(property => property.Name.StartsWith("InstantReplay", StringComparison.Ordinal) && property.Name != nameof(Settings.InstantReplayEnabled))
                .Select(property => $"{property.Name}={property.GetValue(settings)}"));
        }

        private void StartProcessOptimizer()
        {
            if (_watcherData is null)
                return;

            if (!RobloxProcessOptimizer.ShouldRun(App.Settings.Prop) && App.Settings.Prop.EnginePlaceProfiles.Count == 0)
                return;

            EnginePresetValues effective = EffectivePreset();

            _optimizerValues = effective;
            _processOptimizer ??= new RobloxProcessOptimizer(_watcherData.ProcessId, effective);
            _processOptimizer.Start();
        }

        private EnginePresetValues EffectivePreset()
        {
            long placeId = ActivityWatcher?.Data.PlaceId ?? 0;
            return placeId != 0 ? EnginePresets.Resolve(placeId) : EnginePresets.FromSettings(App.Settings.Prop);
        }

        private void StopProcessOptimizer()
        {
            _processOptimizer?.Dispose();
            _processOptimizer = null;
        }

        public int RobloxProcessId => _watcherData?.ProcessId ?? 0;

        public bool InstantReplayRunning => _instantReplay.IsRunning;

        public void CleanRamNow()
        {
            SystemMemoryCleaner.TrimResult result = SystemMemoryCleaner.TrimAllProcessWorkingSets(true);
            NotificationCenter.Notify(
                "RAM cleaned",
                $"Trimmed {result.ProcessesTrimmed} processes (~{result.BytesFreed / 1048576.0:0.#} MB).",
                NotificationCategory.General,
                kind: NotificationKindId.RamCleaned);
        }

        public void ToggleHeadsetAudio()
        {
            ReloadSettingsIfChanged();

            bool enabled = !App.Settings.Prop.HeadsetAudioEnabled;
            App.Settings.Prop.HeadsetAudioEnabled = enabled;
            App.Settings.SaveFields(nameof(App.Settings.Prop.HeadsetAudioEnabled));

            if (enabled)
                HeadsetAudio.Start();
            else
                HeadsetAudio.Stop();
        }

        private static void ReloadSettingsIfChanged()
        {
            try
            {
                if (File.Exists(App.Settings.FileLocation) && App.Settings.HasFileOnDiskChanged())
                {
                    App.Logger.WriteLine("Watcher::ReloadSettingsIfChanged", "Settings changed on disk, reloading before saving a hotkey change");
                    App.Settings.Load(alertFailure: false);
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("Watcher::ReloadSettingsIfChanged", $"Could not check the settings file: {ex.Message}");
            }
        }

        public void TakeScreenshot()
        {
            App.SendStat("capture", "screenshot");
            ScreenshotTaker.Take(ScreenshotTaken);
        }

        private void ScreenshotTaken(string? path, bool copied)
        {
            NotificationCenter.Notify(
                path is null ? "Screenshot failed" : copied ? "Screenshot saved and copied" : "Screenshot saved",
                path is not null ? Path.GetFileName(path) : "Could not find the Roblox window.",
                NotificationCategory.General,
                onClick: path is not null ? NotificationCenter.RevealFile(path) : null,
                actionText: path is not null ? "Edit" : null,
                action: path is not null ? () => Utility.CaptureLibrary.OpenEditor(path) : null,
                kind: NotificationKindId.Screenshot);
        }

        private static void TidyCaptures() => ScreenshotTaker.TidyCaptures();

        private static bool CopyCapture(string path, bool image) => ScreenshotTaker.CopyCapture(path, image);

        private int _replaySaving;

        public void SaveInstantReplay()
        {
            App.SendStat("capture", "instantReplay");

            const string LOG_IDENT = "Watcher::SaveInstantReplay";

            if (!App.Settings.Prop.InstantReplayEnabled)
            {
                NotificationCenter.Notify("Instant Replay is off", "Turn it on under Capture > Instant Replay - it starts buffering as soon as you're in a game.", NotificationCategory.General, kind: NotificationKindId.ReplayNotReady);
                return;
            }

            if (!_instantReplay.IsRunning)
            {
                if (ActivityWatcher?.InGame == true)
                {
                    _instantReplay.Start();
                    NotificationCenter.Notify("Instant Replay just started", "It's buffering now - press the hotkey again in a few seconds to save a clip.", NotificationCategory.General, kind: NotificationKindId.ReplayNotReady);
                }
                else
                {
                    NotificationCenter.Notify("Not in a game yet", "Instant Replay only buffers while you're in a Roblox game.", NotificationCategory.General, kind: NotificationKindId.ReplayNotReady);
                }
                return;
            }

            if (Interlocked.CompareExchange(ref _replaySaving, 1, 0) != 0)
            {
                NotificationCenter.Notify("Still saving the last clip", "Give it a moment before pressing the hotkey again.", NotificationCategory.General, kind: NotificationKindId.ReplayNotReady);
                return;
            }

            int seconds = App.Settings.Prop.InstantReplayClipSeconds;
            if (!_instantReplay.OnGpu)
                NotificationCenter.Notify("Saving replay...", $"Encoding the last {seconds}s to MP4 - this takes a few seconds.", NotificationCategory.General, 4, kind: NotificationKindId.Replay);
            App.Logger.WriteLine(LOG_IDENT, "Encoding clip on a background thread");

            _ = Task.Run(() =>
            {
                string? path = null;
                string? error = null;

                try
                {
                    path = _instantReplay.SaveClip();
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    App.Logger.WriteException(LOG_IDENT, ex);
                }
                finally
                {
                    Interlocked.Exchange(ref _replaySaving, 0);
                }

                bool copied = path is not null && App.Settings.Prop.CaptureCopyReplayToClipboard && CopyCapture(path, image: false);

                if (path is not null)
                    TidyCaptures();

                App.Current.Dispatcher.BeginInvoke(() =>
                {
                    NotificationCenter.Notify(
                        path is null ? "Replay failed" : copied ? "Replay saved and copied" : "Replay saved",
                        path is not null ? Path.GetFileName(path) : (error ?? "Nothing was buffered yet - check the log for details."),
                        NotificationCategory.General, 6,
                        onClick: path is not null ? NotificationCenter.RevealFile(path) : null,
                        actionText: path is not null ? "Edit" : null,
                        action: path is not null ? () => Utility.CaptureLibrary.OpenEditor(path) : null,
                        kind: NotificationKindId.Replay);
                });
            });
        }

        public void ToggleOverlayFocusMode()
        {
            ReloadSettingsIfChanged();

            bool enabled = !App.Settings.Prop.OverlayFocusModeEnabled;
            App.Settings.Prop.OverlayFocusModeEnabled = enabled;
            App.Settings.SaveFields(nameof(App.Settings.Prop.OverlayFocusModeEnabled));
            OverlayHub.Refresh();

            NotificationCenter.Notify(
                enabled ? "Overlay Focus Mode on" : "Overlay Focus Mode off",
                enabled ? "HUD and crosshair are hidden until you toggle this again." : "HUD and crosshair are back.",
                NotificationCategory.General,
                kind: NotificationKindId.OverlayFocusMode);
        }

        private SoftKey? _softKey;

        private bool _rejoiningAfterDrop;

        private Task? _rejoinAfterDropTask;

        private async Task RejoinAfterDropAsync(ActivityWatcher.DroppedConnection dropped)
        {
            const string LOG_IDENT = "Watcher::RejoinAfterDrop";

            if (!App.Settings.Prop.AutoRejoinOnDisconnect || _rejoiningAfterDrop)
                return;

            _rejoiningAfterDrop = true;

            string why = dropped.Reason == 278 ? "You were kicked for being idle" : "The connection dropped";
            string uri = dropped.Game.GetInviteDeeplink(false);
            int delaySeconds = Math.Max(1, App.Settings.Prop.AutoRejoinDelaySeconds);

            App.Logger.WriteLine(LOG_IDENT, $"Disconnect reason {dropped.Reason}, rejoining {dropped.Game.PlaceId}/{dropped.Game.JobId} in {delaySeconds}s");

            NotificationCenter.Notify(why, "Joining the same server again...", NotificationCategory.General, kind: NotificationKindId.AutoRejoin);

            await Task.Delay(TimeSpan.FromSeconds(delaySeconds));

            try
            {
                Utility.FlagProfileSession.MarkIntentionalRestart();
                KillRobloxProcess();

                for (int i = 0; i < 40 && Utility.PartyLauncher.RobloxRunning(); i++)
                    await Task.Delay(250);

                Utility.RobloxLaunch.Launch(uri);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not rejoin: {ex.Message}");
            }
        }

        public void KillRobloxProcess()
        {
            _killedOnPurpose = true;
            CloseProcess(_watcherData!.ProcessId, true);
        }

        public void CloseProcess(int pid, bool force = false)
        {
            const string LOG_IDENT = "Watcher::CloseProcess";

            try
            {
                using var process = Process.GetProcessById(pid);

                App.Logger.WriteLine(LOG_IDENT, $"Killing process '{process.ProcessName}' (pid={pid}, force={force})");

                if (process.HasExited)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"PID {pid} has already exited");
                    return;
                }

                if (force)
                    process.Kill();
                else
                    process.CloseMainWindow();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"PID {pid} could not be closed");
                App.Logger.WriteException(LOG_IDENT, ex);
            }
        }

        private async Task WaitForRobloxExitAsync()
        {
            int pid = _watcherData!.ProcessId;

            try
            {
                using Process roblox = Process.GetProcessById(pid);

                if (string.Equals(roblox.ProcessName, App.RobloxPlayerAppName, StringComparison.OrdinalIgnoreCase))
                {
                    App.Logger.WriteLine("Watcher::Run", $"Holding a handle on Roblox ({pid}), waiting for it to exit");
                    await roblox.WaitForExitAsync();
                    return;
                }

                App.Logger.WriteLine("Watcher::Run", $"Process {pid} is '{roblox.ProcessName}', not Roblox, so there is nothing to watch");
                return;
            }
            catch (ArgumentException)
            {
                App.Logger.WriteLine("Watcher::Run", $"Roblox ({pid}) had already exited");
                return;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("Watcher::Run", $"Could not hold a handle on Roblox ({pid}), falling back to polling: {ex.Message}");
            }

            while (Utilities.GetProcessesSafe().Any(x => x.Id == pid && string.Equals(x.ProcessName, App.RobloxPlayerAppName, StringComparison.OrdinalIgnoreCase)))
                await Task.Delay(1000);
        }

        public async Task Run()
        {
            if (!_lock.IsAcquired || _watcherData is null)
                return;

            ActivityWatcher?.Start();

            await WaitForRobloxExitAsync();

            LauncherThrottle.Stop();

            ReleaseWatcherLock();

            if (_rejoinAfterDropTask is not null)
            {
                App.Logger.WriteLine("Watcher::Run", "Waiting for the rejoin after the dropped connection to launch Roblox");

                try
                {
                    await _rejoinAfterDropTask;
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine("Watcher::Run", $"Rejoin after the dropped connection failed: {ex.Message}");
                }
            }

            bool possibleCrash = ActivityWatcher is not null && ActivityWatcher.InGame;

            bool intentional = Utility.FlagProfileSession.RestartedRecently || _killedOnPurpose;
            if (possibleCrash && intentional)
                possibleCrash = false;

            bool crashToast = false;

            if (App.Settings.Prop.CrashAnalyzerEnabled && !intentional)
            {
                CrashReport? crash = await AnalyzeExitAsync();

                if (crash is not null)
                {
                    if (crash.CleanExit && crash.Confidence.Length == 0)
                    {
                        possibleCrash = false;
                    }
                    else
                    {
                        CrashReports.Save(crash);

                        if (possibleCrash || crash.Confidence != "Unclear")
                        {
                            NotificationCenter.Notify("Roblox closed unexpectedly", crash.Cause, NotificationCategory.General, 12,
                                onClick: () => { try { Process.Start(Paths.Process, "-settings"); } catch { } },
                                kind: NotificationKindId.RobloxClosed);
                            crashToast = true;
                        }
                    }
                }
            }

            if (possibleCrash && App.Settings.Prop.AutoRejoinOnCrash)
            {
                await TryAutoRejoinAsync();

                await Task.Delay(TimeSpan.FromSeconds(6));
            }
            else if (crashToast)
            {
                await Task.Delay(TimeSpan.FromSeconds(12));
            }

            if (_watcherData.AutoclosePids is not null)
            {
                foreach (int pid in _watcherData.AutoclosePids)
                    CloseProcess(pid);
            }

            if (App.LaunchSettings.TestModeFlag.Active)
                Process.Start(Paths.Process, "-settings -testmode");
        }

        private bool _killedOnPurpose;

        private void ReleaseWatcherLock()
        {
            try
            {
                App.Current.Dispatcher.Invoke(_lock.Dispose);
                App.Logger.WriteLine("Watcher::Run", "Roblox has closed, released the watcher lock so a new launch can start its own watcher");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("Watcher::Run", $"Could not release the watcher lock early: {ex.Message}");
            }
        }

        private async Task<CrashReport?> AnalyzeExitAsync()
        {
            const string LOG_IDENT = "Watcher::AnalyzeExit";

            try
            {
                string? log = ActivityWatcher?.LogLocation ?? _watcherData?.LogFile;
                if (string.IsNullOrEmpty(log) || !File.Exists(log))
                    return null;

                await Task.Delay(2500);

                CrashReport report = await Task.Run(() => CrashReports.Analyze(log));
                App.Logger.WriteLine(LOG_IDENT, $"clean exit: {report.CleanExit}, confidence: '{report.Confidence}', cause: {report.Cause}");
                return report;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Analysis failed: {ex.Message}");
                return null;
            }
        }

        private async Task TryAutoRejoinAsync()
        {
            const string LOG_IDENT = "Watcher::TryAutoRejoinAsync";

            long placeId = ActivityWatcher?.Data.PlaceId ?? 0;
            string jobId = ActivityWatcher?.Data.JobId ?? "";

            if (placeId == 0)
            {
                App.Logger.WriteLine(LOG_IDENT, "Possible crash detected but no place ID was captured, cannot rejoin");
                return;
            }

            int maxAttempts = Math.Max(1, App.Settings.Prop.AutoRejoinMaxAttempts);
            int delaySeconds = Math.Max(1, App.Settings.Prop.AutoRejoinDelaySeconds);

            string uri = Utility.RobloxLaunch.DeepLink(placeId, jobId);

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                NotificationCenter.Notify(
                    "Possible crash detected",
                    $"Attempting to rejoin (try {attempt} of {maxAttempts})...",
                    NotificationCategory.General,
                    kind: NotificationKindId.AutoRejoin);

                await Task.Delay(TimeSpan.FromSeconds(delaySeconds));

                try
                {
                    Process.Start(Paths.Process, $"-player \"{uri}\"");
                    App.Logger.WriteLine(LOG_IDENT, $"Rejoin attempt {attempt}/{maxAttempts} launched for place {placeId}, job {jobId}");
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Rejoin attempt {attempt}/{maxAttempts} failed to launch: {ex.Message}");
                    continue;
                }

                await Task.Delay(TimeSpan.FromSeconds(3));

                if (Utilities.GetProcessesSafe().Any(x => x.ProcessName.Equals(App.RobloxPlayerAppName, StringComparison.OrdinalIgnoreCase)))
                {
                    NotificationCenter.Notify("Rejoin successful", "Roblox relaunched.", NotificationCategory.General, kind: NotificationKindId.AutoRejoin);
                    return;
                }
            }

            NotificationCenter.Notify("Rejoin failed", $"Could not relaunch Roblox after {maxAttempts} attempt(s).", NotificationCategory.General, kind: NotificationKindId.AutoRejoin);
        }

        public void Dispose()
        {
            App.Logger.WriteLine("Watcher::Dispose", "Disposing Watcher");

            Step("now playing", NowPlaying.Forget);
            Step("overlays", OverlayHub.Shutdown);
            Step("ping monitor", ServerPingMonitor.Stop);
            Step("tray icon", () => _notifyIcon?.Dispose());
            Step("discord", () => RichPresence?.Dispose());
            Step("game chat", () => GameChat?.Dispose());
            Step("soft key", () => _softKey?.Dispose());
            Step("integrations", () => IntegrationWatcher?.Dispose());
            Step("playtime", () => PlayTimeWatcher?.Dispose());
            Step("sessions", () => SessionTracker?.Dispose());
            Step("performance", () => _performanceMeasurer?.Dispose());
            Step("playtime store", PlayTimeStore.Shutdown);
            Step("fullscreen", FakeExclusiveFullscreen.Shutdown);
            Step("audio ducking", AudioDucker.Shutdown);
            Step("headset audio", HeadsetAudio.Shutdown);
            Step("resolution", ForcedResolution.Shutdown);
            Step("window customizer", RobloxWindowCustomizer.Shutdown);
            Step("performance boost", SystemPerformanceBoost.EndSession);
            Step("process optimizer", StopProcessOptimizer);
            Step("launcher throttle", LauncherThrottle.Stop);
            Step("memory manager", MemoryManager.Shutdown);
            Step("hotkeys", () => _hotkeys?.Dispose());
            Step("instant replay", _instantReplay.Dispose);
            Step("activity watcher", () => ActivityWatcher?.Dispose());
            Step("watcher lock", _lock.Dispose);

            GC.SuppressFinalize(this);
        }

        private static void Step(string name, Action action)
        {
            var clock = Stopwatch.StartNew();

            try
            {
                action();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("Watcher::Dispose", $"{name} threw: {ex.Message}");
            }

            if (clock.ElapsedMilliseconds > 750)
                App.Logger.WriteLine("Watcher::Dispose", $"{name} took {clock.ElapsedMilliseconds} ms");
        }
    }
}
