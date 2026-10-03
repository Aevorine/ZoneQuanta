using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ZoneQuanta.Core.Monitor;
using ZoneQuanta.Core.Settings;
using ZoneQuanta.Core.Traffic;
using ZoneQuanta.Core.Time;
using ZoneQuanta.Core.Update;
using ZoneQuanta.Platform;
using ZoneQuanta.Shell;
using ZoneQuanta.UI.Band;
using ZoneQuanta.UI.Panel;
using ZoneQuanta.UI.Theme;
using ZoneQuanta.UI.Widget;

namespace ZoneQuanta;

public sealed class AppController : IPanelHost, IDisposable
{
    private readonly SettingsStore _store = new();
    private readonly TimeEngine _engine = new();
    private readonly ClockTicker _ticker = new();
    private readonly HotkeyService _hotkey = new();
    private readonly UpdateCoordinator _updates;
    private readonly SystemMetrics _metrics = new();
    private readonly TotalsRecorder _totals = new();
    private BandWindow _band = null!;
    private BandDetailWindow _detail = null!;
    private WidgetWindow _widget = null!;
    private PanelWindow? _panel;
    private TrayService _tray = null!;
    private int _lastMinute = -1;
    private int _topmostTicks;
    private int _trimIn = 20;
    private int _trimTicks;
    private bool _panelWasVisible;

    public AppController()
    {
        _updates = new UpdateCoordinator(_store.Current);
    }

    public AppSettings Settings => _store.Current;
    public TimeEngine Engine => _engine;
    public UpdateCoordinator Updates => _updates;
    public Metrics Latest { get; private set; }
    public TrafficFile Totals => _totals.File;
    public event Action<DateTimeOffset>? Tick;

    public (double X, double Y) WidgetPosition => (
        double.IsNaN(_widget.Left) ? 0 : _widget.Left,
        double.IsNaN(_widget.Top) ? 0 : _widget.Top);

    public void Start(bool showPanel)
    {
        UpdateService.CleanupLeftovers();

        var s = Settings;
        s.AutoStart = AutoStart.IsEnabled();
        ThemeManager.Apply(s.Theme);

        _widget = new WidgetWindow(s, _engine);
        _band = new BandWindow(s);
        _detail = new BandDetailWindow(s, _totals.File, _metrics, () => _band.BoundsPx);
        _band.HoverChanged += _detail.SetHover;
        _tray = new TrayService(s, TogglePanel, ResetWidget, () => _ = _updates.CheckAsync(), ExitApp);

        ShowPendingUpdateFailure();
        _widget.ApplyInitial(DateTimeOffset.UtcNow);
        _metrics.Sample();
        _band.Sync();
        if (s.TrackApps) _ = HelperLauncher.StartIfInstalledAsync();
        s.PropertyChanged += OnSettingChanged;
        _hotkey.Pressed += TogglePanel;
        RegisterHotkey();

        _updates.UpdateFound += info => _tray.Notify("发现新版本", $"ZoneQuanta v{info.Version} 可用，在面板「更新」页安装");
        _updates.PreparingToApply += () => ((App)Application.Current).PrepareExit();

        _ticker.Tick += OnTick;
        _ticker.Hourly += _engine.Refresh;
        _ticker.Start();
        OnTick(DateTimeOffset.UtcNow);

        if (showPanel) ShowPanel();
        if (s.AutoCheckUpdate)
        {
            var delay = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            delay.Tick += async (_, _) =>
            {
                delay.Stop();
                await _updates.CheckAsync();
            };
            delay.Start();
        }
    }

    public void ShowPanel()
    {
        _panel ??= new PanelWindow(this);
        _panel.ShowPanel();
    }

    public void TogglePanel()
    {
        _panel ??= new PanelWindow(this);
        _panel.TogglePanel();
    }

    public void MoveWidget(double x, double y) => _widget.MoveTo(x, y);
    public void AnchorWidget(string where) => _widget.Anchor(where);

    public void ResetWidget()
    {
        _widget.Anchor("TR");
    }

    public async System.Threading.Tasks.Task<bool> SetTrackingAsync(bool on)
    {
        if (on)
        {
            bool ok = await HelperLauncher.EnableAsync();
            Settings.TrackApps = ok;
            return ok;
        }
        await HelperLauncher.DisableAsync();
        Settings.TrackApps = false;
        return true;
    }

    public void OpenSettingsFolder()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZoneQuanta");
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
    }

    private void ShowPendingUpdateFailure()
    {
        string marker = UpdateService.FailureMarker;
        try
        {
            if (!File.Exists(marker)) return;
            string text = File.ReadAllText(marker).Trim();
            File.Delete(marker);
            _tray.Notify("更新失败", text.Length > 0 ? text : "请稍后重试");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void ExitApp()
    {
        ((App)Application.Current).ExitApp();
    }

    public void Dispose()
    {
        _store.Flush();
        _totals.Flush();
        _hotkey.Dispose();
        _tray.Dispose();
        _detail.Close();
        _band.Close();
        _widget.Close();
        _panel?.Close();
    }

    private void OnTick(DateTimeOffset now)
    {
        var s = Settings;

        bool fullscreen = s.HideOnFullscreen && FullscreenWatcher.IsFullscreenActive();
        _widget.SetSuppressed(fullscreen);
        _band.SetSuppressed(fullscreen);

        _widget.Revive();
        _widget.SetDesktopLift(FullscreenWatcher.IsDesktopForeground());
        _band.Revive();
        _panel?.Revive();

        Latest = _metrics.Sample();
        _totals.Add(Latest);
        _band.Update(Latest);
        _detail.Update(Latest);

        if (!fullscreen)
        {
            _widget.Tick(now);
            if (++_topmostTicks >= 3)
            {
                _topmostTicks = 0;
                _widget.ReassertTopmost();
            }
        }
        Tick?.Invoke(now);

        bool panelVisible = _panel?.IsVisible == true;
        if (_panelWasVisible && !panelVisible) _trimIn = 4;
        _panelWasVisible = panelVisible;
        if (++_trimTicks % 600 == 0 && _trimIn == 0) _trimIn = 1;
        if (_trimIn > 0 && --_trimIn == 0 && !panelVisible) MemoryTrim.Run();

        int minute = now.Minute;
        if (minute != _lastMinute)
        {
            _lastMinute = minute;
            _tray.SetTooltip(BuildTooltip(now));
        }
    }

    private string BuildTooltip(DateTimeOffset now)
    {
        var parts = new System.Collections.Generic.List<string>();
        foreach (var z in Settings.Zones)
        {
            var snap = _engine.Compute(z, now);
            parts.Add($"{snap.Label} {snap.Time:HH:mm}");
        }
        return string.Join(" · ", parts);
    }

    private void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppSettings.AutoStart):
                AutoStart.Set(Settings.AutoStart);
                break;
            case nameof(AppSettings.Theme):
                ThemeManager.Apply(Settings.Theme);
                break;
            case nameof(AppSettings.HotkeyEnabled):
            case nameof(AppSettings.HotkeyModifiers):
            case nameof(AppSettings.HotkeyKey):
                RegisterHotkey();
                break;
        }
    }

    private void RegisterHotkey()
    {
        var s = Settings;
        if (!s.HotkeyEnabled)
        {
            _hotkey.Unregister();
            return;
        }
        if (!_hotkey.Register(s.HotkeyModifiers, s.HotkeyKey))
            _tray.Notify("快捷键不可用", "该组合已被其他程序占用，请在「系统」页更换");
    }
}
