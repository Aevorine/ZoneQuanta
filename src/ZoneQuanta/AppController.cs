using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ZoneQuanta.Core;
using ZoneQuanta.Core.Monitor;
using ZoneQuanta.Core.Quick;
using ZoneQuanta.Core.Settings;
using ZoneQuanta.Core.Traffic;
using ZoneQuanta.Core.Time;
using ZoneQuanta.Core.Update;
using ZoneQuanta.Platform;
using ZoneQuanta.Shell;
using ZoneQuanta.UI.Band;
using ZoneQuanta.UI.Panel;
using ZoneQuanta.UI.Quick;
using ZoneQuanta.UI.Theme;
using ZoneQuanta.UI.Widget;

namespace ZoneQuanta;

public sealed class AppController : IPanelHost, IDisposable
{
    private readonly SettingsStore _store = new();
    private readonly TimeEngine _engine = new();
    private readonly ClockTicker _ticker = new();
    private readonly HotkeyService _hotkey = new();
    private readonly ForegroundWatcher _foreground = new();
    private readonly UpdateCoordinator _updates;
    private readonly SystemMetrics _metrics = new();
    private readonly MetricsSampler _sampler;
    private readonly TotalsRecorder _totals = new();
    private readonly QuickStore _quickStore = new();
    private readonly CommandPipe _pipe = new();
    private QuickActions _quick = null!;
    private QuickWindow? _quickWindow;
    private PathBarWindow? _pathBar;
    private StartPinSync? _startSync;
    private bool _recordingKeys;
    private readonly object _gate = new();
    private Dispatcher _dispatcher = null!;
    private Metrics _pending;
    private bool _hasPending, _posted, _envQueued, _fullscreen;
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
    // A version is kept 20 s after the last saved change, so a burst of edits becomes one entry.
    private readonly DispatcherTimer _historyTimer = new() { Interval = TimeSpan.FromSeconds(20) };

    public AppController()
    {
        _updates = new UpdateCoordinator(_store.Current);
        _sampler = new MetricsSampler(_metrics, _store.Current.SampleIntervalMs);
    }

    public AppSettings Settings => _store.Current;
    public TimeEngine Engine => _engine;
    public UpdateCoordinator Updates => _updates;
    public Metrics Latest { get; private set; }
    public TrafficFile Totals => _totals.File;
    public QuickActions Quick => _quick;
    public event Action<DateTimeOffset>? Tick;

    public (double X, double Y) WidgetPosition => (
        double.IsNaN(_widget.Left) ? 0 : _widget.Left,
        double.IsNaN(_widget.Top) ? 0 : _widget.Top);

