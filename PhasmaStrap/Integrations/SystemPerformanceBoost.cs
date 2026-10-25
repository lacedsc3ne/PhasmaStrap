using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

using PhasmaStrap.AppData;

namespace PhasmaStrap.Integrations
{
    internal static class SystemPerformanceBoost
    {
        private const string LOG_IDENT = "SystemPerformanceBoost";

        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
        private static extern uint TimeBeginPeriod(uint uMilliseconds);

        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
        private static extern uint TimeEndPeriod(uint uMilliseconds);

        [DllImport("powrprof.dll")]
        private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

        [DllImport("powrprof.dll")]
        private static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr hMem);

        private static readonly Guid HighPerformanceScheme = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

        private const string GpuPreferenceKey = @"Software\Microsoft\DirectX\UserGpuPreference";
        private const string HighPerformanceGpuValue = "GpuPreference=2;";
        private const string GameConfigStoreKey = @"System\GameConfigStore";
        private const string GameDvrKey = @"Software\Microsoft\Windows\CurrentVersion\GameDVR";
        private const string BackupMutexName = "PhasmaStrap-SystemPerformanceBoost";

        private static readonly object _sessionLock = new();

        private static bool _timerResolutionActive;

        private static string BackupPath => Path.Combine(Paths.Base, "SystemBoostBackup.json");

        internal sealed class Backup
        {
            public bool GameDvrSaved { get; set; }
            public int? GameDvrEnabled { get; set; }
            public int? AppCaptureEnabled { get; set; }
            public Dictionary<string, string?> GpuPreferences { get; set; } = new(StringComparer.OrdinalIgnoreCase);
            public Guid? PowerScheme { get; set; }
        }

        private static Backup ReadBackup()
        {
            try
            {
                if (!File.Exists(BackupPath))
                    return new Backup();

                Backup backup = JsonSerializer.Deserialize<Backup>(File.ReadAllText(BackupPath)) ?? new Backup();
                backup.GpuPreferences = new Dictionary<string, string?>(backup.GpuPreferences ?? new(), StringComparer.OrdinalIgnoreCase);
                return backup;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not read {BackupPath}, starting with no saved Windows values: {ex.Message}");
                return new Backup();
            }
        }

