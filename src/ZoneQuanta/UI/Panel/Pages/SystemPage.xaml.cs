using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Input;
using ZoneQuanta.Platform;

namespace ZoneQuanta.UI.Panel.Pages;

public partial class SystemPage : UserControl
{
    private readonly IPanelHost _host;

    public SystemPage(IPanelHost host)
    {
        _host = host;
        InitializeComponent();
        DataContext = host.Settings;

        ShowHotkey();
        HotkeyBox.PreviewKeyDown += OnKey;
        HotkeyBox.GotKeyboardFocus += (_, _) => HotkeyBox.Text = "按下新的快捷键…";
        HotkeyBox.LostKeyboardFocus += (_, _) => ShowHotkey();
        FolderButton.Click += (_, _) => host.OpenSettingsFolder();
        ExitButton.Click += (_, _) => host.ExitApp();
    }

    public static string Format(uint modifiers, uint vk)
    {
        var parts = new List<string>();
        if ((modifiers & Native.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((modifiers & Native.MOD_ALT) != 0) parts.Add("Alt");
        if ((modifiers & Native.MOD_SHIFT) != 0) parts.Add("Shift");
        if ((modifiers & Native.MOD_WIN) != 0) parts.Add("Win");
        parts.Add(KeyInterop.KeyFromVirtualKey((int)vk).ToString());
        return string.Join(" + ", parts);
    }

    private void ShowHotkey() => HotkeyBox.Text = Format(_host.Settings.HotkeyModifiers, _host.Settings.HotkeyKey);

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

        _host.Settings.HotkeyModifiers = mods;
        _host.Settings.HotkeyKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        Keyboard.ClearFocus();
        ShowHotkey();
    }
}