    public void Start(bool showPanel, bool updated = false)
    {
        bool helperFirst = updated && Settings.TrackApps;
        if (!helperFirst) UpdateService.CleanupLeftovers();

        _dispatcher = Application.Current.Dispatcher;
        var s = Settings;
        if (!DataPaths.Sandboxed) s.AutoStart = AutoStart.IsEnabled();
        ThemeManager.Apply(s.Theme);

        _widget = new WidgetWindow(s, _engine);
        _band = new BandWindow(s);
        _detail = new BandDetailWindow(s, _totals.File, _metrics, () => _band.BoundsPx);
        _band.HoverChanged += _detail.SetHover;
        _band.PaintingChanged += _ => ApplySampleRate();
        _band.SettingsRequested += page =>
        {
            _panel ??= new PanelWindow(this);
            _panel.SelectPage(page);
            _panel.ShowPanel();
        };
        _quick = new QuickActions(_quickStore, s);
        _tray = new TrayService(s, TogglePanel, ResetWidget, () => _ = _updates.CheckAsync(), ExitApp, ToggleQuick, ShowPathBar,
            () => _quick.Sections().SelectMany(sec => sec.Items.Take(sec.Group is null ? 30 : int.MaxValue)
                .Select(i => (sec.Group?.Name ?? "", i.DisplayName, (Action)(() => _quick.Open(i))))));

        ShowPendingUpdateFailure();
        _widget.ApplyInitial(CurrentSecond());
        _band.Sync();
        if (s.TrackApps && !DataPaths.Sandboxed) _ = StartTrackingHelperAsync(helperFirst);
        s.PropertyChanged += OnSettingChanged;
        _hotkey.Pressed += OnHotkey;
        UI.Controls.HotkeyBox.Recording += on =>
        {
            _recordingKeys = on;
            foreach (int id in new[] { HotkeyService.PanelId, HotkeyService.QuickId, HotkeyService.PathId })
                if (on) _hotkey.Unregister(id);
                else RegisterHotkey(id);
        };
        RegisterHotkey(HotkeyService.PanelId);
        RegisterHotkey(HotkeyService.QuickId);
        RegisterHotkey(HotkeyService.PathId);
        ApplyExplorerMenu();
        _startSync = new StartPinSync(_quick, _dispatcher);
        _startSync.Imported += (names, bulk) => PrewarmIcons(names.Count);
        // A successful import is announced by Imported; the explicit command also answers when nothing came.
        _startSync.ImportDone += n =>
        {
            if (n < 0) _tray.Notify("无法读取「开始」布局", "详情见 app.log");
            else if (n == 0) _tray.Notify("没有可导入的应用", "「开始」固定的应用都已在快捷面板中");
        };
        _startSync.Imported += (names, bulk) => _tray.Notify(
            bulk ? $"已从「开始」导入 {names.Count} 个应用" : "已加入快捷面板",
            bulk ? $"按 {UI.Panel.Pages.SystemPage.Format(s.QuickHotkeyModifiers, s.QuickHotkeyKey)} 打开快捷面板" : string.Join("、", names.Take(6)));
        _startSync.Apply(s.QuickStartSync);
        _quick.ScheduleAutoGroup();
        _pipe.Received += args => _dispatcher.BeginInvoke(new Action(() => Execute(args)));
        _pipe.Start();

        _updates.UpdateFound += info => _tray.Notify("发现新版本", $"ZoneQuanta v{info.Version} 可用，在面板「更新」页安装");
        _updates.PreparingToApply += () =>
        {
            _store.Flush();
            DataHistory.Take("更新前");
            ((App)Application.Current).PrepareExit();
        };

        _historyTimer.Tick += (_, _) =>
        {
            _historyTimer.Stop();
            _ = Task.Run(() => DataHistory.Take("变更"));
        };
        _store.Saved += ArmHistory;
        _quickStore.Saved += ArmHistory;
        _ = Task.Run(() => DataHistory.Take("启动"));

        _ticker.Tick += OnTick;
        _ticker.Hourly += _engine.Refresh;
        _ticker.Start();
        _sampler.Sampled += OnSampled;
        _sampler.Start();
        _foreground.Changed += QueueEnvironment;
        _foreground.Start();
        OnTick(CurrentSecond());

        if (showPanel) ShowPanel();
        _dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(Prewarm));
        if (s.AutoCheckUpdate && !DataPaths.Sandboxed)
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

    // Window creation is the slow part of the first hover and the first panel open: do it while idle.
    private void Prewarm()
    {
        _detail.Prewarm();
        if (_panel is null)
        {
            _panel = new PanelWindow(this);
            _trimIn = 6;
        }
        QuickWindow().Prewarm();
        PathBar().Prewarm();
        int px = (int)Math.Round(44 * Math.Max(1, System.Windows.Media.VisualTreeHelper.GetDpi(_widget).DpiScaleX));
        foreach (var item in _quickStore.Items) ShellIcons.Request(item.Path, px, _ => { });
    }

    // Icons of freshly imported items are extracted in the background so the first open is instant.
    private void PrewarmIcons(int newest)
    {
        int px = (int)Math.Round(44 * Math.Max(1, System.Windows.Media.VisualTreeHelper.GetDpi(_widget).DpiScaleX));
        foreach (var item in _quickStore.Items.Skip(Math.Max(0, _quickStore.Items.Count - newest))) ShellIcons.Request(item.Path, px, _ => { });
    }

    private QuickWindow QuickWindow() => _quickWindow ??= new QuickWindow(_quick, () =>
    {
        ShowPanel();
        _panel!.SelectPage("快捷");
    })
    { ImportStart = () => _ = _startSync?.RunAsync(all: true) };

