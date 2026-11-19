using System.Runtime.InteropServices;

namespace PhasmaStrap.Utility
{
    public static class LauncherThrottle
    {
        private const string LOG_IDENT = "LauncherThrottle";

        private const int RecheckMs = 4000;
        private const int TrimAfterMs = 60000;

        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        private const int ProcessPowerThrottling = 4;
        private const int ThreadPowerThrottling = 3;
        private const uint POWER_THROTTLING_CURRENT_VERSION = 1;
        private const uint POWER_THROTTLING_EXECUTION_SPEED = 0x1;

        [StructLayout(LayoutKind.Sequential)]
        private struct POWER_THROTTLING_STATE
        {
            public uint Version;
            public uint ControlMask;
            public uint StateMask;
        }

        private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessInformation(IntPtr process, int infoClass, ref POWER_THROTTLING_STATE info, uint size);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetThreadInformation(IntPtr thread, int infoClass, ref POWER_THROTTLING_STATE info, uint size);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentThread();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref uint size);

        [DllImport("psapi.dll")]
        private static extern bool EmptyWorkingSet(IntPtr process);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventProc proc, uint pid, uint thread, uint flags);

        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hook);

        private static readonly object _sync = new();

        private static System.Threading.Timer? _timer;
        private static IntPtr _hook;
        private static WinEventProc? _hookProc;
        private static System.Windows.Threading.Dispatcher? _hookDispatcher;

        private static int _robloxPid;
        private static Func<bool>? _busy;
        private static bool _running;
        private static bool _trimmed;
        private static long _startedMs;

        private static bool? _appliedBelowNormal;
        private static bool? _appliedEco;

        private static uint _lastForegroundPid;
        private static bool _lastForegroundIsRoblox;

        public static void NormalizeStartupPriority()
        {
            try
            {
                using Process self = Process.GetCurrentProcess();
                ProcessPriorityClass current = self.PriorityClass;
                if (current is ProcessPriorityClass.BelowNormal or ProcessPriorityClass.Idle)
                {
                    self.PriorityClass = ProcessPriorityClass.Normal;
                    App.Logger.WriteLine(LOG_IDENT, $"Started at {current} priority (inherited), put back to Normal");
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not check the startup priority: {ex.Message}");
            }

            SetEco(false);
        }

        public static bool SessionActive
        {
            get
            {
                lock (_sync)
                    return _running && _robloxPid != 0;
            }
        }

        public static void StartForSession(int robloxPid, Func<bool> busy)
        {
            Start(robloxPid, busy);
        }

        public static void StartForegroundOnly()
        {
            Start(0, CompositorDrawingGame);
        }

        public static bool CompositorDrawingGame() =>
            Integrations.Overlays.OverlayHub.CompositorLive && Integrations.Overlays.OverlaySettings.NeedsCapture;

        private static void Start(int robloxPid, Func<bool> busy)
        {
            lock (_sync)
            {
                if (_running)
                    return;
                _running = true;
                _robloxPid = robloxPid;
                _busy = busy;
                _trimmed = robloxPid == 0;
                _startedMs = Environment.TickCount64;
                _timer = new System.Threading.Timer(_ => Evaluate(), null, 1000, RecheckMs);
            }

            InstallForegroundHook();
        }

        public static void Stop()
        {
            System.Threading.Timer? timer;
            lock (_sync)
            {
                if (!_running)
                    return;
                _running = false;
                timer = _timer;
                _timer = null;
            }

            try { timer?.Dispose(); } catch { }
            RemoveForegroundHook();

            lock (_sync)
            {
                ApplyLocked(belowNormal: false, eco: false);
                _appliedBelowNormal = null;
                _appliedEco = null;
            }
        }

        public static void MarkCurrentThreadLatencyCritical(ThreadPriority priority = ThreadPriority.Highest)
        {
            try
            {
                Thread.CurrentThread.Priority = priority;
            }
            catch
            {
            }

            try
            {
                var state = new POWER_THROTTLING_STATE
                {
                    Version = POWER_THROTTLING_CURRENT_VERSION,
                    ControlMask = POWER_THROTTLING_EXECUTION_SPEED,
                    StateMask = 0,
                };
                SetThreadInformation(GetCurrentThread(), ThreadPowerThrottling, ref state, (uint)Marshal.SizeOf<POWER_THROTTLING_STATE>());
            }
            catch
            {
            }
        }

        private static void InstallForegroundHook()
        {
            try
            {
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted)
                    return;

                dispatcher.BeginInvoke(new Action(() =>
                {
                    lock (_sync)
                    {
                        if (!_running || _hook != IntPtr.Zero)
                            return;
                        _hookProc = OnForeground;
                        _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _hookProc, 0, 0, WINEVENT_OUTOFCONTEXT);
                        _hookDispatcher = dispatcher;
                    }

                    if (_robloxPid != 0)
                        MarkCurrentThreadLatencyCritical();
                }));
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Foreground hook not installed, using the timer only: {ex.Message}");
            }
        }

        private static void RemoveForegroundHook()
        {
            IntPtr hook;
            System.Windows.Threading.Dispatcher? dispatcher;
            lock (_sync)
            {
                hook = _hook;
                dispatcher = _hookDispatcher;
                _hook = IntPtr.Zero;
                _hookDispatcher = null;
            }

            if (hook == IntPtr.Zero)
                return;

            try
            {
                if (dispatcher == null || dispatcher.CheckAccess())
                    UnhookWinEvent(hook);
                else if (!dispatcher.HasShutdownStarted)
                    dispatcher.BeginInvoke(new Action(() => UnhookWinEvent(hook)));
            }
            catch
            {
            }
        }

        private static void OnForeground(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            Evaluate();
        }

        private static void Evaluate()
        {
            try
            {
                bool robloxInFront = IsRobloxInFront();
                bool busy = false;
                try { busy = _busy?.Invoke() ?? false; } catch { }

                bool watcher = _robloxPid != 0;
                bool belowNormal = !busy && (watcher || robloxInFront);
                bool eco = !busy && robloxInFront;

                lock (_sync)
                {
                    if (!_running)
                        return;
                    ApplyLocked(belowNormal, eco);
                }

                if (!_trimmed && !busy && Environment.TickCount64 - _startedMs >= TrimAfterMs)
                {
                    _trimmed = true;
                    TrimSelf();
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Check failed: {ex.Message}");
            }
        }

        private static void ApplyLocked(bool belowNormal, bool eco)
        {
            if (_appliedBelowNormal != belowNormal)
            {
                try
                {
                    using Process self = Process.GetCurrentProcess();
                    ProcessPriorityClass desired = belowNormal ? ProcessPriorityClass.BelowNormal : ProcessPriorityClass.Normal;
                    if (self.PriorityClass != desired)
                        self.PriorityClass = desired;
                    _appliedBelowNormal = belowNormal;
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Priority change failed: {ex.Message}");
                    _appliedBelowNormal = belowNormal;
                }
            }

            if (_appliedEco != eco)
            {
                SetEco(eco);
                _appliedEco = eco;
                App.Logger.WriteLine(LOG_IDENT, eco ? "Roblox is in front, PhasmaStrap is in efficiency mode" : $"Efficiency mode off (priority {(belowNormal ? "Below Normal" : "Normal")})");
            }
        }

        private static void SetEco(bool on)
        {
            try
            {
                var state = new POWER_THROTTLING_STATE
                {
                    Version = POWER_THROTTLING_CURRENT_VERSION,
                    ControlMask = on ? POWER_THROTTLING_EXECUTION_SPEED : 0,
                    StateMask = on ? POWER_THROTTLING_EXECUTION_SPEED : 0,
                };
                SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling, ref state, (uint)Marshal.SizeOf<POWER_THROTTLING_STATE>());
            }
            catch
            {
            }
        }

        private static void TrimSelf()
        {
            try
            {
                using Process self = Process.GetCurrentProcess();
                long before = self.WorkingSet64;
                EmptyWorkingSet(self.Handle);
                self.Refresh();
                App.Logger.WriteLine(LOG_IDENT, $"Gave start-up memory back to Windows: {before / 1048576} MB down to {self.WorkingSet64 / 1048576} MB");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Working set trim failed: {ex.Message}");
            }
        }

        internal static bool IsRobloxInFront()
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero || IsIconic(hwnd))
                return false;

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0)
                return false;

            if (_robloxPid != 0 && pid == (uint)_robloxPid)
                return true;

            if (pid == _lastForegroundPid)
                return _lastForegroundIsRoblox;

            bool isRoblox = IsRobloxProcess(pid);
            _lastForegroundPid = pid;
            _lastForegroundIsRoblox = isRoblox;
            return isRoblox;
        }

        private static bool IsRobloxProcess(uint pid)
        {
            IntPtr handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (handle == IntPtr.Zero)
                return false;

            try
            {
                var name = new StringBuilder(1024);
                uint size = (uint)name.Capacity;
                if (!QueryFullProcessImageName(handle, 0, name, ref size))
                    return false;

                string file = Path.GetFileNameWithoutExtension(name.ToString());
                return file.Equals(App.RobloxPlayerAppName, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                CloseHandle(handle);
            }
        }
    }
}
