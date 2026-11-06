using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace ZoneQuanta.Platform;

// Low-level keyboard hook (WH_KEYBOARD_LL) on its own thread, for what RegisterHotKey cannot do:
// 1. Win shortcuts Windows keeps for itself (Win+E, Win+Q …): the hook sees the keys before the shell, swallows the
//    chord and sends an unassigned key (vkE8) so releasing Win does not open Start;
// 2. recording a shortcut with Win: every key is swallowed while recording, so the shell never acts on it.
// Installed only while either is needed; the callback compares a few integers and returns (well under 1 µs),
// and runs on a dedicated thread so a busy UI thread never delays the user's typing.
internal sealed class LowLevelKeys
{
    public static LowLevelKeys Shared { get; } = new();

    private const int WH_KEYBOARD_LL = 13, WM_KEYDOWN = 0x100, WM_SYSKEYDOWN = 0x104;
    private const uint WM_APP_SYNC = 0x8001, WM_APP_MASK = 0x8002, PM_NOREMOVE = 0;
    private const ushort VK_MASK = 0xE8;
    private const uint VK_LWIN = 0x5B, VK_RWIN = 0x5C;
    // Our own injected keys carry this, so the hook lets them through untouched.
    private static readonly IntPtr Marker = new(0x5A514B59);

    private readonly HookProc _proc;
    private readonly object _gate = new();
    private readonly ManualResetEventSlim _synced = new(false);
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _hook;
    private volatile Dictionary<(uint Mods, uint Vk), int> _binds = new();
    private volatile bool _recording, _recordStart;
    private volatile Action<uint, uint>? _onRecord;
    // Hook thread only: modifiers held while recording (they are swallowed, so tracked here) and keys whose
    // press was swallowed (their release and repeats are swallowed too).
    private uint _recMods;
    private readonly HashSet<uint> _eaten = new();

    // Raised on the hook thread with the binding id; subscribers marshal to their own thread.
    public event Action<int>? Fired;

    public bool Installed => _hook != IntPtr.Zero;

    private LowLevelKeys() => _proc = Callback;

    // Takes over a chord (MOD_* flags without MOD_NOREPEAT, virtual key) for the given id.
    public bool Bind(int id, uint mods, uint vk)
    {
        lock (_gate)
        {
            var next = new Dictionary<(uint, uint), int>(_binds);
            foreach (var kv in _binds) if (kv.Value == id) next.Remove(kv.Key);
            next[(mods & 0xF, vk)] = id;
            _binds = next;
        }
        return Sync();
    }

    public void Unbind(int id)
    {
        lock (_gate)
        {
            var next = new Dictionary<(uint, uint), int>(_binds);
            foreach (var kv in _binds) if (kv.Value == id) next.Remove(kv.Key);
            if (next.Count == _binds.Count) return;
            _binds = next;
        }
        Sync();
    }

    // Every key goes to onChord(mods, vk) instead of the system until EndRecording; false if the hook is unavailable.
    public bool BeginRecording(Action<uint, uint> onChord)
    {
        _onRecord = onChord;
        _recordStart = true;
        _recording = true;
        if (Sync()) return true;
        _recording = false;
        _onRecord = null;
        return false;
    }

    public void EndRecording()
    {
        if (!_recording) return;
        _recording = false;
        _onRecord = null;
        Sync();
    }

    // Installs or removes the hook on its thread to match what is needed now; true when installed.
    private bool Sync()
    {
        if (!EnsureThread()) return false;
        _synced.Reset();
        if (!PostThreadMessage(_threadId, WM_APP_SYNC, IntPtr.Zero, IntPtr.Zero)) return false;
        _synced.Wait(500);
        return _hook != IntPtr.Zero || (_binds.Count == 0 && !_recording);
    }

    private bool EnsureThread()
    {
        lock (_gate)
        {
            if (_thread is not null) return _threadId != 0;
            using var ready = new ManualResetEventSlim(false);
            _thread = new Thread(() => Run(ready)) { IsBackground = true, Name = "ZoneQuanta keys", Priority = ThreadPriority.Highest };
            _thread.Start();
            ready.Wait(2000);
            return _threadId != 0;
        }
    }

