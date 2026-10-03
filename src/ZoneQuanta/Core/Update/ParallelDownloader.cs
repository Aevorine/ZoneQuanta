using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace ZoneQuanta.Core.Update;

internal sealed class ParallelDownloader
{
    private const int MaxSegments = 8;
    private const int MinSegmentBytes = 512 * 1024;
    private readonly HttpClient _http;

    public ParallelDownloader(HttpClient http) => _http = http;

    public async Task DownloadAsync(IReadOnlyList<string> urls, long size, string dest, IProgress<double> progress, CancellationToken ct)
    {
        string best = await PickFastestAsync(urls, ct);
        var ordered = new List<string> { best };
        ordered.AddRange(urls.Where(u => u != best));

        int segments = (int)Math.Clamp(size / MinSegmentBytes, 1, MaxSegments);
        long chunk = (size + segments - 1) / segments;

        using (var create = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None)) create.SetLength(size);
        using var handle = File.OpenHandle(dest, FileMode.Open, FileAccess.Write, FileShare.Write, FileOptions.Asynchronous);

        long received = 0;
        var tasks = Enumerable.Range(0, segments).Select(i =>
        {
            long from = i * chunk;
            long to = Math.Min(size - 1, from + chunk - 1);
            return DownloadSegmentAsync(ordered, handle, from, to, n =>
            {
                long total = Interlocked.Add(ref received, n);
                progress.Report(Math.Min(1.0, (double)total / size));
            }, ct);
        });
        await Task.WhenAll(tasks);
    }

    public async Task DownloadSingleAsync(IReadOnlyList<string> urls, long size, string dest, IProgress<double> progress, CancellationToken ct)
    {
        Exception? last = null;
        foreach (string url in urls)
        {
            try
            {
                using var res = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                res.EnsureSuccessStatusCode();
                await using var src = await res.Content.ReadAsStreamAsync(ct);
                await using var dst = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, true);
                byte[] buffer = new byte[1 << 16];
                long total = 0;
                int read;
                while ((read = await src.ReadAsync(buffer, ct)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                    total += read;
                    progress.Report(Math.Min(1.0, (double)total / size));
                }
                if (total == size) return;
                last = new IOException($"下载不完整 {total}/{size}");
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                last = ex;
            }
        }
        throw new HttpRequestException("所有线路均下载失败", last);
    }

    private async Task<string> PickFastestAsync(IReadOnlyList<string> urls, CancellationToken ct)
    {
        if (urls.Count == 1) return urls[0];
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var probes = urls.Select(u => ProbeAsync(u, cts.Token)).ToList();
        while (probes.Count > 0)
        {
            var done = await Task.WhenAny(probes);
            probes.Remove(done);
            string? ok = await done;
            if (ok is not null)
            {
                cts.Cancel();
                return ok;
            }
        }
        return urls[0];
    }

    private async Task<string?> ProbeAsync(string url, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Range = new RangeHeaderValue(0, 0);
            using var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            return res.StatusCode is HttpStatusCode.PartialContent or HttpStatusCode.OK ? url : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return null; }
    }

    private async Task DownloadSegmentAsync(IReadOnlyList<string> urls, SafeFileHandle handle, long from, long to, Action<int> onBytes, CancellationToken ct)
    {
        long position = from;
        Exception? last = null;
        for (int attempt = 0; attempt < urls.Count * 2 && position <= to; attempt++)
        {
            string url = urls[attempt % urls.Count];
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Range = new RangeHeaderValue(position, to);
                using var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                if (res.StatusCode != HttpStatusCode.PartialContent) throw new HttpRequestException("不支持分段下载");

                await using var stream = await res.Content.ReadAsStreamAsync(ct);
                byte[] buffer = new byte[1 << 16];
                int read;
                while ((read = await stream.ReadAsync(buffer, ct)) > 0)
                {
                    await RandomAccess.WriteAsync(handle, buffer.AsMemory(0, read), position, ct);
                    position += read;
                    onBytes(read);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                last = ex;
            }
        }
        if (position <= to) throw new HttpRequestException("下载中断", last);
    }
}
