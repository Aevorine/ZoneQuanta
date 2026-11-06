using System;
using System.IO;
using System.Runtime.InteropServices;
using ZoneQuanta.Core.Quick;

namespace ZoneQuanta.Platform;

// Whether a quick item's target is gone for good: the file or folder is missing, a shortcut points at a
// program that was removed, or a Store / Start app was uninstalled. Anything that might only be out of reach
// (an unplugged drive, a network share, an Internet address, a shortcut without a file target) is not gone.
// Any thread; a check costs a few file-system lookups.
internal static class ItemHealth
{
    public static bool IsGone(string path)
    {
        try
        {
            if (path.StartsWith(@"shell:AppsFolder\", StringComparison.OrdinalIgnoreCase)) return !ShellItemExists(path);
            if (PathEngine.IsShell(path) || !Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal)) return false;
            if (!Reachable(path)) return false;
            if (!File.Exists(path) && !Directory.Exists(path)) return true;
            if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return false;
            string target = ShellLinks.Target(path);
            if (target.Length == 0 || !Path.IsPathFullyQualified(target) || target.StartsWith(@"\\", StringComparison.Ordinal) || !Reachable(target)) return false;
            return !Present(target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or COMException)
        {
            return false;
        }
    }

    // A 64-bit reader may see a 32-bit program's "Program Files (x86)" target as "Program Files" (and back).
    private static bool Present(string target)
    {
        if (File.Exists(target) || Directory.Exists(target)) return true;
        const string x64 = @"\Program Files\", x86 = @"\Program Files (x86)\";
        string other = target.Contains(x86, StringComparison.OrdinalIgnoreCase)
            ? target.Replace(x86, x64, StringComparison.OrdinalIgnoreCase)
            : target.Replace(x64, x86, StringComparison.OrdinalIgnoreCase);
        return other != target && (File.Exists(other) || Directory.Exists(other));
    }

    // The drive is there (a missing removable or network drive says nothing about the file).
    private static bool Reachable(string path) => Path.GetPathRoot(path) is { Length: > 0 } root && Directory.Exists(root);

    // Only "not found" answers mean uninstalled; any other failure (the shell busy at sign-in) says nothing.
    private static bool ShellItemExists(string path)
    {
        var iid = typeof(IShellItem).GUID;
        int hr = SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var item);
        if (item is not null) Marshal.ReleaseComObject(item);
        return hr == 0 || !(unchecked((uint)hr) is 0x80070002 or 0x80070003 or 0x80070490);
    }

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem { }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItem? item);
}
