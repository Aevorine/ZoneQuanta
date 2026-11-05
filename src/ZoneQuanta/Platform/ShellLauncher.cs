using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using ZoneQuanta.Core;

namespace ZoneQuanta.Platform;

// Everything that hands a path to the Windows shell.
internal static class ShellLauncher
{
    public static bool IsExecutable(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".exe" or ".bat" or ".cmd" or ".msi" or ".lnk" or ".ps1" or ".com";

    public static bool Open(string target)
    {
        try
        {
            var psi = new ProcessStartInfo(target) { UseShellExecute = true };
            if (File.Exists(target)) psi.WorkingDirectory = Path.GetDirectoryName(target) ?? "";
            Process.Start(psi)?.Dispose();
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1155 && File.Exists(target))
        {
            OpenWith(target); // no associated program: let the user pick one
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            Log.Error("open", ex);
            return false;
        }
    }

    public static bool RunAsAdmin(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "runas", WorkingDirectory = Path.GetDirectoryName(path) ?? "" })?.Dispose();
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { return false; } // UAC declined
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            Log.Error("runas", ex);
            return false;
        }
    }

    public static void OpenWith(string path)
    {
        try { Process.Start(new ProcessStartInfo("rundll32.exe", $"shell32.dll,OpenAs_RunDLL {path}") { UseShellExecute = false })?.Dispose(); }
        catch (Win32Exception ex) { Log.Error("open-with", ex); }
    }

    // Selects the item in an Explorer window, reusing one that already shows the folder.
    public static void Reveal(string path)
    {
        IntPtr pidl = ILCreateFromPathW(path);
        if (pidl != IntPtr.Zero)
        {
            try { if (SHOpenFolderAndSelectItems(pidl, 0, IntPtr.Zero, 0) == 0) return; }
            finally { ILFree(pidl); }
        }
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true })?.Dispose(); }
        catch (Win32Exception ex) { Log.Error("reveal", ex); }
    }

    public static void Properties(string path)
    {
        var info = new SHELLEXECUTEINFO
        {
            cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
            fMask = SEE_MASK_INVOKEIDLIST,
            lpVerb = "properties",
            lpFile = path,
            nShow = 1,
        };
        if (!ShellExecuteEx(ref info)) Log.Write($"properties failed {Marshal.GetLastWin32Error()}");
    }

    private const uint SEE_MASK_INVOKEIDLIST = 0xC;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHELLEXECUTEINFO
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public string? lpVerb, lpFile, lpParameters, lpDirectory;
        public int nShow;
        public IntPtr hInstApp, lpIDList;
        public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon, hProcess;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ILCreateFromPathW(string path);

    [DllImport("shell32.dll")]
    private static extern void ILFree(IntPtr pidl);

    [DllImport("shell32.dll")]
    private static extern int SHOpenFolderAndSelectItems(IntPtr folder, uint count, IntPtr items, uint flags);
}
