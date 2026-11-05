using System;
using System.Collections.Generic;
using System.Windows.Interop;

namespace ZoneQuanta.Platform;

internal sealed class HotkeyService : IDisposable
{
    public const int PanelId = 0x5A51, QuickId = 0x5A52, PathId = 0x5A53;
    private readonly HwndSource _source;
    private readonly HashSet<int> _registered = new();

    public event Action<int>? Pressed;

    public HotkeyService()
    {
        var p = new HwndSourceParameters("ZoneQuantaHotkey") { ParentWindow = Native.HWND_MESSAGE, Width = 0, Height = 0 };
        _source = new HwndSource(p);
        _source.AddHook(Hook);
    }

    public bool Register(int id, uint modifiers, uint vk)
    {
        Unregister(id);
        if (vk == 0) return true;
        bool ok = Native.RegisterHotKey(_source.Handle, id, modifiers | Native.MOD_NOREPEAT, vk);
        if (ok) _registered.Add(id);
        return ok;
    }

    public void Unregister(int id)
    {
        if (!_registered.Remove(id)) return;
        Native.UnregisterHotKey(_source.Handle, id);
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && _registered.Contains(wParam.ToInt32()))
        {
            handled = true;
            Pressed?.Invoke(wParam.ToInt32());
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (int id in new List<int>(_registered)) Unregister(id);
        _source.RemoveHook(Hook);
        _source.Dispose();
    }
}
