using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using PhasmaStrap.Models.Persistable;

namespace PhasmaStrap.Integrations
{
    internal sealed class RobloxProcessOptimizer : IDisposable
    {
        private const int PollIntervalMs = 2000;

        private const int TrimDelayMs = 15000;

        private static readonly int ProcessorCount = Environment.ProcessorCount;

        private readonly int _processId;

        private readonly EnginePresetValues? _override;

        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        private Task? _loopTask;

        private bool _disposed;

        private DateTime _unfocusedSinceUtc = DateTime.MinValue;

        private bool _trimmedThisTabOut;

        private ProcessPriorityClass? _lastPriority;

        private int? _lastCpuLimit;

        private IntPtr? _originalAffinity;

        private bool? _originalPriorityBoostEnabled;

        private int _started;

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        public RobloxProcessOptimizer(int processId, EnginePresetValues? presetOverride = null)
        {
            _processId = processId;
            _override = presetOverride;
        }

        private EnginePresetValues EffectiveValues => _override ?? EnginePresets.FromSettings(App.Settings.Prop);

        public static bool ShouldRun(Settings? settings)
        {
            if (settings == null)
            {
                return false;
            }
            return settings.OptimizeRoblox
                || settings.RobloxEfficiencyMode
                || settings.ReduceMemoryOutOfFocus
                || !IsAutomaticCpuLimit(settings.SelectedCpuPriority)
                || !IsNormalPriority(settings.RobloxPriorityLimit);
        }

        public static void ApplyLaunchProfile(Process process)
        {
            if (process == null || process.HasExited || !IsRobloxPlayer(process))
            {
                return;
            }
            Settings settings = App.Settings.Prop;
            if (!ShouldRun(settings))
            {
                return;
            }
            TryApplyPriority(process, ResolvePriority(EnginePresets.FromSettings(settings)));
        }

        public void Start()
        {
            if (_disposed || Interlocked.Exchange(ref _started, 1) != 0)
            {
                return;
            }
            _loopTask = Task.Run(() => RunAsync(_cancellationTokenSource.Token));
        }

