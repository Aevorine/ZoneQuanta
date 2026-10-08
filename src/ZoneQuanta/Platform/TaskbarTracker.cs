using System;
using System.Windows.Threading;

namespace ZoneQuanta.Platform;

// Window events follow Explorer independently of the metrics sampling. The geometry-only timer
// covers shell animations that omit WinEvents: it runs at frame rate while the taskbar is moving
// and relaxes to a slow heartbeat once it has been still for a moment.
internal sealed class TaskbarTracker : IDisposable
{
    private const int FastMs = 16, IdleMs = 120, SettleMs = 700;

    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render)
        { Interval = TimeSpan.FromMilliseconds(IdleMs) };
    private readonly Native.WinEventProc _callback;
    private IntPtr _hook;
    private Native.RECT _last;
    private bool _visible, _haveRect;
    private long _movingUntil, _activeUntil;
    public bool IsMoving => Environment.TickCount64 < _movingUntil;
    public int MovingRemainingMs => (int)Math.Max(0, _movingUntil - Environment.TickCount64);
    public IntPtr Handle { get; private set; }
    public event Action? Changed;
    // The cached free-space layout may be stale (a taskbar button appeared, moved or went away).
    public event Action? LayoutChanged;
    // A different taskbar window (Explorer restarted): nothing known about the old one applies.
    public event Action? Rebuilt;

    public TaskbarTracker()
    {
        _callback = OnWindowEvent; // Keep the delegate alive until unhooked.
        _timer.Tick += (_, _) => Check();
    }

    public void Start() { Check(); _timer.Start(); }
    public void Stop()
    {
        _timer.Stop();
        if (_hook != IntPtr.Zero) Native.UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
        Handle = IntPtr.Zero;
        _haveRect = false;
    }

    private void Check()
    {
        IntPtr tray = Native.FindWindow("Shell_TrayWnd", null);
        if (tray != Handle)
        {
            if (_hook != IntPtr.Zero) Native.UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
            Handle = tray;
            Rebuilt?.Invoke();
            _haveRect = false;
            if (tray != IntPtr.Zero)
            {
                Native.GetWindowThreadProcessId(tray, out uint process);
                _hook = Native.SetWinEventHook(0x8000, 0x800B, IntPtr.Zero, _callback, process, 0, 2);
            }
            Changed?.Invoke();
        }
        if (tray == IntPtr.Zero || !Native.GetWindowRect(tray, out var rect))
        {
            Pace();
            return;
        }
        bool visible = Native.IsWindowVisible(tray);
        if (_haveRect && visible == _visible && rect.Left == _last.Left && rect.Top == _last.Top &&
            rect.Right == _last.Right && rect.Bottom == _last.Bottom)
        {
            Pace();
            return;
        }
        long now = Environment.TickCount64;
        if (_haveRect && (rect.Left != _last.Left || rect.Top != _last.Top))
            _movingUntil = now + 180;
        _activeUntil = now + SettleMs;
        _last = rect;
        _visible = visible;
        _haveRect = true;
        Pace();
        Changed?.Invoke();
    }

    private void Pace()
    {
        var wanted = TimeSpan.FromMilliseconds(Environment.TickCount64 < _activeUntil ? FastMs : IdleMs);
        if (_timer.Interval != wanted) _timer.Interval = wanted;
    }

    private void OnWindowEvent(IntPtr hook, uint evt, IntPtr hwnd, int objectId, int childId, uint thread, uint time)
    {
        if (Handle == IntPtr.Zero || (hwnd != Handle && !Native.IsChild(Handle, hwnd))) return;
        Native.GetWindowThreadProcessId(hwnd, out uint owner);
        if (owner == (uint)Environment.ProcessId) return;
        Check();
        // Show/hide and descendant movement during an auto-hide slide do not
        // change the safe horizontal gap. Keep the cached layout alive.
        if (evt is 0x8002 or 0x8003 || IsMoving) return;
        if ((evt <= 0x8004 || evt == 0x800B) && !(hwnd == Handle && objectId == 0 && childId == 0))
            LayoutChanged?.Invoke();
    }

    public void Dispose() { Stop(); GC.KeepAlive(_callback); }
}
