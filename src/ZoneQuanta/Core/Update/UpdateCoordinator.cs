using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using ZoneQuanta.Core.Settings;

namespace ZoneQuanta.Core.Update;

public sealed class UpdateCoordinator : Observable
{
    private readonly UpdateService _service = new();
    private readonly AppSettings _settings;
    private string _status = "尚未检查更新";
    private double _progress;
    private bool _busy;
    private UpdateInfo? _available;

    public UpdateCoordinator(AppSettings settings) => _settings = settings;

    public event Action<UpdateInfo>? UpdateFound;
    public event Action? PreparingToApply;

    public string Status { get => _status; private set => Set(ref _status, value); }
    public double Progress { get => _progress; private set => Set(ref _progress, value); }
    public bool Busy { get => _busy; private set => Set(ref _busy, value); }
    public UpdateInfo? Available { get => _available; private set => Set(ref _available, value); }

    public async Task CheckAsync()
    {
        if (Busy) return;
        Busy = true;
        Progress = 0;
        Status = "正在检查更新…";
        Log.Write("check start");
        try
        {
            var info = await _service.CheckAsync(_settings.UseMirrors, CancellationToken.None);
            Available = info;
            if (info is null)
            {
                Status = $"已是最新版本 v{UpdateService.CurrentVersion}";
            }
            else
            {
                Status = $"发现新版本 v{info.Version}";
                UpdateFound?.Invoke(info);
            }
        }
        catch (Exception ex)
        {
            Log.Error("check", ex);
            Status = "无法连接更新服务器";
        }
        finally
        {
            Busy = false;
        }

        if (Available is not null && _settings.AutoInstallUpdate) await InstallAsync();
    }

    public async Task InstallAsync()
    {
        if (Busy || Available is null) return;
        Busy = true;
        Progress = 0;
        Status = "正在下载…";

        string file;
        try
        {
            var progress = new Progress<double>(p =>
            {
                Progress = p;
                Status = $"正在下载 {p:P0}";
            });
            file = await _service.DownloadAsync(Available, _settings.UseMirrors, progress, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Log.Error("install-download", ex);
            Status = ex is CryptographicException ? "文件校验失败，已取消" : $"下载失败：{Brief(ex)}，请稍后重试";
            Busy = false;
            return;
        }

        Status = "正在安装…";
        Traffic.HelperLauncher.RequestStop();

        // Tear everything down while the executable is still intact: once it is replaced, the
        // single-file runtime can no longer load assemblies that were not touched yet, so the
        // old process must do nothing but start the new one and exit.
        try { PreparingToApply?.Invoke(); }
        catch (Exception ex) { Log.Error("install-prepare", ex); }

        try
        {
            UpdateService.ApplyAndRestart(file);
        }
        catch (Exception ex)
        {
            Log.Error("install-apply", ex);
            try { System.IO.File.WriteAllText(UpdateService.FailureMarker, $"安装失败：{Brief(ex)}"); }
            catch (Exception) { }
            UpdateService.RelaunchCurrent();
        }
        Environment.Exit(0);
    }

    private static string Brief(Exception ex)
    {
        Exception e = ex;
        while (e.InnerException is not null) e = e.InnerException;
        string text = e.Message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length > 48 ? text[..48] + "…" : text;
    }
}