        private async Task RunAsync(CancellationToken token)
        {
            try
            {
                using Process? process = TryGetProcess();
                if (process == null || process.HasExited || !IsRobloxPlayer(process))
                    return;
                while (!token.IsCancellationRequested && !process.HasExited)
                {
                    ApplySettings(process);
                    await Task.Delay(PollIntervalMs, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("RobloxProcessOptimizer", "Runtime optimizer failed: " + ex.Message);
            }
            finally
            {
                RestoreProcessState();
            }
        }

        private void ApplySettings(Process process)
        {
            EnginePresetValues settings = EffectiveValues;
            bool focused = IsProcessFocused();
            ProcessPriorityClass desiredPriority = !focused && settings.RobloxEfficiencyMode ? ProcessPriorityClass.Idle : (!focused && settings.ReduceMemoryOutOfFocus ? ProcessPriorityClass.BelowNormal : ResolvePriority(settings));
            if (_lastPriority != desiredPriority)
            {
                TryApplyPriority(process, desiredPriority);
                _lastPriority = desiredPriority;
            }
            if (settings.OptimizeRoblox && !_originalPriorityBoostEnabled.HasValue)
            {
                try
                {
                    _originalPriorityBoostEnabled = process.PriorityBoostEnabled;
                }
                catch
                {
                }
            }
            bool enablePriorityBoost = settings.OptimizeRoblox && (focused || !settings.ReduceMemoryOutOfFocus);
            if (enablePriorityBoost)
            {
                try
                {
                    if (!process.PriorityBoostEnabled)
                    {
                        process.PriorityBoostEnabled = true;
                    }
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine("RobloxProcessOptimizer", "Priority boost could not be enabled: " + ex.Message);
                }
            }
            else if (_originalPriorityBoostEnabled.HasValue)
            {
                try
                {
                    if (process.PriorityBoostEnabled != _originalPriorityBoostEnabled.Value)
                    {
                        process.PriorityBoostEnabled = _originalPriorityBoostEnabled.Value;
                    }
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine("RobloxProcessOptimizer", "Priority boost could not be restored: " + ex.Message);
                }
            }
            int? cpuLimit = GetCpuLimit(settings.SelectedCpuPriority);
            if (_lastCpuLimit != cpuLimit)
            {
                if (cpuLimit.HasValue && !_originalAffinity.HasValue)
                {
                    try
                    {
                        _originalAffinity = process.ProcessorAffinity;
                    }
                    catch
                    {
                    }
                }
                TryApplyCpuLimit(process, settings.SelectedCpuPriority, _originalAffinity);
                _lastCpuLimit = cpuLimit;
            }
            if (focused)
            {
                _unfocusedSinceUtc = DateTime.MinValue;
                _trimmedThisTabOut = false;
                return;
            }
            if (_unfocusedSinceUtc == DateTime.MinValue)
            {
                _unfocusedSinceUtc = DateTime.UtcNow;
                return;
            }
            if (settings.ReduceMemoryOutOfFocus && !_trimmedThisTabOut && (DateTime.UtcNow - _unfocusedSinceUtc).TotalMilliseconds >= TrimDelayMs)
            {
                _trimmedThisTabOut = true;
                TrimWorkingSet(process);
            }
        }

        private static void TrimWorkingSet(Process process)
        {
            try
            {
                if (EmptyWorkingSet(process.Handle))
                {
                    process.Refresh();
                    App.Logger.WriteLine("RobloxProcessOptimizer", "Trimmed Roblox working set to " + process.WorkingSet64 / 1048576 + " MB while unfocused");
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("RobloxProcessOptimizer", "Working set trim failed: " + ex.Message);
            }
        }

        private void RestoreProcessState()
        {
            using Process? process = TryGetProcess();
            if (process == null || process.HasExited)
            {
                return;
            }
            if (_originalAffinity.HasValue)
            {
                try
                {
                    process.ProcessorAffinity = _originalAffinity.Value;
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine("RobloxProcessOptimizer", "CPU affinity could not be restored: " + ex.Message);
                }
            }
            if (_originalPriorityBoostEnabled.HasValue)
            {
                try
                {
                    process.PriorityBoostEnabled = _originalPriorityBoostEnabled.Value;
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine("RobloxProcessOptimizer", "Priority boost could not be restored: " + ex.Message);
                }
            }
            TryApplyPriority(process, ShouldRun(App.Settings.Prop) ? ResolvePriority(EnginePresets.FromSettings(App.Settings.Prop)) : ProcessPriorityClass.Normal);
        }

        private Process? TryGetProcess()
        {
            try
            {
                return Process.GetProcessById(_processId);
            }
            catch
            {
                return null;
            }
        }

        private bool IsProcessFocused()
        {
            try
            {
                IntPtr foregroundWindow = GetForegroundWindow();
                if (foregroundWindow == IntPtr.Zero || IsIconic(foregroundWindow))
                {
                    return false;
                }
                GetWindowThreadProcessId(foregroundWindow, out uint processId);
                return processId == (uint)_processId;
            }
            catch
            {
                return false;
            }
        }

        private static void TryApplyPriority(Process process, ProcessPriorityClass priority)
        {
            try
            {
                if (process.PriorityClass != priority)
                {
                    process.PriorityClass = priority;
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("RobloxProcessOptimizer", "Priority change failed: " + ex.Message);
            }
        }

        private static void TryApplyCpuLimit(Process process, string? selection, IntPtr? originalAffinity)
        {
            int? limit = GetCpuLimit(selection);
            if (!limit.HasValue)
            {
                if (originalAffinity.HasValue)
                {
                    try
                    {
                        process.ProcessorAffinity = originalAffinity.Value;
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteLine("RobloxProcessOptimizer", "CPU affinity could not be restored: " + ex.Message);
                    }
                }
                return;
            }
            int processorCount = ProcessorCount;
            int pointerBits = IntPtr.Size * 8;
            if (processorCount > pointerBits)
            {
                App.Logger.WriteLine("RobloxProcessOptimizer", "CPU limit skipped because this system uses Windows processor groups");
                return;
            }
            if (limit.Value >= processorCount)
            {
                if (originalAffinity.HasValue)
                {
                    try
                    {
                        process.ProcessorAffinity = originalAffinity.Value;
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteLine("RobloxProcessOptimizer", "CPU affinity could not be restored: " + ex.Message);
                    }
                }
                return;
            }
            try
            {
                ulong mask = (1UL << limit.Value) - 1UL;
                process.ProcessorAffinity = (IntPtr)unchecked((long)mask);
                App.Logger.WriteLine("RobloxProcessOptimizer", "Roblox CPU limit set to " + limit.Value + " logical processors");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("RobloxProcessOptimizer", "CPU limit change failed: " + ex.Message);
            }
        }

        private static ProcessPriorityClass ResolvePriority(EnginePresetValues settings)
        {
            string priority = settings.RobloxPriorityLimit?.Trim() ?? "Normal";
            if (priority.Equals("Realtime", StringComparison.OrdinalIgnoreCase))
            {
                return ProcessPriorityClass.High;
            }
            if (priority.Equals("High", StringComparison.OrdinalIgnoreCase))
            {
                return ProcessPriorityClass.High;
            }
            if (priority.Equals("Above Normal", StringComparison.OrdinalIgnoreCase) || priority.Equals("AboveNormal", StringComparison.OrdinalIgnoreCase))
            {
                return ProcessPriorityClass.AboveNormal;
            }
            if (priority.Equals("Below Normal", StringComparison.OrdinalIgnoreCase) || priority.Equals("BelowNormal", StringComparison.OrdinalIgnoreCase))
            {
                return ProcessPriorityClass.BelowNormal;
            }
            if (priority.Equals("Low", StringComparison.OrdinalIgnoreCase) || priority.Equals("Idle", StringComparison.OrdinalIgnoreCase))
            {
                return ProcessPriorityClass.Idle;
            }
            return settings.OptimizeRoblox ? ProcessPriorityClass.AboveNormal : ProcessPriorityClass.Normal;
        }

        private static int? GetCpuLimit(string? selection)
        {
            if (IsAutomaticCpuLimit(selection))
            {
                return null;
            }
            string value = selection!.Trim().Split(' ')[0];
            if (!int.TryParse(value, out int limit) || limit < 1)
            {
                return null;
            }
            return Math.Min(limit, ProcessorCount);
        }

        private static bool IsAutomaticCpuLimit(string? selection)
        {
            return string.IsNullOrWhiteSpace(selection) || selection.Equals("Automatic", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsNormalPriority(string? priority)
        {
            return string.IsNullOrWhiteSpace(priority) || priority.Equals("Normal", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRobloxPlayer(Process process)
        {
            try
            {
                return process.ProcessName.Equals("RobloxPlayerBeta", StringComparison.OrdinalIgnoreCase)
                    || process.ProcessName.Equals("Roblox", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _cancellationTokenSource.Cancel();
            try
            {
                _loopTask?.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("RobloxProcessOptimizer", "Shutdown failed: " + ex.Message);
            }
            _loopTask = null;
            _cancellationTokenSource.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
