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
            ConnectTimeout = TimeSpan.FromSeconds(8),
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
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or CryptographicException or FormatException) { }
            if (info is not null) return info.Version > CurrentVersion ? info : null;
        }
        throw new HttpRequestException("无法连接更新服务器");
    }

    public async Task<string> DownloadAsync(UpdateInfo info, bool useMirrors, IProgress<double> progress, CancellationToken ct)
    {
        string origin = $"https://github.com/{Repo}/releases/download/{info.Tag}/{info.AssetName}";
        string exe = Environment.ProcessPath ?? throw new InvalidOperationException();
        string target = exe + ".new";

        await _downloader.DownloadAsync(Candidates(origin, useMirrors).ToList(), info.Size, target, progress, ct);

        string actual = await ComputeSha256Async(target, ct);
        if (!string.Equals(actual, info.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(target);
            throw new CryptographicException("文件校验失败");
        }
        return target;
    }

    public static void ApplyAndRestart(string newFile)
    {
        string exe = Environment.ProcessPath ?? throw new InvalidOperationException();
        string old = exe + ".old";
        if (File.Exists(old)) File.Delete(old);
        File.Move(exe, old);
        try { File.Move(newFile, exe); }
        catch { File.Move(old, exe); throw; }
        Process.Start(new ProcessStartInfo(exe, "--updated") { UseShellExecute = true });
    }

    public static void CleanupLeftovers()
    {
        string? exe = Environment.ProcessPath;
        if (exe is null) return;
        foreach (string f in new[] { exe + ".old", exe + ".new" })
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
