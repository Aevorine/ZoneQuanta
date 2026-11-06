using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Win32 = Microsoft.Win32;

namespace ZoneQuanta.UI.Quick;

// The folders whose new shortcuts join the quick panel: the two Start menu folders (fixed) and the user's own.
// Add, remove, scan now, or import what is already there.
public sealed class WatchFoldersPanel : Border
{
    private readonly QuickActions _actions;
    private readonly StackPanel _list = new();
    private readonly TextBlock _result = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };

    public WatchFoldersPanel(QuickActions actions)
    {
        _actions = actions;
        Width = 420;
        Padding = new Thickness(16, 12, 12, 12);
        CornerRadius = new CornerRadius(14);
        BorderThickness = new Thickness(1);
        SetResourceReference(BackgroundProperty, "CardBrush");
        SetResourceReference(BorderBrushProperty, "LineBrush");

        var title = new TextBlock { Text = "监视的文件夹", Margin = new Thickness(0, 0, 0, 8) };
        title.SetResourceReference(StyleProperty, "Heading");

        var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(ActionButton("添加文件夹", Add));
        buttons.Children.Add(ActionButton("立即扫描", () => Run(false)));
        buttons.Children.Add(ActionButton("导入已有", () => Run(true)));
        _result.SetResourceReference(StyleProperty, "Muted");
        buttons.Children.Add(_result);

        var stack = new StackPanel();
        stack.Children.Add(title);
        stack.Children.Add(_list);
        stack.Children.Add(buttons);
        Child = stack;
        Loaded += (_, _) => Fill();
    }

    private static Button ActionButton(string text, Action act)
    {
        var b = new Button { Content = text, Margin = new Thickness(0, 0, 6, 6) };
        b.SetResourceReference(StyleProperty, "Action");
        b.Click += (_, _) => act();
        return b;
    }

    private void Fill()
    {
        _list.Children.Clear();
        var extra = _actions.Settings.QuickWatchFolders;
        foreach (var dir in ShortcutFolderSync.DefaultFolders.Concat(extra).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            bool fixedOne = ShortcutFolderSync.DefaultFolders.Contains(dir, StringComparer.OrdinalIgnoreCase);
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2), MinHeight = 32 };
            var icon = new System.Windows.Shapes.Path { Data = (Geometry)Application.Current.FindResource(fixedOne ? "Icon.Start" : "Icon.Folder"), Width = 16, Height = 16, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            icon.SetResourceReference(StyleProperty, "IconPath");
            icon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, Directory.Exists(dir) ? "Accent1Brush" : "MutedBrush");
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);
            if (!fixedOne)
            {
                var remove = new Button { Tag = Application.Current.FindResource("Icon.Close"), ToolTip = "移除" };
                remove.SetResourceReference(StyleProperty, "IconButton");
                string d = dir;
                remove.Click += (_, _) =>
                {
                    _actions.Settings.QuickWatchFolders = _actions.Settings.QuickWatchFolders.Where(x => !string.Equals(x, d, StringComparison.OrdinalIgnoreCase)).ToList();
                    Fill();
                };
                DockPanel.SetDock(remove, Dock.Right);
                row.Children.Add(remove);
            }
            var text = new TextBlock { Text = dir, ToolTip = dir, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(text);
            _list.Children.Add(row);
        }
    }

    private void Add()
    {
        var dlg = new Win32.OpenFolderDialog { Multiselect = true };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        var next = new List<string>(_actions.Settings.QuickWatchFolders);
        foreach (var f in dlg.FolderNames)
            if (!next.Contains(f, StringComparer.OrdinalIgnoreCase) && !ShortcutFolderSync.DefaultFolders.Contains(f, StringComparer.OrdinalIgnoreCase)) next.Add(f);
        _actions.Settings.QuickWatchFolders = next;
        Fill();
    }

    private async void Run(bool existing)
    {
        if (_actions.FolderSync is not { } sync) return;
        _result.Text = "…";
        int n = await sync.RunAsync(existing);
        _result.Text = n > 0 ? $"已导入 {n}" : "没有新的应用";
    }

    public static Popup Flyout(UIElement anchor, QuickActions actions)
    {
        var host = new Border { Margin = new Thickness(12, 4, 12, 16), Child = new WatchFoldersPanel(actions) };
        host.Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Opacity = 0.3, Direction = 270, Color = Colors.Black };
        return new Popup { PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade, Child = host };
    }
}
