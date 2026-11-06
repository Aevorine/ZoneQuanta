using System;
using System.Collections.Generic;
using System.Windows.Interop;
using System.Windows.Threading;

namespace ZoneQuanta.Platform;

// Global shortcuts. RegisterHotKey first (no hook, zero cost); a Win combination that Windows or another
// program already owns is taken over by the low-level hook instead (LowLevelKeys). Win + L stays the system's.
internal sealed class HotkeyService : IDisposable
{
    public const int PanelId = 0x5A51, QuickId = 0x5A52, PathId = 0x5A53;
    private const uint VK_L = 0x4C;
    private readonly HwndSource _source;
    private readonly HashSet<int> _registered = new(), _hooked = new();
    private readonly Dispatcher _dispatcher;

    public event Action<int>? Pressed;

    public HotkeyService()
    {
        var p = new HwndSourceParameters("ZoneQuantaHotkey") { ParentWindow = Native.HWND_MESSAGE, Width = 0, Height = 0 };
        _source = new HwndSource(p);
        _source.AddHook(Hook);
        _dispatcher = Dispatcher.CurrentDispatcher;
        LowLevelKeys.Shared.Fired += OnHookFired;
    }

    // How the shortcut with this id is held: "system" (RegisterHotKey), "hook" (taken over) or null.
    public string? Route(int id) => _registered.Contains(id) ? "system" : _hooked.Contains(id) ? "hook" : null;

    public bool Register(int id, uint modifiers, uint vk)
    {
        Unregister(id);
        if (vk == 0) return true;
        if (Native.RegisterHotKey(_source.Handle, id, modifiers | Native.MOD_NOREPEAT, vk))
        {
            _registered.Add(id);
            return true;
        }
        if ((modifiers & Native.MOD_WIN) == 0 || IsReserved(modifiers, vk)) return false;
        if (!LowLevelKeys.Shared.Bind(id, modifiers, vk))
        {
            // Not installed: the binding must not linger and swallow the chord once the hook comes up later.
            LowLevelKeys.Shared.Unbind(id);
            return false;
        }
        _hooked.Add(id);
        Core.Log.Write($"hotkey {id:X} {modifiers:X}+{vk:X} taken over by the keyboard hook");
        return true;
    }

    public void Unregister(int id)
    {
        if (_hooked.Remove(id)) LowLevelKeys.Shared.Unbind(id);
        if (!_registered.Remove(id)) return;
        Native.UnregisterHotKey(_source.Handle, id);
    }

    // Win + L locks the workstation below every hook; nothing can take it over.
    public static bool IsReserved(uint modifiers, uint vk) => (modifiers & Native.MOD_WIN) != 0 && vk == VK_L;

    // Free for RegisterHotKey right now (nobody, including this program, holds it).
    public static bool IsFree(uint modifiers, uint vk)
    {
        const int probe = 0x5AFF;
        if (!Native.RegisterHotKey(IntPtr.Zero, probe, modifiers | Native.MOD_NOREPEAT, vk)) return false;
        Native.UnregisterHotKey(IntPtr.Zero, probe);
        return true;
    }

    private void OnHookFired(int id) => _dispatcher.BeginInvoke(new Action(() =>
    {
        if (_hooked.Contains(id)) Pressed?.Invoke(id);
    }));

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
        foreach (int id in new List<int>(_hooked)) Unregister(id);
        LowLevelKeys.Shared.Fired -= OnHookFired;
        _source.RemoveHook(Hook);
        _source.Dispose();
    }
}
