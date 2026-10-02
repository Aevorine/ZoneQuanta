using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ZoneQuanta.UI.Theme;

namespace ZoneQuanta.UI.Setup;

public sealed class InstallWindow : Window
{
    public const string FolderName = "ZoneQuanta";

    private readonly TextBox _path = new();
    private readonly TextBlock _final = new();
    private readonly System.Windows.Controls.Primitives.ToggleButton _desktop = new() { Content = "创建桌面快捷方式", IsChecked = true };
    private readonly System.Windows.Controls.Primitives.ToggleButton _autostart = new() { Content = "开机自启", IsChecked = false };
    private readonly TextBlock _error = new();

    public string? InstalledExe { get; private set; }

    public InstallWindow()
    {
        Title = "安装 ZoneQuanta";
        Width = 560;
        Height = 380;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        FontFamily = (FontFamily)Application.Current.FindResource("AppFont");
        SetResourceReference(BackgroundProperty, "SurfaceBrush");

        var title = new TextBlock { Text = "安装位置", FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 14) };

        _path.Style = (Style)FindResource("Field");
        _path.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs");
        _path.TextChanged += (_, _) => UpdateFinal();

        var browse = new Button { Content = "浏览", Style = (Style)FindResource("Action"), Margin = new Thickness(8, 0, 0, 0), MinWidth = 80 };
        browse.Click += (_, _) => Browse();

        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(browse, Dock.Right);
        row.Children.Add(browse);
        row.Children.Add(_path);

        _final.Style = (Style)FindResource("Muted");
        _final.Margin = new Thickness(2, 10, 0, 0);
        _final.TextWrapping = TextWrapping.Wrap;
        _error.Foreground = (Brush)FindResource("Accent2Brush");
        _error.Margin = new Thickness(2, 8, 0, 0);
        _error.TextWrapping = TextWrapping.Wrap;

        StyleCheck(_desktop);
        StyleCheck(_autostart);

        var install = new Button { Content = "安装并运行", Style = (Style)FindResource("Primary"), Height = 40 };
        install.Click += (_, _) => Install();
        var cancel = new Button { Content = "取消", Style = (Style)FindResource("Action"), Height = 40, Margin = new Thickness(0, 0, 8, 0), MinWidth = 90 };
        cancel.Click += (_, _) => DialogResult = false;

        var buttons = new DockPanel { Margin = new Thickness(0, 18, 0, 0) };
        DockPanel.SetDock(cancel, Dock.Left);
        buttons.Children.Add(cancel);
        buttons.Children.Add(install);

        var root = new StackPanel { Margin = new Thickness(26, 24, 26, 20) };
        root.Children.Add(title);
        root.Children.Add(row);
        root.Children.Add(_final);
        root.Children.Add(_error);
        root.Children.Add(new StackPanel { Margin = new Thickness(0, 16, 0, 0), Children = { _desktop, _autostart } });
        root.Children.Add(buttons);
        Content = root;
        UpdateFinal();
    }

    public bool CreateDesktopShortcut => _desktop.IsChecked == true;
    public bool EnableAutostart => _autostart.IsChecked == true;

    public static string TargetDir(string chosen)
    {
        string trimmed = chosen.Trim().TrimEnd('\\', '/');
        return string.Equals(Path.GetFileName(trimmed), FolderName, StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : Path.Combine(trimmed, FolderName);
    }

    private void StyleCheck(System.Windows.Controls.Primitives.ToggleButton box)
    {
        box.Style = (Style)FindResource("SwitchRow");
    }

    private void UpdateFinal()
    {
        _final.Text = _path.Text.Trim().Length == 0 ? string.Empty : "将安装到：" + TargetDir(_path.Text);
    }

    private void Browse()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog { SelectedPath = _path.Text, ShowNewFolderButton = true };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) _path.Text = dialog.SelectedPath;
    }

    private void Install()
    {
        _error.Text = string.Empty;
        try
        {
            string dir = TargetDir(_path.Text);
            Directory.CreateDirectory(dir);
            string self = Environment.ProcessPath ?? throw new InvalidOperationException();
            string target = Path.Combine(dir, "ZoneQuanta.exe");
            if (!string.Equals(Path.GetFullPath(self), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                File.Copy(self, target, true);
            InstalledExe = target;
            DialogResult = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _error.Text = "无法写入该位置，请换一个文件夹";
        }
    }
}
