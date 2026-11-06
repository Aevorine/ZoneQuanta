using System;
using System.Text.RegularExpressions;

namespace ZoneQuanta.Core.Update;

// Addresses of the official release pages on GitHub. Built only from constants compiled into this program
// and, for one release, the tag and file name of a manifest whose signature was verified; an environment
// override (used for testing downloads) never changes them. Every address is checked again right before it
// is opened, so neither a forged manifest nor a changed setting can send the link anywhere else.
public static class ReleaseLinks
{
    public const string Host = "github.com";
    private static readonly string Root = $"https://{Host}/{UpdateService.Repo}/releases";
    private static readonly Regex Tag = new(@"^v\d{1,4}\.\d{1,4}\.\d{1,6}$", RegexOptions.CultureInvariant);
    private static readonly Regex Asset = new(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,80}\.exe$", RegexOptions.CultureInvariant);

    // Always the newest release, whichever it is.
    public static string Latest => Root + "/latest";

    // The page of the release a verified manifest names, else the newest release.
    public static string PageOf(UpdateInfo? info) => info is not null && Tag.IsMatch(info.Tag) ? $"{Root}/tag/{info.Tag}" : Latest;

    // Direct download of that release's program file; null when the manifest names nothing valid.
    public static string? FileOf(UpdateInfo? info) =>
        info is not null && Tag.IsMatch(info.Tag) && Asset.IsMatch(info.AssetName) ? $"{Root}/download/{info.Tag}/{info.AssetName}" : null;

    // https, exactly github.com, this repository's releases, nothing hidden (no user name, port, query or
    // fragment), and written exactly as it canonicalises, so what is shown is what opens.
    public static bool IsOfficial(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u)
        && u.Scheme == Uri.UriSchemeHttps && u.IsDefaultPort && u.UserInfo.Length == 0
        && string.Equals(u.Host, Host, StringComparison.Ordinal)
        && u.Query.Length == 0 && u.Fragment.Length == 0
        && (u.AbsolutePath == $"/{UpdateService.Repo}/releases" || u.AbsolutePath.StartsWith($"/{UpdateService.Repo}/releases/", StringComparison.Ordinal))
        && string.Equals(u.AbsoluteUri, url, StringComparison.Ordinal);
}