    private void Run(ManualResetEventSlim ready)
    {
        PeekMessage(out _, IntPtr.Zero, 0, 0, PM_NOREMOVE); // creates the message queue
        _threadId = GetCurrentThreadId();
        ready.Set();
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == WM_APP_SYNC)
            {
                bool need = _binds.Count > 0 || _recording || _eaten.Count > 0;
                if (need && _hook == IntPtr.Zero)
                {
                    _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
                    Core.Log.Write(_hook == IntPtr.Zero ? $"keys hook failed {Marshal.GetLastWin32Error()}" : "keys hook on");
                }
                else if (!need && _hook != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(_hook);
                    _hook = IntPtr.Zero;
                    Core.Log.Write("keys hook off");
                }
                _synced.Set();
            }
            else if (msg.message == WM_APP_MASK) Mask();
        }
        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
    }

    private IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        // KBDLLHOOKSTRUCT: vkCode @0, flags @8, dwExtraInfo @16 (x64 and x86 alike).
        if (code >= 0 && Marshal.ReadIntPtr(lParam, 16) != Marker)
        {
            uint vk = (uint)Marshal.ReadInt32(lParam, 0);
            int msg = wParam.ToInt32();
            if (Handle(vk, msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)) return new IntPtr(1);
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private bool Handle(uint vk, bool down)
    {
        if (_recording)
        {
            // Our window lost the foreground (Alt+Tab is swallowed, but a click elsewhere is not): stop at once,
            // so the keyboard can never stay captured.
            if (!OwnForeground())
            {
                _recording = false;
                _onRecord = null;
                PostThreadMessage(_threadId, WM_APP_SYNC, IntPtr.Zero, IntPtr.Zero);
            }
            else
            {
                if (_recordStart)
                {
                    _recordStart = false;
                    _recMods = HeldMods();
                }
                uint bit = ModBit(vk);
                if (down)
                {
                    // Held since before recording: the system saw the press, so its repeats are swallowed but its
                    // release must go through (else the key stays down system-wide).
                    bool seen = Down((int)vk) && !_eaten.Contains(vk);
                    if (bit != 0) _recMods |= bit;
                    else if (!_eaten.Contains(vk) && !seen)
                    {
                        if ((_recMods & Native.MOD_WIN) != 0) PostThreadMessage(_threadId, WM_APP_MASK, IntPtr.Zero, IntPtr.Zero);
                        _onRecord?.Invoke(_recMods, vk);
                    }
                    if (!seen) _eaten.Add(vk);
                    return true;
                }
                if (bit != 0) _recMods &= ~bit;
                return _eaten.Remove(vk);
            }
        }
        if (!down)
        {
            if (!_eaten.Remove(vk)) return false;
            if (_eaten.Count == 0 && _binds.Count == 0) PostThreadMessage(_threadId, WM_APP_SYNC, IntPtr.Zero, IntPtr.Zero);
            return true;
        }
        if (_eaten.Contains(vk)) return true;
        var binds = _binds;
        if (binds.Count == 0 || ModBit(vk) != 0) return false;
        // A repeat of a key the foreground program already saw pressed: not ours to take (its release would be lost).
        if (Down((int)vk)) return false;
        // Modifiers pass through untouched in this mode, so the system's own state is exact.
        uint mods = HeldMods();
        if (!binds.TryGetValue((mods, vk), out int id)) return false;
        _eaten.Add(vk);
        if ((mods & Native.MOD_WIN) != 0) PostThreadMessage(_threadId, WM_APP_MASK, IntPtr.Zero, IntPtr.Zero);
        Fired?.Invoke(id);
        return true;
    }

    // Win was held through a swallowed chord: an unassigned key in between tells the shell "Win was used",
    // so Start stays closed. It also counts as our input, which lets the panel we open take the foreground.
    private static void Mask()
    {
        var inputs = new INPUT[2];
        inputs[0].type = inputs[1].type = 1;
        inputs[0].u.ki = new KEYBDINPUT { wVk = VK_MASK, dwExtraInfo = Marker };
        inputs[1].u.ki = new KEYBDINPUT { wVk = VK_MASK, dwFlags = 2, dwExtraInfo = Marker };
        SendInput(2, inputs, Marshal.SizeOf<INPUT>());
    }

    private static uint HeldMods()
    {
        uint m = 0;
        if (Down(0x11)) m |= Native.MOD_CONTROL;
        if (Down(0x12)) m |= Native.MOD_ALT;
        if (Down(0x10)) m |= Native.MOD_SHIFT;
        if (Down((int)VK_LWIN) || Down((int)VK_RWIN)) m |= Native.MOD_WIN;
        return m;
    }

    private static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    private static uint ModBit(uint vk) => vk switch
    {
        0x10 or 0xA0 or 0xA1 => Native.MOD_SHIFT,
        0x11 or 0xA2 or 0xA3 => Native.MOD_CONTROL,
        0x12 or 0xA4 or 0xA5 => Native.MOD_ALT,
        VK_LWIN or VK_RWIN => Native.MOD_WIN,
        _ => 0,
    };

    private static bool OwnForeground()
    {
        var fg = Native.GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;
        Native.GetWindowThreadProcessId(fg, out uint pid);
        return pid == (uint)Environment.ProcessId;
    }

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public InputUnion u; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out MSG msg, IntPtr hwnd, uint min, uint max, uint remove);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint thread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);
}
