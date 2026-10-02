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
    public event Action? ReadyToExit;

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
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
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
        try
        {
            var progress = new Progress<double>(p =>
            {
                Progress = p;
                Status = $"正在下载 {p:P0}";
            });
            string file = await _service.DownloadAsync(Available, _settings.UseMirrors, progress, CancellationToken.None);
            Status = "正在安装…";
            UpdateService.ApplyAndRestart(file);
            Status = "更新完成，正在重启";
            ReadyToExit?.Invoke();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or CryptographicException or System.IO.IOException or UnauthorizedAccessException)
        {
            Status = ex is CryptographicException ? "文件校验失败，已取消" : "更新失败，请稍后重试";
        }
        finally
        {
            Busy = false;
        }
    }
}