        private static void WriteBackup(Backup backup)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BackupPath)!);
            string tempPath = BackupPath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tempPath, BackupPath, true);
        }

        private static void WithBackup(Action<Backup> work)
        {
            using var mutex = new Mutex(false, BackupMutexName);
            bool owned = false;

            try
            {
                try { owned = mutex.WaitOne(3000); }
                catch (AbandonedMutexException) { owned = true; }

                if (!owned)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Another PhasmaStrap window is changing Windows settings, skipping this change");
                    return;
                }

                work(ReadBackup());
            }
            finally
            {
                if (owned)
                {
                    try { mutex.ReleaseMutex(); } catch { }
                }
            }
        }

        public static void ApplyGpuPreference()
        {
            try
            {
                string exePath = new RobloxPlayerData().ExecutablePath;
                bool force = App.Settings.Prop.ForceHighPerformanceGpu;

                WithBackup(backup =>
                {
                    using RegistryKey key = Registry.CurrentUser.CreateSubKey(GpuPreferenceKey);
                    bool changed = false;

                    if (force)
                    {
                        foreach (string oldPath in backup.GpuPreferences.Keys.ToList())
                        {
                            if (oldPath.Equals(exePath, StringComparison.OrdinalIgnoreCase) || File.Exists(oldPath))
                                continue;

                            RestoreGpuValue(key, oldPath, backup.GpuPreferences[oldPath]);
                            backup.GpuPreferences.Remove(oldPath);
                            changed = true;
                            App.Logger.WriteLine(LOG_IDENT, $"Removed the GPU choice for an old Roblox version at {oldPath}");
                        }

                        if (string.IsNullOrEmpty(exePath))
                        {
                            App.Logger.WriteLine(LOG_IDENT, "Roblox is not installed yet, the GPU choice will be set after it is");
                        }
                        else if (!backup.GpuPreferences.ContainsKey(exePath))
                        {
                            string? previous = key.GetValue(exePath) as string;
                            backup.GpuPreferences[exePath] = previous;
                            WriteBackup(backup);
                            key.SetValue(exePath, HighPerformanceGpuValue, RegistryValueKind.String);
                            App.Logger.WriteLine(LOG_IDENT, $"Roblox pinned to the high-performance GPU at {exePath} (was \"{previous ?? "not set"}\")");
                        }
                    }
                    else if (backup.GpuPreferences.Count > 0)
                    {
                        foreach (var item in backup.GpuPreferences)
                            RestoreGpuValue(key, item.Key, item.Value);

                        App.Logger.WriteLine(LOG_IDENT, $"Put back the previous GPU choice for {backup.GpuPreferences.Count} Roblox version(s)");
                        backup.GpuPreferences.Clear();
                        changed = true;
                    }

                    if (changed)
                        WriteBackup(backup);
                });
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
            }
        }

        private static void RestoreGpuValue(RegistryKey key, string exePath, string? previous)
        {
            if (key.GetValue(exePath) is string current && current != HighPerformanceGpuValue)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Left the GPU choice for {exePath} as \"{current}\" because it was changed outside PhasmaStrap");
                return;
            }

            if (previous is null)
                key.DeleteValue(exePath, throwOnMissingValue: false);
            else
                key.SetValue(exePath, previous, RegistryValueKind.String);
        }

        public static void ApplyGameDvr()
        {
            bool disable = App.Settings.Prop.DisableGameDVR;
            try
            {
                WithBackup(backup =>
                {
                    using RegistryKey gameConfigStore = Registry.CurrentUser.CreateSubKey(GameConfigStoreKey);
                    using RegistryKey gameDvr = Registry.CurrentUser.CreateSubKey(GameDvrKey);

                    if (disable && !backup.GameDvrSaved)
                    {
                        backup.GameDvrEnabled = gameConfigStore.GetValue("GameDVR_Enabled") as int?;
                        backup.AppCaptureEnabled = gameDvr.GetValue("AppCaptureEnabled") as int?;
                        backup.GameDvrSaved = true;
                        WriteBackup(backup);

                        gameConfigStore.SetValue("GameDVR_Enabled", 0, RegistryValueKind.DWord);
                        gameDvr.SetValue("AppCaptureEnabled", 0, RegistryValueKind.DWord);

                        App.Logger.WriteLine(LOG_IDENT, $"Game DVR background capture disabled (was GameDVR_Enabled={backup.GameDvrEnabled?.ToString() ?? "not set"}, AppCaptureEnabled={backup.AppCaptureEnabled?.ToString() ?? "not set"})");
                    }
                    else if (!disable && backup.GameDvrSaved)
                    {
                        RestoreDword(gameConfigStore, "GameDVR_Enabled", backup.GameDvrEnabled);
                        RestoreDword(gameDvr, "AppCaptureEnabled", backup.AppCaptureEnabled);

                        App.Logger.WriteLine(LOG_IDENT, $"Game DVR background capture put back (GameDVR_Enabled={backup.GameDvrEnabled?.ToString() ?? "not set"}, AppCaptureEnabled={backup.AppCaptureEnabled?.ToString() ?? "not set"})");

                        backup.GameDvrSaved = false;
                        backup.GameDvrEnabled = null;
                        backup.AppCaptureEnabled = null;
                        WriteBackup(backup);
                    }
                });
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
            }
        }

        private static void RestoreDword(RegistryKey key, string name, int? previous)
        {
            if (previous is null)
                key.DeleteValue(name, throwOnMissingValue: false);
            else
                key.SetValue(name, previous.Value, RegistryValueKind.DWord);
        }

        public static void OnGameJoin()
        {
            lock (_sessionLock)
            {
                if (App.Settings.Prop.BoostTimerResolution && !_timerResolutionActive)
                {
                    try
                    {
                        uint result = TimeBeginPeriod(1);
                        _timerResolutionActive = result == 0;
                        App.Logger.WriteLine(LOG_IDENT, _timerResolutionActive ? "Timer resolution raised to 1 ms for this session" : $"Could not raise the timer resolution, error {result}");
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteException(LOG_IDENT, ex);
                    }
                }

                if (App.Settings.Prop.UseHighPerformancePowerPlan)
                {
                    try
                    {
                        WithBackup(backup =>
                        {
                            if (backup.PowerScheme is null && PowerGetActiveScheme(IntPtr.Zero, out IntPtr activeGuidPtr) == 0 && activeGuidPtr != IntPtr.Zero)
                            {
                                Guid active = Marshal.PtrToStructure<Guid>(activeGuidPtr);
                                LocalFree(activeGuidPtr);

                                if (active != HighPerformanceScheme)
                                {
                                    backup.PowerScheme = active;
                                    WriteBackup(backup);
                                }
                            }

                            Guid target = HighPerformanceScheme;
                            uint result = PowerSetActiveScheme(IntPtr.Zero, ref target);
                            App.Logger.WriteLine(LOG_IDENT, result == 0
                                ? $"Switched to the High performance power plan for this session (was {backup.PowerScheme?.ToString() ?? "already High performance"})"
                                : $"Could not switch to the High performance power plan, error {result}. The plan may be missing on this PC");
                        });
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteException(LOG_IDENT, ex);
                    }
                }
            }
        }

        public static void OnGameLeave() => EndSession();

        public static void EndSession()
        {
            lock (_sessionLock)
            {
                if (_timerResolutionActive)
                {
                    try
                    {
                        TimeEndPeriod(1);
                        App.Logger.WriteLine(LOG_IDENT, "Timer resolution put back");
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteException(LOG_IDENT, ex);
                    }
                    _timerResolutionActive = false;
                }

                try
                {
                    WithBackup(backup =>
                    {
                        if (backup.PowerScheme is null)
                            return;

                        Guid previous = backup.PowerScheme.Value;
                        uint result = PowerSetActiveScheme(IntPtr.Zero, ref previous);
                        App.Logger.WriteLine(LOG_IDENT, result == 0
                            ? $"Power plan put back to {previous}"
                            : $"Could not put the power plan back to {previous}, error {result}");

                        if (result == 0)
                        {
                            backup.PowerScheme = null;
                            WriteBackup(backup);
                        }
                    });
                }
                catch (Exception ex)
                {
                    App.Logger.WriteException(LOG_IDENT, ex);
                }
            }
        }
    }
}
