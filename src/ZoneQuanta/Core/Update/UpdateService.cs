using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ZoneQuanta.Core.Update;

public sealed record UpdateInfo(Version Version, string Tag, string Notes, string AssetName, long Size, string Sha256);

public sealed class UpdateService
{
    public const string Repo = "Aevorine/ZoneQuanta";
    private static readonly string[] Mirrors = { "https://ghfast.top/", "https://gh-proxy.com/" };

    private readonly HttpClient _http;
    private readonly ParallelDownloader _downloader;

    public static Version CurrentVersion { get; } =
        Assembly.GetExecutingAssembly().GetName().Version is { } v ? new Version(v.Major, v.Minor, Math.Max(v.Build, 0)) : new Version(1, 0, 0);

    public static string Flavor { get; } =
        Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "Flavor")?.Value ?? "lite";

    public UpdateService()
    {
        var handler = new SocketsHttpHandler
        {
            MaxConnectionsPerServer = 16,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            ConnectTimeout = TimeSpan.FromSeconds(15),
            AllowAutoRedirect = true,
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"ZoneQuanta/{CurrentVersion}");
        _downloader = new ParallelDownloader(_http);
    }

    public async Task<UpdateInfo?> CheckAsync(bool useMirrors, CancellationToken ct)
    {
        string origin = $"https://github.com/{Repo}/releases/latest/download/update.json";
        var tasks = Candidates(origin, useMirrors).Select(u => FetchManifestAsync(u, ct)).ToList();

        while (tasks.Count > 0)
        {
            var done = await Task.WhenAny(tasks);
            tasks.Remove(done);
            UpdateInfo? info = null;
            try { info = await done; }
            catch (Exception ex) { Log.Error("manifest", ex); }
            if (info is not null)
            {
                Log.Write($"manifest ok: latest v{info.Version}, current v{CurrentVersion}, flavor {Flavor}");
                return info.Version > CurrentVersion ? info : null;
            }
        }
        throw new HttpRequestException("无法连接更新服务器");
    }

    public async Task<string> DownloadAsync(UpdateInfo info, bool useMirrors, IProgress<double> progress, CancellationToken ct)
    {
        string origin = $"https://github.com/{Repo}/releases/download/{info.Tag}/{info.AssetName}";
        string exe = Environment.ProcessPath ?? throw new InvalidOperationException();
        string target = exe + ".new";

        var urls = Candidates(origin, useMirrors).ToList();

        if (await IsValidAsync(target, info, ct))
        {
            progress.Report(1);
            return target;
        }

        Exception? last = null;
        for (int round = 0; round < 3; round++)
        {
            try
            {
                try { await _downloader.DownloadAsync(urls, info.Size, target, progress, ct); }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    Log.Error("download", ex);
                    await _downloader.DownloadSingleAsync(urls, info.Size, target, progress, ct);
                }

                if (await IsValidAsync(target, info, ct)) return target;
                last = new CryptographicException("文件校验失败");
                TryDelete(target);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                last = ex;
                Log.Error("download-retry", ex);
                await Task.Delay(1500 * (round + 1), ct);
            }
        }
        throw last ?? new HttpRequestException("下载失败");
    }

    private static async Task<bool> IsValidAsync(string path, UpdateInfo info, CancellationToken ct)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length != info.Size) return false;
            return string.Equals(await ComputeSha256Async(path, ct), info.Sha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void Retry(Action action)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { action(); return; }
            catch (Exception ex) when (attempt < 5 && ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(400);
            }
        }
    }

    public static void ApplyAndRestart(string newFile)
    {
        string exe = Environment.ProcessPath ?? throw new InvalidOperationException();
        if (!File.Exists(newFile)) throw new FileNotFoundException("更新文件不存在", newFile);
        string old = exe + ".old";
        try { if (File.Exists(old)) File.Delete(old); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { old = $"{exe}.old{DateTime.Now:HHmmssfff}"; }
        Retry(() => File.Move(exe, old));
        try { Retry(() => File.Move(newFile, exe)); }
        catch { Retry(() => File.Move(old, exe)); throw; }
        Process.Start(new ProcessStartInfo(exe, "--updated") { UseShellExecute = true });
    }

    public static void CleanupLeftovers()
    {
        string? exe = Environment.ProcessPath;
        if (exe is null) return;
        string dir = Path.GetDirectoryName(exe) ?? ".";
        string name = Path.GetFileName(exe);
        var leftovers = new List<string> { exe + ".new" };
        try { leftovers.AddRange(Directory.GetFiles(dir, name + ".old*")); }
        catch (IOException) { }
        foreach (string f in leftovers)
        {
            try { if (File.Exists(f)) File.Delete(f); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static IEnumerable<string> Candidates(string origin, bool useMirrors)
    {
        yield return origin;
        if (!useMirrors) yield break;
        foreach (string m in Mirrors) yield return m + origin;
    }

    private async Task<UpdateInfo?> FetchManifestAsync(string url, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(12));
        string text = await _http.GetStringAsync(url, cts.Token);

        using var envelope = JsonDocument.Parse(text);
        string payload = envelope.RootElement.GetProperty("payload").GetString() ?? string.Empty;
        byte[] sig = Convert.FromBase64String(envelope.RootElement.GetProperty("sig").GetString() ?? string.Empty);

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(UpdateKey.PublicKeyBase64), out _);
        if (!ecdsa.VerifyData(Encoding.UTF8.GetBytes(payload), sig, HashAlgorithmName.SHA256))
            throw new CryptographicException("签名无效");

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        string version = root.GetProperty("version").GetString() ?? "0.0.0";
        string notes = root.TryGetProperty("notes", out var n) ? n.GetString() ?? string.Empty : string.Empty;
        var asset = root.GetProperty("assets").GetProperty(Flavor);

        return new UpdateInfo(
            Version.Parse(version), "v" + version, notes,
            asset.GetProperty("name").GetString() ?? string.Empty,
            asset.GetProperty("size").GetInt64(),
            asset.GetProperty("sha256").GetString() ?? string.Empty);
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan | FileOptions.Asynchronous);
        byte[] hash = await SHA256.HashDataAsync(fs, ct);
        return Convert.ToHexString(hash);
    }
}
