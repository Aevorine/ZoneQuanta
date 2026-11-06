using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZoneQuanta.Core;

namespace ZoneQuanta.Platform;

// Brand icons for programs that carry none, from the product's own website (the icons it declares in its web
// manifest and <link rel=icon> tags, then /apple-touch-icon.png and /favicon.ico) or its GitHub organisation's
// avatar when the site offers nothing sharp. Only https and small responses; every image is decoded and saved
// again as PNG under icons\ before anything shows it. A source that gave nothing is asked again a day later;
// one that could not be reached (offline, timed out) after a quarter of an hour. Addresses on the local network
// are never asked, not even through a redirect. Background threads only.
internal static class WebIcons
{
    public static volatile bool Enabled = true;

    private const int MaxHtml = 768 * 1024, MaxManifest = 64 * 1024, MaxImage = 1024 * 1024, KeepPx = 256, MaxTries = 6;
    private static readonly TimeSpan RetryAfter = TimeSpan.FromDays(1), RetryOffline = TimeSpan.FromMinutes(15), PerSource = TimeSpan.FromSeconds(25);
    private static readonly object Gate = new(), FetchGate = new();
    private static Dictionary<string, DateTime>? _failed;
    private static readonly Dictionary<string, DateTime> Unreached = new(StringComparer.OrdinalIgnoreCase);

    // Whether any request of one source failed to reach its server.
    private sealed class Attempt { public bool Unreachable; }
    private static readonly HttpClient Http = CreateClient();

    private static string Dir => DataPaths.File("icons");
    private static string FailedFile => Path.Combine(Dir, "failed.tsv");

    public static string? Cached(AppBrands.Web web)
    {
        string f = FileOf(web.Key);
        return File.Exists(f) ? f : null;
    }

    public static bool Due(AppBrands.Web web)
    {
        lock (Gate)
            return (!Failed().TryGetValue(web.Key, out var at) || DateTime.UtcNow - at > RetryAfter)
                   && (!Unreached.TryGetValue(web.Key, out var off) || DateTime.UtcNow - off > RetryOffline);
    }

    // Fetches the sources that are not cached and not recently tried, four at a time; the keys now cached.
    // Returns at once when another fetch is under way.
    public static HashSet<string> Fetch(IEnumerable<AppBrands.Web> sources)
    {
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var todo = sources.DistinctBy(s => s.Key, StringComparer.OrdinalIgnoreCase).Where(s => Cached(s) is null && Due(s)).Take(16).ToList();
        if (todo.Count == 0 || !Monitor.TryEnter(FetchGate)) return done;
        try
        {
            Directory.CreateDirectory(Dir);
            Parallel.ForEach(todo, new ParallelOptions { MaxDegreeOfParallelism = 4 }, web =>
            {
                var sw = Stopwatch.StartNew();
                using var cts = new CancellationTokenSource(PerSource);
                var attempt = new Attempt();
                string how;
                bool ok;
                // Whatever goes wrong with one source stays with it: recorded as a failure, never thrown.
                try { ok = FetchOne(web, attempt, cts.Token, out how); }
                catch (Exception ex)
                {
                    ok = false;
                    how = ex.GetType().Name;
                    if (ex is OperationCanceledException or HttpRequestException) attempt.Unreachable = true;
                }
                bool offline = !ok && (attempt.Unreachable || cts.IsCancellationRequested);
                Log.Write($"web-icon {web.Key} -> {how}{(offline ? " (unreachable, retry in 15 min)" : "")} {sw.ElapsedMilliseconds}ms");
                lock (Gate)
                {
                    if (ok)
                    {
                        done.Add(web.Key);
                        Failed().Remove(web.Key);
                        Unreached.Remove(web.Key);
                    }
                    else if (offline) Unreached[web.Key] = DateTime.UtcNow;
                    else Failed()[web.Key] = DateTime.UtcNow;
                }
            });
            lock (Gate) SaveFailed();
        }
        finally { Monitor.Exit(FetchGate); }
        return done;
    }

