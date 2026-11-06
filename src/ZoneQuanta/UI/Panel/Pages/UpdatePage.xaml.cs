using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using ZoneQuanta.Core.Update;

namespace ZoneQuanta.UI.Panel.Pages;

public partial class UpdatePage : UserControl
{
    private readonly UpdateCoordinator _updates;

    public UpdatePage(IPanelHost host)
    {
        InitializeComponent();
        DataContext = host.Settings;
        _updates = host.Updates;

        VersionText.Text = $"v{UpdateService.CurrentVersion}";
        FlavorText.Text = "内置运行时与全部依赖";

        CheckButton.Click += async (_, _) => await _updates.CheckAsync();
        LinkButton.Click += (_, _) => OpenRelease();
        CopyLinkButton.Click += (_, _) => Copy(LinkButton.Content as string ?? "");
        CopyHashButton.Click += (_, _) => { if (_updates.Latest is { } l) Copy(l.Sha256); };
        InstallButton.Click += async (_, _) => await _updates.InstallAsync();

        _updates.PropertyChanged += OnChanged;
        Unloaded += (_, _) => _updates.PropertyChanged -= OnChanged;
        Loaded += (_, _) =>
        {
            _updates.PropertyChanged -= OnChanged;
            _updates.PropertyChanged += OnChanged;
            Sync();
        };
        Sync();
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e) => Dispatcher.Invoke(Sync);

    // Opens exactly the address shown, and only when it is an official release page.
    private void OpenRelease()
    {
        string url = LinkButton.Content as string ?? "";
        if (!ReleaseLinks.IsOfficial(url))
        {
            Core.Log.Write($"release link refused: {url}");
            return;
        }
        Core.Log.Write($"release link open: {url}");
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception ex) { Core.Log.Error("release-link", ex); }
    }

    private static void Copy(string text)
    {
        if (text.Length == 0) return;
        try { Clipboard.SetText(text); }
        catch (System.Runtime.InteropServices.COMException ex) { Core.Log.Error("copy", ex); }
    }

    private void Sync()
    {
        var latest = _updates.Latest;
        string link = ReleaseLinks.PageOf(latest);
        LinkButton.Content = ReleaseLinks.IsOfficial(link) ? link : ReleaseLinks.Latest;
        // Read out as the address itself, like any link.
        System.Windows.Automation.AutomationProperties.SetName(LinkButton, (string)LinkButton.Content);
        LinkButton.ToolTip = latest is null ? "最新版本" : $"v{latest.Version}";
        bool hash = latest is not null && System.Text.RegularExpressions.Regex.IsMatch(latest.Sha256, "^[0-9A-Fa-f]{64}$");
        HashRow.Visibility = hash ? Visibility.Visible : Visibility.Collapsed;
        HashText.Text = hash ? $"SHA-256  {latest!.Sha256.ToUpperInvariant()}" : "";
        HashText.ToolTip = hash ? $"{latest!.AssetName}\n{latest.Sha256.ToUpperInvariant()}" : null;
        StatusText.Text = _updates.Status;
        Bar.Value = _updates.Progress;
        Bar.Visibility = _updates.Busy ? Visibility.Visible : Visibility.Hidden;
        CheckButton.IsEnabled = !_updates.Busy;
        InstallButton.IsEnabled = !_updates.Busy && _updates.Available is not null;
        InstallButton.Visibility = _updates.Available is not null ? Visibility.Visible : Visibility.Hidden;
    }
}
