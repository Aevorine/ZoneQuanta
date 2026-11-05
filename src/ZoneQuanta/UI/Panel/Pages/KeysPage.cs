using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using ZoneQuanta.Core.Settings;
using ZoneQuanta.UI.Controls;

namespace ZoneQuanta.UI.Panel.Pages;

// Every shortcut in one place: record, reset, switch off. Saved in settings.json.
public sealed class KeysPage : UserControl
{
    private sealed record GlobalDef(string Id, string Label, string EnabledProp, uint DefMods, uint DefKey);

    private static readonly GlobalDef[] Globals =
    {
        new("Panel", "显示 / 隐藏面板", nameof(AppSettings.HotkeyEnabled), 3, 0x5A),
        new("Quick", "快捷面板", nameof(AppSettings.QuickHotkeyEnabled), 3, 0x20),
        new("Path", "打开路径", nameof(AppSettings.PathHotkeyEnabled), 3, 0x47),
    };

    private static readonly (string Id, string Label)[] Locals =
    {
        ("Reveal", "打开所在位置"), ("Rename", "重命名"), ("Remove", "移除"),
        ("CopyPath", "复制路径"), ("AddQuick", "加入快捷面板"), ("Complete", "补全路径"),
    };

    private static readonly Key[] Reserved = { Key.Return, Key.Escape, Key.Up, Key.Down, Key.Left, Key.Right };

    // These also act inside the path box, so a plain typing key would stop the user from typing it.
    private static readonly string[] TypedIn = { "Reveal", "AddQuick", "Complete" };

    private static bool TypingKey(KeyChord c) =>
        (c.Mods & ~ModifierKeys.Shift) == ModifierKeys.None && !(c.Key is >= Key.F1 and <= Key.F24 or Key.Tab or Key.Return);

    private readonly AppSettings _s;

    public KeysPage(IPanelHost host)
    {
        _s = host.Settings;
        DataContext = _s;

        var rows = new List<FrameworkElement> { Heading("全局") };
        foreach (var g in Globals) rows.Add(GlobalRow(g));
        rows.Add(Heading("快捷面板与打开路径"));
        foreach (var (id, label) in Locals) rows.Add(LocalRow(id, label));

        var grid = new Grid();
        for (int i = 0; i < rows.Count; i++)
        {
            bool heading = rows[i] is TextBlock;
            grid.RowDefinitions.Add(new RowDefinition { Height = heading ? GridLength.Auto : new GridLength(1, GridUnitType.Star), MinHeight = heading ? 0 : 42 });
            Grid.SetRow(rows[i], i);
            grid.Children.Add(rows[i]);
        }

        var card = new Border { CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Padding = new Thickness(8, 6, 8, 8), Child = grid };
        card.SetResourceReference(Border.BackgroundProperty, "CardBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        Content = card;
    }

    private static TextBlock Heading(string text)
    {
        var t = new TextBlock { Text = text, Margin = new Thickness(12, 10, 0, 4) };
        t.SetResourceReference(StyleProperty, "Heading");
        return t;
    }

    private (uint Mods, uint Key) GetGlobal(string id) => id switch
    {
        "Quick" => (_s.QuickHotkeyModifiers, _s.QuickHotkeyKey),
        "Path" => (_s.PathHotkeyModifiers, _s.PathHotkeyKey),
        _ => (_s.HotkeyModifiers, _s.HotkeyKey),
    };

    private FrameworkElement GlobalRow(GlobalDef g)
    {
        HotkeyBox box = null!;
        box = new HotkeyBox(() => { var (m, k) = GetGlobal(g.Id); return KeyChord.FromGlobal(m, k); }, chord =>
        {
            foreach (var other in Globals.Where(o => o.Id != g.Id))
            {
                var (m, k) = GetGlobal(other.Id);
                if (m == chord.GlobalMods && k == chord.VirtualKey) return $"与「{other.Label}」相同";
            }
            _s.SetGlobalHotkey(g.Id, chord.GlobalMods, chord.VirtualKey);
            return null;
        }, needsModifier: true);

        var sw = new CheckBox();
        sw.SetResourceReference(StyleProperty, "Switch");
        sw.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding(g.EnabledProp) { Mode = BindingMode.TwoWay });
        return Row(g.Label, box, sw, () => box.Apply(KeyChord.FromGlobal(g.DefMods, g.DefKey)));
    }

    private FrameworkElement LocalRow(string id, string label)
    {
        HotkeyBox box = null!;
        box = new HotkeyBox(() => KeyChord.Parse(_s.LocalKey(id)), chord =>
        {
            if (chord.Mods == ModifierKeys.None && Reserved.Contains(chord.Key)) return "该键用于导航";
            if (TypedIn.Contains(id) && TypingKey(chord)) return "该键用于输入，请加 Ctrl 或 Alt";
            foreach (var (other, otherLabel) in Locals.Where(o => o.Id != id))
                if (KeyChord.Parse(_s.LocalKey(other)) == chord) return $"与「{otherLabel}」相同";
            _s.SetLocalKey(id, chord.Storage);
            return null;
        }, needsModifier: false);
        return Row(label, box, null, () => box.Apply(KeyChord.Parse(AppSettings.DefaultLocalKeys[id])));
    }

    private static FrameworkElement Row(string label, HotkeyBox box, CheckBox? sw, Action reset)
    {
        var g = new Grid { Margin = new Thickness(12, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        g.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(box, 1);
        g.Children.Add(box);
        if (sw is not null)
        {
            sw.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(sw, 2);
            g.Children.Add(sw);
        }
        var undo = new Button { Tag = Application.Current.FindResource("Icon.Reset"), ToolTip = "恢复默认" };
        undo.SetResourceReference(StyleProperty, "IconButton");
        undo.Click += (_, _) => reset();
        Grid.SetColumn(undo, 3);
        g.Children.Add(undo);
        return g;
    }
}