    private static bool FetchOne(AppBrands.Web web, Attempt attempt, CancellationToken ct, out string how)
    {
        (byte[] Pix, int W, int H, string From)? best = null;
        int Side((byte[] Pix, int W, int H, string From)? b) => b is { } v ? Math.Min(v.W, v.H) : 0;
        if (web.Domain is { } domain)
        {
            var home = new Uri("https://" + domain + "/");
            var candidates = new List<(Uri Uri, int Size)>();
            if (GetText(home, MaxHtml, attempt, ct) is { } page)
            {
                home = page.Final;
                candidates.AddRange(Declared(page.Text, page.Final, attempt, ct));
            }
            candidates.Add((new Uri(home, "/apple-touch-icon.png"), 180));
            candidates.Add((new Uri(home, "/favicon.ico"), 32));
            int tries = 0;
            foreach (var c in candidates.DistinctBy(c => c.Uri.AbsoluteUri).OrderByDescending(c => Score(c.Size)))
            {
                if (++tries > MaxTries || ct.IsCancellationRequested) break;
                if (GetBytes(c.Uri, MaxImage, attempt, ct) is not { } data || Usable(data) is not { } img) continue;
                if (Math.Min(img.W, img.H) > Side(best)) best = (img.Pix, img.W, img.H, c.Uri.Host + c.Uri.AbsolutePath);
                if (Side(best) >= 96) break;
            }
        }
        // A blurry favicon only: the organisation's avatar is sharper.
        if (Side(best) < 48 && web.GitHub is { } owner && !ct.IsCancellationRequested
            && GetBytes(new Uri($"https://github.com/{owner}.png?size={KeepPx}"), MaxImage, attempt, ct) is { } avatar && Usable(avatar) is { } a
            && Math.Min(a.W, a.H) > Side(best))
            best = (a.Pix, a.W, a.H, "github avatar " + owner);
        if (best is not { } b)
        {
            how = "nothing usable";
            return false;
        }
        Save(FileOf(web.Key), b.Pix, b.W, b.H);
        how = $"{b.From} {b.W}x{b.H}";
        return true;
    }

    // Declared sizes first, the sharpest that is not huge on top: 192 px is ideal, anything from 64 px is fine.
    private static int Score(int size) => size >= 64 ? 10_000 - Math.Abs(size - 192) : size;