    private PathBarWindow PathBar() => _pathBar ??= new PathBarWindow(_quick);

    public void ToggleQuick() => QuickWindow().Toggle();

    public void ShowPathBar() => PathBar().Toggle();

    // Commands from a second launch: Explorer verb, a script or Claude Code.
    public void Execute(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--quick-add":
                    var paths = new List<string>();
                    while (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)) paths.Add(FixQuotedRoot(args[++i]));
                    _quick.Add(paths);
                    QuickWindow().Present();
                    break;
                case "--quick":
                    QuickWindow().Present();
                    break;
                case "--path-bar":
                    PathBar().Present();
                    break;
                case "--open":
                    if (i + 1 < args.Length) _quick.OpenText(args[++i]);
                    break;
                case "--exit":
                    ExitApp();
                    return;
                case "--panel":
                    ShowPanel();
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)) _panel!.SelectPage(args[++i]);
                    break;
            }
        }
    }

    // "%1" of a drive root arrives as C:\" and argv parsing turns that into C:" .
    private static string FixQuotedRoot(string arg) => arg.EndsWith('"') ? arg[..^1] + "\\" : arg;

    private void OnHotkey(int id)
    {
        switch (id)
        {
            case HotkeyService.PanelId: TogglePanel(); break;
            case HotkeyService.QuickId: ToggleQuick(); break;
            case HotkeyService.PathId: ShowPathBar(); break;
        }
    }

    private void ApplyExplorerMenu()
    {
        string? exe = Environment.ProcessPath;
        if (exe is null || DataPaths.Sandboxed) return;
        if (!Settings.QuickContextMenu) ExplorerMenu.Set(false, exe);
        else if (!ExplorerMenu.IsRegistered(exe)) ExplorerMenu.Set(true, exe);
    }

    // A default combination already owned by another program is replaced by the first free one.
    private static readonly uint[] FallbackKeys = { 0x20, 0x47, 0x4B, 0x4A, 0x45, 0x4F, 0x4C, 0x56 };

    private bool TryFallbackHotkey(int id, uint mods, uint key)
    {
        var s = Settings;
        bool isDefault = mods == 3 && (id == HotkeyService.QuickId ? key == 0x20 : id == HotkeyService.PathId && key == 0x47);
        if (!isDefault) return false;
        uint other = id == HotkeyService.QuickId ? s.PathHotkeyKey : s.QuickHotkeyKey;
        foreach (uint vk in FallbackKeys)
        {
            if (vk == key || vk == other || (s.HotkeyModifiers == 3 && vk == s.HotkeyKey)) continue;
            if (!_hotkey.Register(id, 3, vk)) continue;
            if (id == HotkeyService.QuickId) s.QuickHotkeyKey = vk;
            else s.PathHotkeyKey = vk;
            Log.Write($"hotkey fallback {id:X} -> {vk:X}");
            return true;
        }
        return false;
    }

    private void CheckQuota()
    {
        var s = Settings;
        if (!s.QuotaEnabled || !s.QuotaAlert) return;
        long cap = (long)(s.QuotaGB * 1024 * 1024 * 1024);
        var r = Quota.Compute(_totals.File, cap, s.QuotaResetDay, DateTime.Now);
        int level = r.Fraction >= 1 ? 100 : r.Fraction * 100 >= s.QuotaAlertPercent ? s.QuotaAlertPercent : 0;
        if (level == 0) return;
        string cycle = r.Start.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
        string key = $"{cycle}-{level}";
        if (s.QuotaAlerted == key || (level < 100 && s.QuotaAlerted == $"{cycle}-100")) return;
        s.QuotaAlerted = key;
        string used = UnitFormat.Size(r.Used, false, "Auto"), all = UnitFormat.Size(r.Cap, false, "Auto");
        _tray.Notify(level >= 100 ? "流量额度已用完" : $"流量已用 {r.Fraction:P0}", $"已用 {used} / {all}，账期还剩 {r.DaysLeft:0.#} 天");
        Log.Write($"quota alert {level}");
    }

    private async Task StartTrackingHelperAsync(bool afterUpdate)
    {
        if (afterUpdate)
        {
            await HelperLauncher.StopRunningAsync();
            UpdateService.CleanupLeftovers();
        }
        if (await HelperLauncher.StartIfInstalledAsync() != HelperState.Missing) return;
        // The scheduled task is gone (removed by hand or by a cleanup tool): say so instead of
        // leaving the switch on while nothing is being counted.
        Settings.TrackApps = false;
        _tray.Notify("应用流量统计已关闭", "后台助手已不存在，可在「流量」页重新开启");
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
        if (DataPaths.Sandboxed) return false;
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
        string dir = DataPaths.Root;
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

    private void ArmHistory() => Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
    {
        _historyTimer.Stop();
        _historyTimer.Start();
    }));

    // The current state is kept as a version first, so going back is itself undoable.
    public void RestoreSnapshot(Snapshot snapshot)
    {
        _store.Flush();
        DataHistory.Take("恢复前");
        ((App)Application.Current).Restart(() => DataHistory.Restore(snapshot));
    }

    public void Dispose()
    {
        _ticker.Dispose();
        _sampler.Dispose();
        _foreground.Dispose();
        _totals.Flush();
        _hotkey.Dispose();
        _pipe.Dispose();
        _startSync?.Dispose();
        _tray.Dispose();
        _quickWindow?.Close();
        _pathBar?.Close();
        _detail.Close();
        _band.Close();
        _widget.Close();
        _panel?.Close();
        // Last: closing the windows commits where they were left.
        _store.Flush();
        if (_historyTimer.IsEnabled)
        {
            _historyTimer.Stop();
            DataHistory.Take("退出");
        }
    }

    // Fast sampling only pays off while something on screen shows the numbers. Otherwise a one-second
    // pass still keeps the daily totals exact, at a fraction of the wake-ups.
    private void ApplySampleRate()
    {
        int wanted = Settings.SampleIntervalMs;
        if (!_band.IsPainting && _panel?.IsVisible != true) wanted = Math.Max(wanted, 1000);
        if (_sampler.IntervalMs != wanted) _sampler.IntervalMs = wanted;
    }

    private static DateTimeOffset CurrentSecond()
    {
        long ticks = Native.PreciseUtcTicks();
        return new DateTimeOffset(ticks - ticks % TimeSpan.TicksPerSecond, TimeSpan.Zero);
    }

    // Runs on the UI thread at the top of every second. The clock is drawn first: nothing after it
    // (environment checks, panel refresh, memory trim) may delay the new second.
    private void OnTick(DateTimeOffset now)
    {
        if (!_fullscreen) _widget.Tick(now);
        Tick?.Invoke(now);

        EvaluateEnvironment();
        _widget.Revive();
        _band.Revive();
        _panel?.Revive();
        if (!_fullscreen && ++_topmostTicks >= 3)
        {
            _topmostTicks = 0;
            _widget.ReassertTopmost();
        }

        bool panelVisible = _panel?.IsVisible == true;
        if (_panelWasVisible && !panelVisible) _trimIn = 4;
        if (panelVisible != _panelWasVisible) ApplySampleRate();
        _panelWasVisible = panelVisible;
        if (++_trimTicks % 600 == 0 && _trimIn == 0) _trimIn = 1;
        if (_trimIn > 0 && --_trimIn == 0 && !panelVisible) MemoryTrim.Run();

        int minute = now.Minute;
        if (minute != _lastMinute)
        {
            _lastMinute = minute;
            _tray.SetTooltip(BuildTooltip(now));
            CheckQuota();
        }
    }

    // Fullscreen hiding and the desktop lift. Besides the per-second pass this also runs the moment the
    // foreground window changes or resizes, so the widget gets out of a game's way (and back) at once.
    private void EvaluateEnvironment()
    {
        _envQueued = false;
        bool fullscreen = Settings.HideOnFullscreen && FullscreenWatcher.IsFullscreenActive();
        if (fullscreen != _fullscreen)
        {
            _fullscreen = fullscreen;
            if (!fullscreen) _widget.Refresh(CurrentSecond()); // reappear on the current second, not the one it hid at
            _widget.SetSuppressed(fullscreen);
            _band.SetSuppressed(fullscreen);
        }
        _widget.SetDesktopLift(FullscreenWatcher.IsDesktopForeground());
        // Win+D / Win+M change the foreground window: bring back anything the shell just iconified or hid.
        _widget.Revive();
        _band.Revive();
    }

    private void QueueEnvironment()
    {
        if (_envQueued) return;
        _envQueued = true;
        _dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(EvaluateEnvironment));
    }

    // Sampler thread. Rates are replaced by the newest sample, while byte counts are summed so that a
    // busy UI thread coalescing two samples never loses traffic from the daily total.
    private void OnSampled(Metrics m)
    {
        lock (_gate)
        {
            _pending = _hasPending ? m with { UpBytes = m.UpBytes + _pending.UpBytes, DownBytes = m.DownBytes + _pending.DownBytes } : m;
            _hasPending = true;
            if (_posted) return;
            _posted = true;
        }
        _dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(ApplyMetrics));
    }

    private void ApplyMetrics()
    {
        Metrics m;
        lock (_gate)
        {
            m = _pending;
            _hasPending = false;
            _posted = false;
        }
        Latest = m;
        _totals.Add(m);
        _band.Update(m, _totals.File.TodayTotal());
        _detail.Update(m);
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
                if (!DataPaths.Sandboxed) AutoStart.Set(Settings.AutoStart);
                break;
            case nameof(AppSettings.Theme):
                ThemeManager.Apply(Settings.Theme);
                break;
            case nameof(AppSettings.HideOnFullscreen):
                EvaluateEnvironment();
                break;
            case nameof(AppSettings.SampleIntervalMs):
                ApplySampleRate();
                break;
            case nameof(AppSettings.HotkeyEnabled):
            case nameof(AppSettings.HotkeyModifiers):
            case nameof(AppSettings.HotkeyKey):
                RegisterHotkey(HotkeyService.PanelId);
                break;
            case nameof(AppSettings.QuickHotkeyEnabled):
            case nameof(AppSettings.QuickHotkeyModifiers):
            case nameof(AppSettings.QuickHotkeyKey):
                RegisterHotkey(HotkeyService.QuickId);
                break;
            case nameof(AppSettings.PathHotkeyEnabled):
            case nameof(AppSettings.PathHotkeyModifiers):
            case nameof(AppSettings.PathHotkeyKey):
                RegisterHotkey(HotkeyService.PathId);
                break;
            case nameof(AppSettings.QuickContextMenu):
                ApplyExplorerMenu();
                break;
            case nameof(AppSettings.QuickStartSync):
                _startSync?.Apply(Settings.QuickStartSync);
                break;
            case nameof(AppSettings.QuotaEnabled):
            case nameof(AppSettings.QuotaGB):
            case nameof(AppSettings.QuotaResetDay):
            case nameof(AppSettings.QuotaAlertPercent):
                CheckQuota();
                break;
        }
    }

    private void RegisterHotkey(int id)
    {
        var s = Settings;
        if (_recordingKeys || DataPaths.Sandboxed) return;
        var (on, mods, key, page) = id switch
        {
            HotkeyService.QuickId => (s.QuickHotkeyEnabled, s.QuickHotkeyModifiers, s.QuickHotkeyKey, "快捷键"),
            HotkeyService.PathId => (s.PathHotkeyEnabled, s.PathHotkeyModifiers, s.PathHotkeyKey, "快捷键"),
            _ => (s.HotkeyEnabled, s.HotkeyModifiers, s.HotkeyKey, "快捷键"),
        };
        if (!on)
        {
            _hotkey.Unregister(id);
            return;
        }
        if (!_hotkey.Register(id, mods, key) && !TryFallbackHotkey(id, mods, key))
        {
            _tray.Notify("快捷键不可用", $"{UI.Panel.Pages.SystemPage.Format(mods, key)} 已被其他程序占用，请在「{page}」页更换");
            Log.Write($"hotkey busy {id:X}");
        }
    }
}
