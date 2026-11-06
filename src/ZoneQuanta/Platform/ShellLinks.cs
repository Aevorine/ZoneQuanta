using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using ZoneQuanta.Core.Quick;

namespace ZoneQuanta.Platform;

// Shortcut target and program description, used to group quick items the name alone does not explain.
internal static class ShellLinks
{
    // Any thread: the shell link object is free-threaded.
    public static TargetHint? Describe(string path)
    {
        try
        {
            string target = path, args = "", comment = "";
            if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                (target, args, comment) = Resolve(path);
            // An Internet shortcut: its address is the target; an imported one also says which folders it was filed in.
            else if (path.EndsWith(".url", StringComparison.OrdinalIgnoreCase) && File.Exists(path) && new FileInfo(path).Length < 64 * 1024)
            {
                var lines = File.ReadLines(path).Select(l => l.Trim()).ToList();
                target = lines.FirstOrDefault(l => l.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))?[4..] ?? "";
                comment = lines.FirstOrDefault(l => l.StartsWith("Folder=", StringComparison.OrdinalIgnoreCase))?[7..] ?? "";
            }
            if (target.Length == 0) return null;
            string desc = "", product = "", company = "";
            if (target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(target))
            {
                var v = FileVersionInfo.GetVersionInfo(target);
                desc = v.FileDescription?.Trim() ?? "";
                product = v.ProductName?.Trim() ?? "";
                company = v.CompanyName?.Trim() ?? "";
            }
            return ProgramFacts.With(new TargetHint(target, args, desc, product, company, comment));
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or ArgumentException or InvalidCastException)
        {
            return null;
        }
    }

    // Target of a shortcut, "" when it cannot be read.
    public static string Target(string lnk)
    {
        try { return Resolve(lnk).Target; }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or ArgumentException or InvalidCastException) { return ""; }
    }

    private static (string Target, string Args, string Comment) Resolve(string lnk)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            ((IPersistFile)link).Load(lnk, 0);
            var path = new StringBuilder(1024);
            link.GetPath(path, path.Capacity, IntPtr.Zero, 0);
            var args = new StringBuilder(2048);
            link.GetArguments(args, args.Capacity);
            var comment = new StringBuilder(1024);
            link.GetDescription(comment, comment.Capacity);
            return (Environment.ExpandEnvironmentVariables(path.ToString()), args.ToString(), comment.ToString().Trim());
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    // Only the methods up to GetArguments are called; the vtable order must still match IShellLinkW.
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int cch, IntPtr findData, uint flags);
        void GetIDList(out IntPtr pidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int cch);
    }
}
