using System;

namespace ZoneQuanta.Platform;

// Raises Changed the moment the foreground window changes or the foreground window moves / resizes
// (a player entering fullscreen, Win+D), so fullscreen hiding and the desktop lift react immediately
// instead of waiting for the next one-second poll. Events arrive on the thread that installed the hook.
internal sealed class ForegroundWatcher : IDisposable
{
    private readonly Native.WinEventProc _callback;
    private IntPtr _foreground, _location;

    public event Action? Changed;

    public ForegroundWatcher()
    {
        _callback = OnEvent; // Keep the delegate alive until unhooked.
    }

    public void Start()
    {
        if (_foreground != IntPtr.Zero) return;
        uint flags = Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS;
        _foreground = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _callback, 0, 0, flags);
        _location = Native.SetWinEventHook(Native.EVENT_OBJECT_LOCATIONCHANGE, Native.EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero, _callback, 0, 0, flags);
    }

    private void OnEvent(IntPtr hook, uint evt, IntPtr hwnd, int objectId, int childId, uint thread, uint time)
    {
        // Location changes fire for every cursor and child object; only the foreground window itself matters.
        if (evt == Native.EVENT_OBJECT_LOCATIONCHANGE && (objectId != 0 || hwnd != Native.GetForegroundWindow())) return;
        Changed?.Invoke();
    }

    public void Dispose()
    {
        if (_foreground != IntPtr.Zero) Native.UnhookWinEvent(_foreground);
        if (_location != IntPtr.Zero) Native.UnhookWinEvent(_location);
        _foreground = _location = IntPtr.Zero;
        GC.KeepAlive(_callback);
    }
}
