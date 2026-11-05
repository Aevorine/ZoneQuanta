using System;
using System.Windows.Controls;
using System.Windows.Input;
using ZoneQuanta.Platform;
using ZoneQuanta.UI.Panel.Pages;

namespace ZoneQuanta.UI.Controls;

// Read-only field that records the next modifier + key combination pressed while focused.
public sealed class HotkeyBox : TextBox
{
    private readonly Func<(uint Mods, uint Key)> _get;
    private readonly Action<uint, uint> _set;

    public HotkeyBox(Func<(uint Mods, uint Key)> get, Action<uint, uint> set)
    {
        _get = get;
        _set = set;
        IsReadOnly = true;
        Cursor = Cursors.Hand;
        SetResourceReference(StyleProperty, "Field");
        Show();
        PreviewKeyDown += OnKey;
        GotKeyboardFocus += (_, _) => Text = "按下新的快捷键…";
        LostKeyboardFocus += (_, _) => Show();
    }

    public void Show()
    {
        var (m, k) = _get();
        Text = SystemPage.Format(m, k);
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.None)
            return;
        if (key == Key.Escape)
        {
            Keyboard.ClearFocus();
            return;
        }
        uint mods = 0;
        var m = Keyboard.Modifiers;
        if (m.HasFlag(ModifierKeys.Control)) mods |= Native.MOD_CONTROL;
        if (m.HasFlag(ModifierKeys.Alt)) mods |= Native.MOD_ALT;
        if (m.HasFlag(ModifierKeys.Shift)) mods |= Native.MOD_SHIFT;
        if (m.HasFlag(ModifierKeys.Windows)) mods |= Native.MOD_WIN;
        if (mods == 0) return;
        _set(mods, (uint)KeyInterop.VirtualKeyFromKey(key));
        Keyboard.ClearFocus();
        Show();
    }
}
