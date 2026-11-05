using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ZoneQuanta.Core.Settings;
using ZoneQuanta.UI.Controls;
using ZoneQuanta.UI.Quick;
using Win32 = Microsoft.Win32;

namespace ZoneQuanta.UI.Panel.Pages;

public partial class QuickPage : UserControl
{
    private static readonly (string Prop, string Text)[] FieldDefs =
    {
        (nameof(AppSettings.QuickShowIcon), "图标"), (nameof(AppSettings.QuickShowName), "名称"),
        (nameof(AppSettings.QuickShowPath), "路径"), (nameof(AppSettings.QuickShowType), "类型"),
        (nameof(AppSettings.QuickShowSize), "大小"), (nameof(AppSettings.QuickShowModified), "修改时间"),
        (nameof(AppSettings.QuickShowCount), "打开次数"),
    };

    private static readonly (string Key, string Text)[] SortDefs =
    {
        ("Custom", "自定义"), ("Name", "名称"), ("Recent", "最近"), ("Frequent", "最常用"), ("Type", "类型"),
    };

    private readonly QuickGrid _grid;

    public QuickPage(IPanelHost host)
    {
        InitializeComponent();
        var s = host.Settings;
        var actions = host.Quick;
        DataContext = s;

        Features.Children.Add(HotkeyRow("快捷面板", () => (s.QuickHotkeyModifiers, s.QuickHotkeyKey),
            (m, k) => { s.QuickHotkeyModifiers = m; s.QuickHotkeyKey = k; }, nameof(AppSettings.QuickHotkeyEnabled)));
        Features.Children.Add(HotkeyRow("打开路径", () => (s.PathHotkeyModifiers, s.PathHotkeyKey),
            (m, k) => { s.PathHotkeyModifiers = m; s.PathHotkeyKey = k; }, nameof(AppSettings.PathHotkeyEnabled)));

        foreach (var (prop, text) in FieldDefs)
        {
            var chip = new System.Windows.Controls.Primitives.ToggleButton { Content = text };
            chip.SetResourceReference(StyleProperty, "Chip");
            chip.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding(prop) { Mode = BindingMode.TwoWay });
            Fields.Children.Add(chip);
        }

        foreach (var (key, text) in SortDefs)
        {
            var r = new RadioButton { Content = text, GroupName = "quick-sort", Margin = new Thickness(0, 0, 6, 0), IsChecked = s.QuickSort == key };
            r.SetResourceReference(StyleProperty, "Segment");
            r.Checked += (_, _) => s.QuickSort = key;
            Sorts.Children.Add(r);
        }

        _grid = new QuickGrid(actions, singleClickOpen: false);
        GridHost.Content = _grid;
        Search.TextChanged += (_, _) => _grid.Filter = Search.Text;

        TileMode.Click += (_, _) => s.QuickView = "Grid";
        ListMode.Click += (_, _) => s.QuickView = "List";
        SyncView(s);
        s.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppSettings.QuickView)) SyncView(s);
            if (e.PropertyName == nameof(AppSettings.QuickSort))
                foreach (RadioButton r in Sorts.Children) r.IsChecked = SortDefs[Sorts.Children.IndexOf(r)].Key == s.QuickSort;
        };

        AddFiles.Click += (_, _) =>
        {
            var dlg = new Win32.OpenFileDialog { Multiselect = true, DereferenceLinks = false };
            if (dlg.ShowDialog(Window.GetWindow(this)) == true) actions.Add(dlg.FileNames);
        };
        AddFolder.Click += (_, _) =>
        {
            var dlg = new Win32.OpenFolderDialog { Multiselect = true };
            if (dlg.ShowDialog(Window.GetWindow(this)) == true) actions.Add(dlg.FolderNames);
        };
        OpenQuick.Click += (_, _) => host.ToggleQuick();
    }

    private void SyncView(AppSettings s)
    {
        TileMode.IsChecked = s.QuickView != "List";
        ListMode.IsChecked = s.QuickView == "List";
    }

    private static FrameworkElement HotkeyRow(string label, Func<(uint, uint)> get, Action<uint, uint> set, string enabledProp)
    {
        var g = new Grid { MinHeight = 44, Margin = new Thickness(12, 0, 12, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        var box = new HotkeyBox(get, set) { Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center, MinWidth = 90 };
        Grid.SetColumn(box, 1);
        g.Children.Add(box);
        var sw = new CheckBox();
        sw.SetResourceReference(StyleProperty, "Switch");
        sw.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding(enabledProp) { Mode = BindingMode.TwoWay });
        Grid.SetColumn(sw, 2);
        g.Children.Add(sw);
        return g;
    }
}
