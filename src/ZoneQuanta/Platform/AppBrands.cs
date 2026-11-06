using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using ZoneQuanta.Core;

namespace ZoneQuanta.Platform;

// What a program that carries no icon of its own can show instead, from what is known about its product:
// Windows' own artwork for Windows components, the icon an editor extension declares, and the product's
// website or GitHub organisation (from its npm package, or a short table of well-known command-line tools)
// for WebIcons to fetch. Also finds program files behind npm / Scoop shims and older copies' names.
internal static class AppBrands
{
    // Where a brand icon can be fetched: the product's website, and its GitHub organisation as a fallback.
    public sealed record Web(string? Domain, string? GitHub)
    {
        public string Key => Domain ?? "github-" + GitHub;
        public string Spec => Domain + "|" + GitHub;

        public static Web? Parse(string spec)
        {
            int bar = spec.IndexOf('|');
            if (bar < 0) return null;
            string d = spec[..bar], g = spec[(bar + 1)..];
            return Make(d.Length > 0 ? d : null, g.Length > 0 ? g : null);
        }

        // Only plain host names and GitHub account names, so nothing else can be asked for.
        public static Web? Make(string? domain, string? github)
        {
            if (domain is not null && !Regex.IsMatch(domain, @"^(?=.{4,100}$)([a-z0-9](-?[a-z0-9])*\.)+[a-z]{2,}$")) domain = null;
            if (github is not null && !Regex.IsMatch(github, "^[A-Za-z0-9](-?[A-Za-z0-9]){0,38}$")) github = null;
            return domain is null && github is null ? null : new Web(domain, github);
        }
    }

    private sealed record Rule(string Name, string? InPath, string? Product, Web Web);

