using System.Runtime.InteropServices;

namespace PhasmaStrap.Integrations
{
    public sealed class SoftKey : IDisposable
    {
        private const string LOG_IDENT = "SoftKey";

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
        private const uint WM_QUIT = 0x0012;
        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x1, KEYEVENTF_KEYUP = 0x2, KEYEVENTF_SCANCODE = 0x8;
        private const uint MAPVK_VK_TO_VSC = 0;

        private static readonly IntPtr Marker = new(0x50534B59);

        public static readonly string[] ProfileNames = { "WASD", "ZQSD", "ESDF", "Arrows" };

        private static readonly Dictionary<string, (int Up, int Left, int Down, int Right)> Profiles = new(StringComparer.OrdinalIgnoreCase)
        {
            { "WASD", (0x57, 0x41, 0x53, 0x44) },
            { "ZQSD", (0x5A, 0x51, 0x53, 0x44) },
            { "ESDF", (0x45, 0x53, 0x44, 0x46) },
            { "Arrows", (0x26, 0x25, 0x28, 0x27) },
        };

        private sealed class Axis
        {
            public int First, Second;
            public bool FirstHeld, SecondHeld;
            public int Released;

            public bool Owns(int key) => key == First || key == Second;

            public int Other(int key) => key == First ? Second : First;

            public bool Held(int key) => key == First ? FirstHeld : SecondHeld;

            public void SetHeld(int key, bool held)
            {
                if (key == First)
                    FirstHeld = held;
                else
                    SecondHeld = held;
            }

            public void Clear()
            {
                FirstHeld = SecondHeld = false;
                Released = 0;
            }
        }

        private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode, scanCode, flags, time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk, wScan;
            public uint dwFlags, time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Explicit, Size = 40)]
        private struct INPUT
        {
            [FieldOffset(0)] public uint type;
            [FieldOffset(8)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam, lParam;
            public uint time;
            public int x, y;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);

        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern int GetMessage(out MSG message, IntPtr hwnd, uint min, uint max);

        [DllImport("user32.dll")]
        private static extern bool PostThreadMessage(uint thread, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint SendInput(uint count, INPUT[] inputs, int size);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint code, uint type);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string? name);

        private readonly int _robloxPid;
        private readonly string _profile;
        private readonly Axis _horizontal;
        private readonly Axis _vertical;
        private readonly HookProc _proc;

        private Thread? _thread;
        private uint _threadId;
        private IntPtr _hook;
        private long _resolved;

        public SoftKey(int robloxPid, string? profile)
        {
            _robloxPid = robloxPid;
            _profile = profile is not null && Profiles.ContainsKey(profile) ? profile : "WASD";

            var keys = Profiles[_profile];
            _horizontal = new Axis { First = keys.Left, Second = keys.Right };
            _vertical = new Axis { First = keys.Up, Second = keys.Down };
            _proc = Callback;
        }

        public void Start()
        {
            if (_thread is not null)
                return;

            _thread = new Thread(Run) { IsBackground = true, Name = "SoftKey" };
            _thread.Start();
        }

        private void Run()
        {
            _threadId = GetCurrentThreadId();
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);

            if (_hook == IntPtr.Zero)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not listen for keys (error {Marshal.GetLastWin32Error()})");
                return;
            }

            App.Logger.WriteLine(LOG_IDENT, $"Resolving opposite keys for {_profile} while Roblox {_robloxPid} is in front");

            while (GetMessage(out MSG _, IntPtr.Zero, 0, 0) > 0)
            {
            }

            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;

            App.Logger.WriteLine(LOG_IDENT, $"Stopped after resolving {Interlocked.Read(ref _resolved)} overlapping press(es)");
        }

        private bool RobloxInFront()
        {
            IntPtr window = GetForegroundWindow();

            if (window == IntPtr.Zero)
                return false;

            GetWindowThreadProcessId(window, out uint process);
            return process == (uint)_robloxPid;
        }

        private IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0)
            {
                try
                {
                    var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

                    if (data.dwExtraInfo != Marker)
                    {
                        int message = wParam.ToInt32();
                        bool down = message is WM_KEYDOWN or WM_SYSKEYDOWN;
                        bool up = message is WM_KEYUP or WM_SYSKEYUP;

                        if (down || up)
                            Handle((int)data.vkCode, down);
                    }
                }
                catch (Exception)
                {
                }
            }

            return CallNextHookEx(_hook, code, wParam, lParam);
        }

        private void Handle(int key, bool down)
        {
            Axis? axis = _horizontal.Owns(key) ? _horizontal : _vertical.Owns(key) ? _vertical : null;

            if (axis is null)
                return;

            if (!RobloxInFront())
            {
                _horizontal.Clear();
                _vertical.Clear();
                return;
            }

            int other = axis.Other(key);

            if (down)
            {
                if (axis.Held(key))
                    return;

                axis.SetHeld(key, true);

                if (axis.Held(other) && axis.Released != other)
                {
                    Send(other, false);
                    axis.Released = other;
                    Interlocked.Increment(ref _resolved);
                }

                return;
            }

            axis.SetHeld(key, false);

            if (axis.Released == key)
            {
                axis.Released = 0;
            }
            else if (axis.Released == other && axis.Held(other))
            {
                Send(other, true);
                axis.Released = 0;
            }
        }

        private static void Send(int key, bool down)
        {
            uint flags = KEYEVENTF_SCANCODE | (down ? 0 : KEYEVENTF_KEYUP);

            if (key is >= 0x25 and <= 0x28)
                flags |= KEYEVENTF_EXTENDEDKEY;

            var input = new INPUT
            {
                type = INPUT_KEYBOARD,
                ki = new KEYBDINPUT
                {
                    wVk = 0,
                    wScan = (ushort)MapVirtualKey((uint)key, MAPVK_VK_TO_VSC),
                    dwFlags = flags,
                    dwExtraInfo = Marker,
                },
            };

            SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        }

        public void Dispose()
        {
            if (_threadId != 0)
                PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);

            _thread = null;
        }
    }
}
