using System;
using System.Windows.Interop;

namespace ZoneQuanta.Platform;

internal sealed class HotkeyService : IDisposable
{
    private const int HotkeyId = 0x5A51;
    private readonly HwndSource _source;
    private bool _registered;

    public event Action? Pressed;

    public HotkeyService()
    {
        var p = new HwndSourceParameters("ZoneQuantaHotkey") { ParentWindow = Native.HWND_MESSAGE, Width = 0, Height = 0 };
        _source = new HwndSource(p);
        _source.AddHook(Hook);
    }

    public bool Register(uint modifiers, uint vk)
    {
        Unregister();
        if (vk == 0) return true;
        _registered = Native.RegisterHotKey(_source.Handle, HotkeyId, modifiers | Native.MOD_NOREPEAT, vk);
        return _registered;
    }

    public void Unregister()
    {
        if (!_registered) return;
        Native.UnregisterHotKey(_source.Handle, HotkeyId);
        _registered = false;
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        _source.RemoveHook(Hook);
        _source.Dispose();
    }
}