    // Command-line tools and services that ship without an icon. Name: process name, "prefix*" or "*suffix";
    // InPath: only programs whose path contains it; Product: an installed product (Apps & features) of the same
    // family whose icon is used before anything is fetched.
    private static readonly Rule[] Rules =
    {
        new("*", @"\Android\Sdk\", null, new("developer.android.com", "android")),
        new("adb", null, null, new("developer.android.com", "android")),
        new("fastboot", null, null, new("developer.android.com", "android")),
        new("qemu-system-*", null, null, new("www.qemu.org", "qemu")),
        new("gh", null, null, new("github.com", "github")),
        new("grok", null, null, new("grok.com", "xai-org")),
        new("codex", null, null, new("openai.com", "openai")),
        new("ChatGPT", null, null, new("chatgpt.com", "openai")),
        new("Gemini", null, null, new("gemini.google.com", "google-gemini")),
        new("Foxit*", null, null, new("www.foxit.com", null)),
        new("cua-driver", null, null, new("cua.ai", "trycua")),
        new("opencode", null, null, new("opencode.ai", null)),
        new("rustup*", null, null, new("www.rust-lang.org", "rust-lang")),
        new("cargo", null, null, new("www.rust-lang.org", "rust-lang")),
        new("rustc", null, null, new("www.rust-lang.org", "rust-lang")),
        new("rust-analyzer", null, null, new("www.rust-lang.org", "rust-lang")),
        new("go", null, null, new("go.dev", "golang")),
        new("gopls", null, null, new("go.dev", "golang")),
        new("*.test", null, null, new("go.dev", "golang")), // a Go test binary
        new("curl", null, null, new("curl.se", "curl")),
        new("uv", null, null, new("astral.sh", "astral-sh")),
        new("uvx", null, null, new("astral.sh", "astral-sh")),
        new("ruff", null, null, new("astral.sh", "astral-sh")),
        new("workerd", null, null, new("www.cloudflare.com", "cloudflare")),
        new("cloudflared", null, null, new("www.cloudflare.com", "cloudflare")),
        new("deno", null, null, new("deno.com", "denoland")),
        new("bun", null, null, new("bun.sh", "oven-sh")),
        new("ffmpeg", null, null, new("ffmpeg.org", "FFmpeg")),
        new("ffprobe", null, null, new("ffmpeg.org", "FFmpeg")),
        new("yt-dlp", null, null, new(null, "yt-dlp")),
        new("aria2c", null, null, new(null, "aria2")),
        new("ollama", null, null, new("ollama.com", "ollama")),
        new("vs_*", null, "Visual Studio Installer", new("visualstudio.microsoft.com", "microsoft")),
    };

    public static Web? Known(string name, string path)
    {
        foreach (var r in Rules)
        {
            if (r.InPath is not null && !path.Contains(r.InPath, StringComparison.OrdinalIgnoreCase)) continue;
            if (Matches(r.Name, name)) return r.Web;
        }
        return null;
    }

    // An installed product of the same family that has an icon (vs_setup_bootstrapper -> Visual Studio Installer).
    public static string? ProductIcon(string name, string path)
    {
        foreach (var r in Rules)
            if (r.Product is not null && (r.InPath is null || path.Contains(r.InPath, StringComparison.OrdinalIgnoreCase)) && Matches(r.Name, name)
                && InstalledPrograms.Named(r.Product) is { } p)
                return p.Icon;
        return null;
    }

    private static bool Matches(string pattern, string name) =>
        pattern == "*" ? true
        : pattern.EndsWith('*') ? name.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase)
        : pattern.StartsWith('*') ? name.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase)
        : string.Equals(pattern, name, StringComparison.OrdinalIgnoreCase);

    private static readonly HashSet<string> Runtimes = new(StringComparer.OrdinalIgnoreCase) { "Bun", "Deno", "Node.js" };
    private static readonly HashSet<string> RuntimeNames = new(StringComparer.OrdinalIgnoreCase) { "bun", "bunx", "deno", "node" };

    // A program compiled into a runtime's single executable (bun build --compile, deno compile) carries the
    // runtime's icon and description, not its own: opencode.exe says "Bun".
    public static bool RuntimeBuilt(string path, string name)
    {
        if (RuntimeNames.Contains(name)) return false;
        try
        {
            if (!File.Exists(path)) return false;
            var v = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
            return Runtimes.Contains(v.ProductName?.Trim() ?? "") || Runtimes.Contains(v.FileDescription?.Trim() ?? "");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private static readonly HashSet<string> Defender = new(StringComparer.OrdinalIgnoreCase)
    {
        "smartscreen", "MsMpEng", "MpCmdRun", "NisSrv", "MpDefenderCoreService", "SecurityHealthService", "SecurityHealthSystray", "SecurityHealthHost",
    };

    // Windows' own artwork for its components that carry no icon: the Windows logo, the Windows Security
    // shield for Defender's parts.
    public static string? WindowsArt(string name)
    {
        string sys = Environment.SystemDirectory;
        foreach (string f in Defender.Contains(name) ? new[] { "WindowsSecurityIcon.png", "@WLOGO_96x96.png" } : new[] { "@WLOGO_96x96.png" })
        {
            string file = Path.Combine(sys, f);
            if (File.Exists(file)) return IconImages.Prefix + file;
        }
        return null;
    }

    // ...\extensions\<publisher.name-version>\...: the icon the extension's package.json declares.
    public static string? ExtensionIcon(string path)
    {
        int at = path.LastIndexOf(@"\extensions\", StringComparison.OrdinalIgnoreCase);
        if (at < 0) return null;
        int end = path.IndexOf('\\', at + 12);
        if (end < 0) return null;
        string root = path[..end];
        if (ReadJson(Path.Combine(root, "package.json")) is not { } json) return null;
        using (json)
        {
            if (!json.RootElement.TryGetProperty("icon", out var icon) || icon.ValueKind != JsonValueKind.String) return null;
            try
            {
                string file = Path.GetFullPath(Path.Combine(root, icon.GetString()!));
                bool inside = file.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase);
                bool image = Path.GetExtension(file).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".ico";
                return inside && image && File.Exists(file) ? IconImages.Prefix + file : null;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
        }
    }

    // A program inside node_modules: the website or GitHub organisation its package (or an enclosing one) names.
    public static Web? NpmHome(string path)
    {
        string dir = Path.GetDirectoryName(path) ?? "";
        for (int guard = 0; guard < 12 && dir.Contains(@"\node_modules\", StringComparison.OrdinalIgnoreCase); guard++)
        {
            if (ReadJson(Path.Combine(dir, "package.json")) is { } json)
                using (json)
                {
                    var root = json.RootElement;
                    string? home = Text(root, "homepage");
                    string? repo = root.TryGetProperty("repository", out var r) ? r.ValueKind == JsonValueKind.String ? r.GetString() : Text(r, "url") : null;
                    string? owner = repo is null ? null : Regex.Match(repo, @"github(?:\.com[:/]|:)([A-Za-z0-9-]+)/") is { Success: true } m ? m.Groups[1].Value : null;
                    string? host = home is not null && Uri.TryCreate(home, UriKind.Absolute, out var u) && u.Scheme is "https" or "http" ? u.Host.ToLowerInvariant() : null;
                    // A homepage on GitHub itself says the same as the repository.
                    if (host is "github.com" or "www.github.com") host = null;
                    if (Web.Make(host, owner) is { } web) return web;
                }
            dir = Path.GetDirectoryName(dir) ?? "";
        }
        return null;
    }

    // %APPDATA%\npm\<name>.cmd naming "%dp0%\node_modules\...\name.exe", or a Scoop shim's "path = ...".
    public static string? ShimTarget(string name)
    {
        string npm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm");
        if (Small(Path.Combine(npm, name + ".cmd")) is { } cmd && Regex.Match(cmd, @"%~?dp0%?\\(node_modules\\[^""%\r\n]+?\.exe)", RegexOptions.IgnoreCase) is { Success: true } m
            && Inside(npm, m.Groups[1].Value) is { } exe)
            return exe;
        string scoop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scoop");
        if (Small(Path.Combine(scoop, "shims", name + ".shim")) is { } shim && Regex.Match(shim, @"^\s*path\s*=\s*""?([^""\r\n]+?\.exe)""?\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase) is { Success: true } s
            && Inside(scoop, s.Groups[1].Value) is { } target)
            return target;
        return null;
    }

    // Names an older or temporary copy runs under: old_x, x_old, x.exe.old.123, x-headless.
    public static IEnumerable<string> BaseNames(string name)
    {
        foreach (string pattern in new[]
        {
            @"^(?:old|new|bak)[_-](.+)$", @"^(.+?)[._-](?:old|bak|new)(?:[._-]?\d+)?$", @"^(.+?)\.exe\.old\.\d+$", @"^(.+?)-headless$",
        })
            if (Regex.Match(name, pattern, RegexOptions.IgnoreCase) is { Success: true } m && m.Groups[1].Value.Length > 1)
                yield return m.Groups[1].Value;
    }

    private static string? Inside(string root, string relative)
    {
        try
        {
            string full = Path.GetFullPath(Path.IsPathFullyQualified(relative) ? relative : Path.Combine(root, relative));
            return full.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase) && File.Exists(full) ? full : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }

    private static string? Small(string file, int limit = 64 * 1024)
    {
        try { return File.Exists(file) && new FileInfo(file).Length < limit ? File.ReadAllText(file) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private static JsonDocument? ReadJson(string file)
    {
        if (Small(file, 4 * 1024 * 1024) is not { } text) return null; // an extension's manifest can be large
        try { return JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }); }
        catch (JsonException) { return null; }
    }

    private static string? Text(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
