using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ZoneQuanta.Core.Settings;
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

    private static readonly (string Key, string Text)[] PlaceDefs =
    {
        ("Last", "上次位置"), ("Center", "屏幕中央"), ("Cursor", "跟随指针"),
    };

    private readonly QuickGrid _grid;

    public QuickPage(IPanelHost host)
    {
        InitializeComponent();
        var s = host.Settings;
        var actions = host.Quick;
        DataContext = s;

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

        foreach (var (key, text) in PlaceDefs)
        {
            var r = new RadioButton { Content = text, GroupName = "quick-place", Margin = new Thickness(0, 0, 6, 0), IsChecked = s.QuickPlacement == key };
            r.SetResourceReference(StyleProperty, "Segment");
            r.Checked += (_, _) => s.QuickPlacement = key;
            Places.Children.Add(r);
        }
        ResetSize.Click += (_, _) => s.QuickBounds = null;
        Loaded += (_, _) => SyncSize(s);

        _grid = new QuickGrid(actions, singleClickOpen: false) { GroupBackground = "SurfaceBrush" };
        GridHost.Content = _grid;
        Search.TextChanged += (_, _) => _grid.Filter = Search.Text;

        GroupMode.Click += (_, _) => s.QuickLayout = "Groups";
        TileMode.Click += (_, _) => s.QuickLayout = "Grid";
        ListMode.Click += (_, _) => s.QuickLayout = "List";
        SyncView(s);
        s.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppSettings.QuickLayout)) SyncView(s);
            if (e.PropertyName == nameof(AppSettings.QuickBounds)) SyncSize(s);
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

    // Physical pixels, as the resize badge on the quick panel shows them; "自动" until the user sizes it.
    private void SyncSize(AppSettings s)
    {
        double k = Math.Max(0.5, System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX);
        var b = s.QuickBounds;
        SizeText.Text = b is null ? "自动" : $"{Math.Round(b.Width * k)} × {Math.Round(b.Height * k)}{(b.Maximized ? " · 最大化" : "")}";
        ResetSize.IsEnabled = b is not null;
    }

    private void SyncView(AppSettings s)
    {
        GroupMode.IsChecked = s.QuickLayout == "Groups";
        TileMode.IsChecked = s.QuickLayout == "Grid";
        ListMode.IsChecked = s.QuickLayout == "List";
    }
}