    // <link rel="...icon..." href sizes type> and the web manifest's icons; SVG is skipped (not drawable here).
    private static IEnumerable<(Uri Uri, int Size)> Declared(string html, Uri page, Attempt attempt, CancellationToken ct)
    {
        var found = new List<(Uri, int)>();
        Uri? manifest = null;
        foreach (Match m in Regex.Matches(html, @"<link\b[^>]*>", RegexOptions.IgnoreCase))
        {
            string tag = m.Value;
            string rel = Attr(tag, "rel").ToLowerInvariant(), href = WebUtility.HtmlDecode(Attr(tag, "href"));
            if (href.Length == 0 || !Uri.TryCreate(page, href, out var uri) || uri.Scheme != Uri.UriSchemeHttps) continue;
            if (rel.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("manifest")) { manifest ??= uri; continue; }
            if (!rel.Contains("icon") || rel.Contains("mask-icon") || IsSvg(href, Attr(tag, "type"))) continue;
            int size = Size(Attr(tag, "sizes"));
            found.Add((uri, size > 0 ? size : rel.Contains("apple-touch-icon") ? 180 : 32));
        }
        if (manifest is not null && GetText(manifest, MaxManifest, attempt, ct) is { } json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json.Text);
                if (doc.RootElement.TryGetProperty("icons", out var icons) && icons.ValueKind == JsonValueKind.Array)
                    foreach (var icon in icons.EnumerateArray())
                    {
                        string src = Str(icon, "src"), purpose = Str(icon, "purpose");
                        // Maskable icons are drawn edge to edge for a launcher to crop; they look wrong uncropped.
                        if (src.Length == 0 || IsSvg(src, Str(icon, "type")) || (purpose.Length > 0 && !purpose.Contains("any"))) continue;
                        if (Uri.TryCreate(json.Final, src, out var uri) && uri.Scheme == Uri.UriSchemeHttps) found.Add((uri, Size(Str(icon, "sizes"))));
                    }
            }
            catch (JsonException) { }
        }
        return found;
    }

    private static bool IsSvg(string href, string type) =>
        type.Contains("svg", StringComparison.OrdinalIgnoreCase) || href.Split('?')[0].EndsWith(".svg", StringComparison.OrdinalIgnoreCase);

    // "16x16 32x32" -> 32; "any" -> 0.
    private static int Size(string sizes)
    {
        int best = 0;
        foreach (Match m in Regex.Matches(sizes, @"(\d{1,4})[xX](\d{1,4})"))
            best = Math.Max(best, Math.Min(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)));
        return best;
    }

    private static string Attr(string tag, string name) =>
        Regex.Match(tag, $@"\b{name}\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))", RegexOptions.IgnoreCase) is { Success: true } m
            ? (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value).Trim()
            : "";

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    // A real picture of a sensible size with something visible in it, at most KeepPx on its longer side.
    private static (byte[] Pix, int W, int H)? Usable(byte[] data)
    {
        using var ms = new MemoryStream(data, writable: false);
        if (IconImages.Decode(ms) is not { } img || img.W < 16 || img.H < 16) return null;
        int visible = 0;
        for (int i = 3; i < img.Pix.Length; i += 4) if (img.Pix[i] > 32) visible++;
        if (visible < img.W * img.H / 50) return null;
        if (Math.Max(img.W, img.H) <= KeepPx) return (img.Pix, img.W, img.H);
        double f = (double)KeepPx / Math.Max(img.W, img.H);
        int w = Math.Max(1, (int)Math.Round(img.W * f)), h = Math.Max(1, (int)Math.Round(img.H * f));
        return (IconImages.Resample(img.Pix, img.W, img.H, w, h), w, h);
    }

    private static void Save(string file, byte[] pix, int w, int h)
    {
        var src = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, pix, w * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(src));
        using (var fs = new FileStream(file + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None)) encoder.Save(fs);
        File.Move(file + ".tmp", file, overwrite: true);
    }

    private static (string Text, Uri Final)? GetText(Uri uri, int limit, Attempt attempt, CancellationToken ct)
    {
        if (Get(uri, limit, "text/html,application/xhtml+xml,application/manifest+json,application/json;q=0.9,*/*;q=0.5", attempt, ct) is not { } r) return null;
        if (r.Type.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return null;
        return (Encoding.UTF8.GetString(r.Data), r.Final);
    }

    private static byte[]? GetBytes(Uri uri, int limit, Attempt attempt, CancellationToken ct)
    {
        if (Get(uri, limit, "image/png,image/x-icon,image/jpeg,image/*;q=0.9,*/*;q=0.5", attempt, ct) is not { } r) return null;
        // Single-page sites answer every path with their HTML.
        return r.Type.Contains("html", StringComparison.OrdinalIgnoreCase) || r.Type.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ? null : r.Data;
    }

    // At most limit bytes, https all the way, every hop of a redirect checked like the first address. Each read
    // honours the token, so a server that stops sending mid-body cannot hold the fetch.
    private static (byte[] Data, string Type, Uri Final)? Get(Uri uri, int limit, string accept, Attempt attempt, CancellationToken ct)
    {
        try
        {
            for (int hop = 0; hop <= 5; hop++)
            {
                if (uri.Scheme != Uri.UriSchemeHttps || !IsPublic(uri, ct)) return null;
                using var req = new HttpRequestMessage(HttpMethod.Get, uri);
                req.Headers.Accept.ParseAdd(accept);
                using var resp = Http.Send(req, HttpCompletionOption.ResponseHeadersRead, ct);
                if ((int)resp.StatusCode is >= 300 and < 400 && resp.Headers.Location is { } to)
                {
                    uri = to.IsAbsoluteUri ? to : new Uri(uri, to);
                    continue;
                }
                if (!resp.IsSuccessStatusCode || resp.Content.Headers.ContentLength > limit) return null;
                using var s = resp.Content.ReadAsStream(ct);
                var buffer = new MemoryStream();
                var chunk = new byte[16 * 1024];
                int n;
                while ((n = s.ReadAsync(chunk, ct).AsTask().GetAwaiter().GetResult()) > 0)
                {
                    buffer.Write(chunk, 0, n);
                    if (buffer.Length > limit) return null;
                }
                return (buffer.ToArray(), resp.Content.Headers.ContentType?.MediaType ?? "", uri);
            }
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
        {
            // Not reaching the server says nothing about whether it has an icon.
            if (ex is not IOException) attempt.Unreachable = true;
            return null;
        }
    }

    // A host on the internet: not a local name or an address of this computer or its network (a page's icon
    // links must not make this program probe the router or other devices). A name that does not resolve here
    // may still reach the internet through a proxy, so only resolved local addresses are refused.
    private static bool IsPublic(Uri uri, CancellationToken ct)
    {
        string host = uri.IdnHost.TrimEnd('.');
        if (IPAddress.TryParse(host.Trim('[', ']'), out var literal)) return !IsLocal(literal);
        if (!host.Contains('.') || Regex.IsMatch(host, @"(^|\.)(localhost|local|lan|home|internal|intranet|corp|home\.arpa)$", RegexOptions.IgnoreCase)) return false;
        try
        {
            var addresses = System.Net.Dns.GetHostAddressesAsync(host, ct).WaitAsync(TimeSpan.FromSeconds(3), ct).GetAwaiter().GetResult();
            return addresses.All(a => !IsLocal(a));
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or TimeoutException or ArgumentException) { return true; }
    }

    private static bool IsLocal(IPAddress a)
    {
        if (a.IsIPv4MappedToIPv6) a = a.MapToIPv4();
        if (IPAddress.IsLoopback(a) || a.Equals(IPAddress.Any) || a.Equals(IPAddress.IPv6Any)) return true;
        if (a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            return a.IsIPv6LinkLocal || a.IsIPv6SiteLocal || a.IsIPv6UniqueLocal || a.IsIPv6Multicast;
        var b = a.GetAddressBytes();
        return b[0] == 10 || b[0] == 0 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168)
               || (b[0] == 169 && b[1] == 254) || (b[0] == 100 && b[1] >= 64 && b[1] <= 127) || b[0] >= 224;
    }

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false, // followed by hand, so each hop is checked
            ConnectCallback = ConnectPublic,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(6),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        // Many sites turn away clients that do not look like a browser; nothing about the user is sent.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
        return client;
    }

    // The connection itself goes only to an internet address from this very lookup (a name answering with a
    // local address as well, or differently the second time, cannot reach the local network); the system's
    // configured proxy, usually on this computer, is the one exception.
    private static async ValueTask<Stream> ConnectPublic(SocketsHttpConnectionContext ctx, CancellationToken ct)
    {
        string host = ctx.DnsEndPoint.Host;
        int port = ctx.DnsEndPoint.Port;
        // Only the handler itself sends CONNECT: the tunnel to the configured proxy (this program asks for https only).
        bool toProxy = ctx.InitialRequestMessage.Method == HttpMethod.Connect;
        var addresses = IPAddress.TryParse(host, out var literal) ? new[] { literal } : await System.Net.Dns.GetHostAddressesAsync(host, ct);
        var allowed = toProxy ? addresses : addresses.Where(a => !IsLocal(a)).ToArray();
        if (allowed.Length == 0) throw new HttpRequestException($"{host} is on the local network");
        var socket = new System.Net.Sockets.Socket(System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(allowed, port, ct);
            return new System.Net.Sockets.NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static string FileOf(string key)
    {
        var sb = new StringBuilder();
        foreach (char c in key.ToLowerInvariant()) sb.Append(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '-' ? c : '-');
        return Path.Combine(Dir, sb + ".png");
    }

    private static Dictionary<string, DateTime> Failed()
    {
        if (_failed is not null) return _failed;
        var d = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (File.Exists(FailedFile))
                foreach (string line in File.ReadLines(FailedFile))
                    if (line.Split('\t') is { Length: 2 } p && long.TryParse(p[1], out long t)) d[p[0]] = DateTime.FromBinary(t);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { Log.Error("web-icons-read", ex); }
        return _failed = d;
    }

    private static void SaveFailed()
    {
        try
        {
            var sb = new StringBuilder();
            foreach (var (k, at) in Failed())
                if (DateTime.UtcNow - at <= RetryAfter) sb.Append(k).Append('\t').Append(at.ToBinary()).Append('\n');
            File.WriteAllText(FailedFile + ".tmp", sb.ToString());
            File.Move(FailedFile + ".tmp", FailedFile, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Error("web-icons-save", ex); }
    }
}
