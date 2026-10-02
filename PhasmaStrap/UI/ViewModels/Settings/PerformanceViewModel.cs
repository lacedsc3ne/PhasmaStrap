using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

using CommunityToolkit.Mvvm.Input;

using PhasmaStrap.Integrations.FrameGeneration;
using PhasmaStrap.Models.Persistable;
using PhasmaStrap.Utility;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public class PerformanceViewModel : NotifyPropertyChangedViewModel
    {
        public int CpuCoreLimit
        {
            get => App.Settings.Prop.CpuCoreLimit;
            set
            {
                App.Settings.Prop.CpuCoreLimit = value;
                CpuCoreLimiter.SetCpuCoreLimit(value == 0 ? Environment.ProcessorCount : value);
                OnPropertyChanged(nameof(CpuCoreLimitDisplay));
            }
        }

        public int MaxCpuCores { get; } = Environment.ProcessorCount;

        public string CpuCoreLimitDisplay => CpuCoreLimit == 0 ? "All cores" : $"{CpuCoreLimit} core(s)";

        public bool FakeExclusiveFullscreen
        {
            get => App.Settings.Prop.FakeExclusiveFullscreen;
            set
            {
                App.Settings.Prop.FakeExclusiveFullscreen = value;
                if (!value)
                    PhasmaStrap.Integrations.FakeExclusiveFullscreen.Restore();
            }
        }

        public bool DuckRobloxAudioOnUnfocus
        {
            get => App.Settings.Prop.DuckRobloxAudioOnUnfocus;
            set
            {
                App.Settings.Prop.DuckRobloxAudioOnUnfocus = value;
                if (!value)
                    PhasmaStrap.Integrations.AudioDucker.Shutdown();
            }
        }

        public bool HeadsetAudioEnabled
        {
            get => App.Settings.Prop.HeadsetAudioEnabled;
            set
            {
                App.Settings.Prop.HeadsetAudioEnabled = value;
                if (!value)
                    PhasmaStrap.Integrations.HeadsetAudio.Shutdown();
            }
        }

        public bool AntiAliasingEnabled
        {
            get => App.Settings.Prop.AntiAliasingEnabled;
            set
            {
                Integrations.AntiAliasing.AntiAliasingManager.SetEnabled(value);
                OnPropertyChanged(nameof(AntiAliasingEnabled));
            }
        }

        public string[] AntiAliasingMethodNames => Integrations.AntiAliasing.AntiAliasingSettings.MethodNames;

        public int AntiAliasingMethodIndex
        {
            get => Integrations.AntiAliasing.AntiAliasingSettings.MethodIndex;
            set
            {
                if (value < 0)
                    return;

                Integrations.AntiAliasing.AntiAliasingManager.SetMethod(value);
                OnPropertyChanged(nameof(AntiAliasingMethodIndex));
            }
        }

        public bool FrameGenEnabled
        {
            get => FrameGenSettings.ModeIndex > 0;
            set
            {
                bool confirmed = !value;

                if (value)
                {
                    MessageBoxResult result = Frontend.ShowMessageBox(
                        "Frame generation does not improve performance or actual FPS. It is intended only to make motion appear smoother on low end PCs. It will not help mid range or high end systems, so never enable it on those systems.\n\nEnable frame generation anyway?",
                        MessageBoxImage.Warning,
                        MessageBoxButton.YesNo,
                        MessageBoxResult.No);
                    confirmed = result == MessageBoxResult.Yes;
                }

                if (!FrameGenManager.SetMode(value ? 1 : 0, confirmed))
                {
                    OnPropertyChanged(nameof(FrameGenEnabled));
                    return;
                }

                OnPropertyChanged(nameof(FrameGenEnabled));
            }
        }

        public bool FrameLimiterAvailable => Integrations.Nvidia.FrameLimiter.Available;

        private int _driverFrameLimit = -1;

        public int DriverFrameLimit
        {
            get
            {
                if (_driverFrameLimit < 0)
                    _driverFrameLimit = Integrations.Nvidia.FrameLimiter.Current();

                return _driverFrameLimit;
            }
            set
            {
                _driverFrameLimit = Math.Clamp(value, 0, 1000);
                OnPropertyChanged(nameof(DriverFrameLimit));
            }
        }

        public string FrameLimitStatus
        {
            get
            {
                if (!Integrations.Nvidia.FrameLimiter.Available)
                    return Integrations.Nvidia.FrameLimiter.UnavailableReason;

                return $"The driver is set to {Integrations.Nvidia.FrameLimiter.Describe(Integrations.Nvidia.FrameLimiter.Current())}. Roblox reads it when it starts, so a change applies on the next launch.";
            }
        }

        public ICommand ApplyFrameLimitCommand => new RelayCommand(() =>
        {
            Integrations.Nvidia.FrameLimiter.Set(DriverFrameLimit);

            _driverFrameLimit = Integrations.Nvidia.FrameLimiter.Current();
            OnPropertyChanged(nameof(DriverFrameLimit));
            OnPropertyChanged(nameof(FrameLimitStatus));
        });

        public ICommand RefreshFrameLimitCommand => new RelayCommand(() =>
        {
            _driverFrameLimit = Integrations.Nvidia.FrameLimiter.Current();
            OnPropertyChanged(nameof(DriverFrameLimit));
            OnPropertyChanged(nameof(FrameLimitStatus));
            OnPropertyChanged(nameof(FrameLimiterAvailable));
        });

        public int FrameGenQuality
        {
            get => FrameGenSettings.QualityIndex;
            set
            {
                FrameGenManager.SetQuality(value);
                OnPropertyChanged(nameof(FrameGenQuality));
                OnPropertyChanged(nameof(FrameGenQualityDisplay));
            }
        }

        public string FrameGenQualityDisplay => FrameGenQuality switch
        {
            0 => "Fast",
            2 => "Quality",
            _ => "Balanced",
        };

        public bool ForceHighPerformanceGpu
        {
            get => App.Settings.Prop.ForceHighPerformanceGpu;
            set
            {
                App.Settings.Prop.ForceHighPerformanceGpu = value;
                Integrations.SystemPerformanceBoost.ApplyGpuPreference();
            }
        }

        public bool DisableGameDVR
        {
            get => App.Settings.Prop.DisableGameDVR;
            set
            {
                App.Settings.Prop.DisableGameDVR = value;
                Integrations.SystemPerformanceBoost.ApplyGameDvr();
            }
        }

        public bool BoostTimerResolution
        {
            get => App.Settings.Prop.BoostTimerResolution;
            set => App.Settings.Prop.BoostTimerResolution = value;
        }

        public bool UseHighPerformancePowerPlan
        {
            get => App.Settings.Prop.UseHighPerformancePowerPlan;
            set => App.Settings.Prop.UseHighPerformancePowerPlan = value;
        }

        private bool _cleaningRam;

        public bool CleaningRam
        {
            get => _cleaningRam;
            private set { _cleaningRam = value; OnPropertyChanged(nameof(CleaningRam)); }
        }

        private DateTime _lastCleanAt;
        private double _lastFreedMb;
        private bool _lastPurged;
        private int _lastTrimmed;

        /// <summary>"Freed 1.4 GB · 3 minutes ago" after a clean this session, empty before the first one.</summary>
        public string CleanRamStatus
        {
            get
            {
                if (CleaningRam)
                    return "Cleaning...";
                if (_lastCleanAt == default)
                    return "";

                string freed = _lastFreedMb >= 1024 ? $"{_lastFreedMb / 1024:0.0} GB" : $"{_lastFreedMb:0} MB";
                string text = $"Freed {freed} · {Ago(_lastCleanAt)}";
                if (!_lastPurged)
                    text += ". Standby memory is only purged with administrator rights.";
                return text;
            }
        }

        /// <summary>Longer detail for the status line's tooltip.</summary>
        public string CleanRamDetail => _lastCleanAt == default ? "" : $"Trimmed {_lastTrimmed} processes{(_lastPurged ? " and purged the standby list" : "")}.";

        private static string Ago(DateTime when)
        {
            TimeSpan elapsed = DateTime.Now - when;
            if (elapsed.TotalMinutes < 1)
                return "just now";
            if (elapsed.TotalHours < 1)
                return (int)elapsed.TotalMinutes == 1 ? "1 minute ago" : $"{(int)elapsed.TotalMinutes} minutes ago";
            if (elapsed.TotalDays < 1)
                return (int)elapsed.TotalHours == 1 ? "1 hour ago" : $"{(int)elapsed.TotalHours} hours ago";
            return when.ToString("d MMM, HH:mm", CultureInfo.CurrentCulture);
        }

        /// <summary>Re-reads the "minutes ago" part; called when the Boost page is shown again.</summary>
        public void RefreshCleanRamStatus() => OnPropertyChanged(nameof(CleanRamStatus));

        public ICommand CleanRamCommand => new AsyncRelayCommand(async () =>
        {
            if (CleaningRam)
                return;

            CleaningRam = true;
            OnPropertyChanged(nameof(CleanRamStatus));

            PhasmaStrap.Utility.SystemMemoryCleaner.TrimResult trim = await Task.Run(PhasmaStrap.Utility.SystemMemoryCleaner.TrimAllProcessWorkingSets);
            bool purged = await Task.Run(PhasmaStrap.Utility.SystemMemoryCleaner.PurgeStandbyListElevated);

            _lastFreedMb = trim.BytesFreed / 1048576.0;
            _lastTrimmed = trim.ProcessesTrimmed;
            _lastPurged = purged;
            _lastCleanAt = DateTime.Now;

            CleaningRam = false;
            OnPropertyChanged(nameof(CleanRamStatus));
            OnPropertyChanged(nameof(CleanRamDetail));
        });

        public bool AutoCleanRam
        {
            get => App.Settings.Prop.AutoCleanRam;
            set
            {
                App.Settings.Prop.AutoCleanRam = value;

                if (value)
                    PhasmaStrap.Utility.AutoRamCleaner.Start();
                else
                    PhasmaStrap.Utility.AutoRamCleaner.Stop();
            }
        }

        public string[] EnginePresetNames => Integrations.EnginePresets.PresetNames;

        public ObservableCollection<string> EngineExcludedPlaces { get; } = new(App.Settings.Prop.EngineExcludedPlaces);

        private string _engineExcludePlaceId = "";

        public string EngineExcludePlaceId
        {
            get => _engineExcludePlaceId;
            set { _engineExcludePlaceId = value; OnPropertyChanged(nameof(EngineExcludePlaceId)); }
        }

        public ICommand AddEngineExcludedPlaceCommand => new RelayCommand(() =>
        {
            string id = EngineExcludePlaceId.Trim();

            if (!long.TryParse(id, out _) || EngineExcludedPlaces.Contains(id))
                return;

            EngineExcludedPlaces.Add(id);
            App.Settings.Prop.EngineExcludedPlaces.Add(id);
            EngineExcludePlaceId = "";
        });

        public ICommand RemoveEngineExcludedPlaceCommand => new RelayCommand<string>(id =>
        {
            if (id is null)
                return;

            EngineExcludedPlaces.Remove(id);
            App.Settings.Prop.EngineExcludedPlaces.Remove(id);
        });

        public sealed record EnginePlaceAssignment(string PlaceId, string PresetName)
        {
            public string Display => $"{GameNameFor(PlaceId)} · {PresetName}";
        }

        public ObservableCollection<EnginePlaceAssignment> EngineProfileAssignments { get; } = new(
            App.Settings.Prop.EnginePlaceProfiles.Select(kv => new EnginePlaceAssignment(kv.Key, kv.Value)));

        private string _engineAssignPlaceId = "";

        public string EngineAssignPlaceId
        {
            get => _engineAssignPlaceId;
            set { _engineAssignPlaceId = value; OnPropertyChanged(nameof(EngineAssignPlaceId)); }
        }

        private string _engineAssignPresetName = Integrations.EnginePresets.PresetNames.FirstOrDefault() ?? "";

        public string EngineAssignPresetName
        {
            get => _engineAssignPresetName;
            set { _engineAssignPresetName = value; OnPropertyChanged(nameof(EngineAssignPresetName)); }
        }

        public ICommand AddEngineProfileAssignmentCommand => new RelayCommand(() =>
        {
            string id = EngineAssignPlaceId.Trim();

            if (!long.TryParse(id, out _) || string.IsNullOrEmpty(EngineAssignPresetName))
                return;

            var existing = EngineProfileAssignments.FirstOrDefault(a => a.PlaceId == id);
            if (existing is not null)
                EngineProfileAssignments.Remove(existing);

            EngineProfileAssignments.Add(new EnginePlaceAssignment(id, EngineAssignPresetName));
            App.Settings.Prop.EnginePlaceProfiles[id] = EngineAssignPresetName;
            EngineAssignPlaceId = "";
        });

        public ICommand RemoveEngineProfileAssignmentCommand => new RelayCommand<EnginePlaceAssignment>(assignment =>
        {
            if (assignment is null)
                return;

            EngineProfileAssignments.Remove(assignment);
            App.Settings.Prop.EnginePlaceProfiles.Remove(assignment.PlaceId);
        });

        private List<DisplayInfo>? _monitorOptions;
        public List<DisplayInfo> MonitorOptions => _monitorOptions ??= DisplaySystem.GetDisplays();

        public DisplayInfo? SelectedMonitor
        {
            get => MonitorOptions.FirstOrDefault(m => m.DeviceName == App.Settings.Prop.InGameResolutionMonitor)
                ?? MonitorOptions.FirstOrDefault(m => m.IsPrimary)
                ?? MonitorOptions.FirstOrDefault();
            set
            {
                if (value is null || value.DeviceName == App.Settings.Prop.InGameResolutionMonitor)
                    return;
                App.Settings.Prop.InGameResolutionMonitor = value.DeviceName;
                _modeOptions = null;
                OnPropertyChanged(nameof(SelectedMonitor));
                OnPropertyChanged(nameof(ModeOptions));
                OnPropertyChanged(nameof(SelectedMode));
            }
        }

        private List<DisplayMode>? _modeOptions;
        public List<DisplayMode> ModeOptions => _modeOptions ??= DisplaySystem.GetModes(SelectedMonitor?.DeviceName)
            .OrderByDescending(m => m.Width * m.Height).ThenByDescending(m => m.RefreshRate).ToList();

        public DisplayMode? SelectedMode
        {
            get => ModeOptions.FirstOrDefault(m =>
                m.Width == App.Settings.Prop.InGameResolutionWidth &&
                m.Height == App.Settings.Prop.InGameResolutionHeight &&
                m.RefreshRate == App.Settings.Prop.InGameResolutionRefreshRate);
            set
            {
                if (value == null)
                    return;

                App.Settings.Prop.InGameResolutionWidth = value.Width;
                App.Settings.Prop.InGameResolutionHeight = value.Height;
                App.Settings.Prop.InGameResolutionRefreshRate = value.RefreshRate;
                OnPropertyChanged(nameof(SelectedMode));
            }
        }

        public bool ForceInGameResolution
        {
            get => App.Settings.Prop.ForceInGameResolution;
            set
            {
                if (App.Settings.Prop.ForceInGameResolution == value)
                    return;
                App.Settings.Prop.ForceInGameResolution = value;
                OnPropertyChanged(nameof(ForceInGameResolution));
            }
        }

        /// <summary>A game row with its icon, or its initials on a coloured tile until (or unless) the icon is known.</summary>
        public class GameIconRow : NotifyPropertyChangedViewModel
        {
            private static readonly System.Windows.Media.Color[] TileColors =
            {
                System.Windows.Media.Color.FromRgb(0xD9, 0x4F, 0x3D), System.Windows.Media.Color.FromRgb(0x8E, 0x44, 0xC9),
                System.Windows.Media.Color.FromRgb(0x2F, 0x80, 0xED), System.Windows.Media.Color.FromRgb(0x1F, 0x9D, 0x74),
                System.Windows.Media.Color.FromRgb(0xE0, 0x8A, 0x1E), System.Windows.Media.Color.FromRgb(0xC2, 0x3B, 0x80),
                System.Windows.Media.Color.FromRgb(0x4C, 0x5B, 0xD4), System.Windows.Media.Color.FromRgb(0x5E, 0x7D, 0x2A),
            };

            public GameIconRow(string placeId, string title)
            {
                PlaceId = placeId;
                Title = title;
                Initials = MakeInitials(title);

                int hash = 0;
                foreach (char c in placeId)
                    hash = unchecked(hash * 31 + c);
                var brush = new System.Windows.Media.SolidColorBrush(TileColors[(hash & 0x7FFFFFFF) % TileColors.Length]);
                brush.Freeze();
                InitialsBrush = brush;
            }

            public string PlaceId { get; }

            /// <summary>Game name without the place ID.</summary>
            public string Title { get; }

            public string Initials { get; }

            public System.Windows.Media.Brush InitialsBrush { get; }

            private string _iconUrl = "";
            public string IconUrl
            {
                get => _iconUrl;
                set { _iconUrl = value ?? ""; OnPropertyChanged(nameof(IconUrl)); }
            }

            private static string MakeInitials(string title)
            {
                string[] words = title.Split(new[] { ' ', '-', '_', ':', '[', ']', '(', ')', '|' }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(w => char.IsLetterOrDigit(w[0])).ToArray();
                if (words.Length == 0)
                    return "?";
                if (words.Length == 1)
                    return new string(words[0].Where(char.IsLetterOrDigit).Take(2).ToArray()).ToUpperInvariant();
                return (words[0][..1] + words[1][..1]).ToUpperInvariant();
            }
        }

        public sealed class ResolutionGameChoice : GameIconRow
        {
            public ResolutionGameChoice(string placeId, string name, string title = "") : base(placeId, title.Length > 0 ? title : name)
            {
                Name = name;
            }

            public string Name { get; }

            public override string ToString() => Name;
        }

        public List<ResolutionGameChoice> ResolutionRecentGames { get; } = Integrations.PlayTimeStore.GetAll()
            .Where(e => e.PlaceId > 0)
            .Take(30)
            .Select(e =>
            {
                string title = e.Name.Length > 0 ? e.DisplayName : "Place";
                return new ResolutionGameChoice(e.PlaceId.ToString(), $"{title}  ({e.PlaceId})", title) { IconUrl = e.IconUrl };
            })
            .ToList();

        public ResolutionGameChoice? ResolutionPickedGame
        {
            get => null;
            set
            {
                if (value is null)
                    return;
                ResolutionAssignPlaceId = value.PlaceId;
            }
        }

        public sealed class ResolutionPlaceAssignment : GameIconRow
        {
            public ResolutionPlaceAssignment(string placeId, string game, InGameResolutionProfile profile) : base(placeId, game)
            {
                Game = game;
                Profile = profile;
                IconUrl = IconFor(placeId);
            }

            public string Game { get; }

            public InGameResolutionProfile Profile { get; }

            public string Display => $"{Game} · {Profile.Width} × {Profile.Height} at {Profile.RefreshRate} Hz";
        }

        private static string IconFor(string placeId) =>
            Integrations.PlayTimeStore.GetAll().FirstOrDefault(e => e.PlaceId.ToString() == placeId)?.IconUrl ?? "";

        private bool _resolutionIconsRequested;

        /// <summary>
        /// Fills in game icons for the per game rows and the recent games list. Uses the icon saved in play time
        /// history first, then the Roblox thumbnails API (the same lookup the Games page uses).
        /// </summary>
        public async void LoadResolutionIcons()
        {
            if (_resolutionIconsRequested)
                return;
            _resolutionIconsRequested = true;

            try
            {
                var rows = ResolutionRecentGames.Cast<GameIconRow>().Concat(ResolutionPlaceAssignments).Where(r => r.IconUrl.Length == 0).ToList();
                if (rows.Count == 0)
                    return;

                var universes = new Dictionary<string, long>();
                foreach (string placeId in rows.Select(r => r.PlaceId).Distinct().Take(60))
                {
                    if (!long.TryParse(placeId, out long place) || place <= 0)
                        continue;
                    try
                    {
                        long? universe = await GameLookup.UniverseOfAsync(place, TimeSpan.FromSeconds(6));
                        if (universe is > 0)
                            universes[placeId] = universe.Value;
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteLine("PerformanceViewModel", $"Universe lookup for {placeId} failed: {ex.Message}");
                    }
                }

                if (universes.Count == 0)
                    return;

                List<GameInfo> games = await GameLookup.WithIconsAsync(universes.Select(kv => new GameInfo(kv.Value, long.Parse(kv.Key), "")).ToList());
                foreach (GameInfo game in games.Where(g => !string.IsNullOrEmpty(g.IconUrl)))
                {
                    foreach (GameIconRow row in rows.Where(r => universes.TryGetValue(r.PlaceId, out long u) && u == game.UniverseId))
                        row.IconUrl = game.IconUrl;
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("PerformanceViewModel", $"Game icon lookup failed: {ex.Message}");
            }
        }

        private static string GameNameFor(string placeId) =>
            Integrations.PlayTimeStore.GetAll().FirstOrDefault(e => e.PlaceId.ToString() == placeId) is { Name.Length: > 0 } entry ? entry.DisplayName : $"Place {placeId}";

        public ObservableCollection<ResolutionPlaceAssignment> ResolutionPlaceAssignments { get; } = new(
            App.Settings.Prop.InGameResolutionPlaceProfiles.Select(kv => new ResolutionPlaceAssignment(kv.Key, GameNameFor(kv.Key), kv.Value)));

        private string _resolutionAssignPlaceId = "";
        public string ResolutionAssignPlaceId
        {
            get => _resolutionAssignPlaceId;
            set { _resolutionAssignPlaceId = value; OnPropertyChanged(nameof(ResolutionAssignPlaceId)); }
        }

        private string _resolutionAssignStatus = "";
        public string ResolutionAssignStatus
        {
            get => _resolutionAssignStatus;
            private set { _resolutionAssignStatus = value; OnPropertyChanged(nameof(ResolutionAssignStatus)); }
        }

        public ICommand AddResolutionPlaceCommand => new RelayCommand(() =>
        {
            string id = ResolutionAssignPlaceId.Trim();
            if (!long.TryParse(id, out long place) || place <= 0)
            {
                ResolutionAssignStatus = "Pick a game from the list, or type its place ID (the number in its roblox.com/games/ link).";
                return;
            }

            DisplayMode? mode = SelectedMode;
            if (mode is null)
            {
                ResolutionAssignStatus = "Pick a resolution above first. The game gets that monitor and resolution.";
                return;
            }

            var profile = new InGameResolutionProfile
            {
                Monitor = SelectedMonitor?.DeviceName ?? "",
                Width = mode.Width,
                Height = mode.Height,
                RefreshRate = mode.RefreshRate,
            };

            var existing = ResolutionPlaceAssignments.FirstOrDefault(a => a.PlaceId == id);
            if (existing is not null)
                ResolutionPlaceAssignments.Remove(existing);

            var added = new ResolutionPlaceAssignment(id, GameNameFor(id), profile);
            if (added.IconUrl.Length == 0)
                added.IconUrl = ResolutionRecentGames.FirstOrDefault(g => g.PlaceId == id)?.IconUrl ?? "";
            ResolutionPlaceAssignments.Add(added);
            App.Settings.Prop.InGameResolutionPlaceProfiles[id] = profile;
            ResolutionAssignPlaceId = "";
            ResolutionAssignStatus = $"{GameNameFor(id)} will use {mode}. Press Save to keep it.";
        });

        public ICommand RemoveResolutionPlaceCommand => new RelayCommand<ResolutionPlaceAssignment>(assignment =>
        {
            if (assignment is null)
                return;
            ResolutionPlaceAssignments.Remove(assignment);
            App.Settings.Prop.InGameResolutionPlaceProfiles.Remove(assignment.PlaceId);
        });

        public ICommand IdentifyDisplaysCommand => new RelayCommand(() => DisplaySystem.IdentifyDisplays());

        #region Last session tiles

        /// <summary>One stat tile above the frame rate settings: a number, a caption and a row of small bars.</summary>
        public sealed class SessionStatTile
        {
            public string Label { get; init; } = "";
            public string Value { get; init; } = "";
            public string Unit { get; init; } = "";
            public string Caption { get; init; } = "";
            public List<double> Bars { get; init; } = new();
        }

        public ObservableCollection<SessionStatTile> SessionTiles { get; } = new();

        public Visibility SessionTilesVisibility => SessionTiles.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Reads the summary the watcher writes when you leave a game (PerformanceSessionRecorder, stored locally).
        /// Tiles without data are left out, and the row hides when there is no session yet.
        /// </summary>
        public void LoadLastSession()
        {
            SessionTiles.Clear();

            PerformanceSessionSummary? last = PerformanceSessionRecorder.LoadLast();
            if (last is not null)
            {
                string game = last.GameName.Length > 0 ? last.GameName : last.PlaceId > 0 ? $"Place {last.PlaceId}" : "";

                List<int> fps = last.Fps.Where(f => f > 0).ToList();
                if (fps.Count > 0)
                {
                    List<int> sorted = fps.OrderBy(f => f).ToList();
                    int low = sorted[Math.Min(sorted.Count - 1, (int)(sorted.Count * 0.01))];

                    SessionTiles.Add(new SessionStatTile
                    {
                        Label = "Average FPS",
                        Value = Math.Round(fps.Average()).ToString(CultureInfo.CurrentCulture),
                        Caption = game.Length > 0 ? $"Last session, {game}" : "Last session",
                        Bars = Bars(fps, average: true),
                    });
                    SessionTiles.Add(new SessionStatTile
                    {
                        Label = "1% low",
                        Value = low.ToString(CultureInfo.CurrentCulture),
                        Unit = "fps",
                        Caption = "Stutter floor",
                        Bars = Bars(fps, average: false),
                    });
                }

                List<int> ping = last.PingMs.Where(p => p >= 0).ToList();
                if (ping.Count > 0)
                {
                    SessionTiles.Add(new SessionStatTile
                    {
                        Label = "Ping",
                        Value = Math.Round(ping.Average()).ToString(CultureInfo.CurrentCulture),
                        Unit = "ms",
                        Caption = last.Region.Length > 0 ? $"{last.Region} server" : "Average while playing",
                        Bars = Bars(ping, average: true),
                    });
                }

                List<int> memory = last.MemoryMb.Where(m => m > 0).ToList();
                if (memory.Count > 0)
                {
                    int peak = memory.Max();
                    SessionTiles.Add(new SessionStatTile
                    {
                        Label = "Memory",
                        Value = peak >= 1024 ? (peak / 1024.0).ToString("0.0", CultureInfo.CurrentCulture) : peak.ToString(CultureInfo.CurrentCulture),
                        Unit = peak >= 1024 ? "GB" : "MB",
                        Caption = "Peak while playing",
                        Bars = Bars(memory, average: true),
                    });
                }
            }

            OnPropertyChanged(nameof(SessionTilesVisibility));
        }

        /// <summary>Squeezes the samples into 18 bars between 4 and 22 pixels high (bucket average, or bucket minimum for the lows).</summary>
        private static List<double> Bars(List<int> samples, bool average)
        {
            const int count = 18;
            var buckets = new List<double>(count);
            for (int i = 0; i < count; i++)
            {
                int start = i * samples.Count / count;
                int end = Math.Max(start + 1, (i + 1) * samples.Count / count);
                IEnumerable<int> slice = samples.Skip(start).Take(end - start);
                if (!slice.Any())
                    slice = new[] { samples[^1] };
                buckets.Add(average ? slice.Average() : slice.Min());
            }

            double max = Math.Max(1, buckets.Max());
            return buckets.Select(v => 4 + 18 * (v / max)).ToList();
        }

        #endregion
    }
}
