using System;
using System.Windows.Threading;

namespace ZoneQuanta.Platform;

// Window events follow Explorer independently of the one-second metrics tick.
// The small geometry-only fallback covers shell animations that omit WinEvents.
internal sealed class TaskbarTracker : IDisposable
{
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render)
        { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Native.WinEventProc _callback;
    private IntPtr _hook;
    private Native.RECT _last;
    private bool _visible, _haveRect;
    public IntPtr Handle { get; private set; }
    public event Action? Changed;
    public event Action? LayoutChanged;

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
            LayoutChanged?.Invoke();
            _haveRect = false;
            if (tray != IntPtr.Zero)
            {
                Native.GetWindowThreadProcessId(tray, out uint process);
                _hook = Native.SetWinEventHook(0x8000, 0x800B, IntPtr.Zero, _callback, process, 0, 2);
            }
            Changed?.Invoke();
        }
        if (tray == IntPtr.Zero || !Native.GetWindowRect(tray, out var rect)) return;
        bool visible = Native.IsWindowVisible(tray);
        if (_haveRect && visible == _visible && rect.Left == _last.Left && rect.Top == _last.Top &&
            rect.Right == _last.Right && rect.Bottom == _last.Bottom) return;
        _last = rect;
        _visible = visible;
        _haveRect = true;
        Changed?.Invoke();
    }

    private void OnWindowEvent(IntPtr hook, uint evt, IntPtr hwnd, int objectId, int childId, uint thread, uint time)
    {
        if (hwnd == Handle && objectId == 0 && childId == 0 && evt == 0x800B) Check();
        else if ((evt <= 0x8004 || evt == 0x800B) && Handle != IntPtr.Zero &&
            (hwnd == Handle || Native.IsChild(Handle, hwnd)))
            LayoutChanged?.Invoke();
    }

    public void Dispose() { Stop(); GC.KeepAlive(_callback); }
}
