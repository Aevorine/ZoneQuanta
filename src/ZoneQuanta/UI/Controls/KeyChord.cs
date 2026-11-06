using System;
using System.Collections.Generic;
using System.Windows.Input;
using ZoneQuanta.Platform;

namespace ZoneQuanta.UI.Controls;

// A modifier + key combination, stored as "Ctrl+Shift+F2" and shown as "Ctrl + Shift + F2".
public readonly record struct KeyChord(ModifierKeys Mods, Key Key)
{
    private static readonly Dictionary<Key, string> Names = new()
    {
        [Key.Return] = "Enter", [Key.Back] = "Backspace", [Key.Delete] = "Del", [Key.Escape] = "Esc",
        [Key.Next] = "PageDown", [Key.Prior] = "PageUp", [Key.Space] = "Space", [Key.Capital] = "CapsLock",
        [Key.OemPlus] = "=", [Key.OemMinus] = "-", [Key.OemComma] = ",", [Key.OemPeriod] = ".",
    };

    public static KeyChord Parse(string text)
    {
        var mods = ModifierKeys.None;
        var key = Key.None;
        foreach (string raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl": mods |= ModifierKeys.Control; break;
                case "alt": mods |= ModifierKeys.Alt; break;
                case "shift": mods |= ModifierKeys.Shift; break;
                case "win": mods |= ModifierKeys.Windows; break;
                default: if (Enum.TryParse(raw, true, out Key k)) key = k; break;
            }
        }
        return new KeyChord(mods, key);
    }

    public static KeyChord FromGlobal(uint mods, uint vk)
    {
        var m = ModifierKeys.None;
        if ((mods & Native.MOD_CONTROL) != 0) m |= ModifierKeys.Control;
        if ((mods & Native.MOD_ALT) != 0) m |= ModifierKeys.Alt;
        if ((mods & Native.MOD_SHIFT) != 0) m |= ModifierKeys.Shift;
        if ((mods & Native.MOD_WIN) != 0) m |= ModifierKeys.Windows;
        return new KeyChord(m, KeyInterop.KeyFromVirtualKey((int)vk));
    }

    public uint GlobalMods =>
        ((Mods & ModifierKeys.Control) != 0 ? Native.MOD_CONTROL : 0) | ((Mods & ModifierKeys.Alt) != 0 ? Native.MOD_ALT : 0)
        | ((Mods & ModifierKeys.Shift) != 0 ? Native.MOD_SHIFT : 0) | ((Mods & ModifierKeys.Windows) != 0 ? Native.MOD_WIN : 0);

    public uint VirtualKey => (uint)KeyInterop.VirtualKeyFromKey(Key);

    public string Storage => Join("+", Key.ToString());

    public string Display => Join(" + ", Names.TryGetValue(Key, out var n) ? n : Key.ToString());

    private string Join(string sep, string key)
    {
        var parts = new List<string>();
        if ((Mods & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((Mods & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((Mods & ModifierKeys.Shift) != 0) parts.Add("Shift");
        if ((Mods & ModifierKeys.Windows) != 0) parts.Add("Win");
        parts.Add(key);
        return string.Join(sep, parts);
    }

    public bool Matches(KeyEventArgs e)
    {
        Key k = e.Key == Key.System ? e.SystemKey : e.Key;
        return k == Key && CurrentMods() == Mods;
    }

    // Keyboard.Modifiers never reports the Win key (WPF reads only Ctrl, Alt and Shift), so it is added here.
    public static ModifierKeys CurrentMods()
    {
        var m = Keyboard.Modifiers;
        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) m |= ModifierKeys.Windows;
        return m;
    }

    public static bool IsModifier(Key k) =>
        k is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.None;
}
