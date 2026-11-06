using System;
using System.IO;

namespace ZoneQuanta.Core;

public static class Log
{
    private const long MaxBytes = 1024 * 1024;
    private static readonly object Gate = new();
    private static string PathName => DataPaths.File("app.log");

    // Set by the elevated traffic helper: a log line is written only where it is safe to.
    public static Func<string, bool>? CanWrite;

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                if (CanWrite is { } can && (!can(PathName) || !can(PathName + ".1"))) return;
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathName)!);
                var info = new FileInfo(PathName);
                if (info.Exists && info.Length > MaxBytes) File.Move(PathName, PathName + ".1", true);
                File.AppendAllText(PathName, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // Unhandled exceptions: the whole chain and stack, for diagnosing from the log alone.
    public static void Crash(string context, Exception ex) => Write($"CRASH {context}: {ex}");

    public static void Error(string context, Exception ex)
    {
        string frame = ex.StackTrace?.Split(Environment.NewLine, 2)[0].Trim() ?? string.Empty;
        string file = ex is FileNotFoundException f && f.FileName is { Length: > 0 } n ? $" [{n}]" : string.Empty;
        Write($"{context}: {ex.GetType().Name}: {ex.Message}{file} {frame}".TrimEnd());
    }
}
