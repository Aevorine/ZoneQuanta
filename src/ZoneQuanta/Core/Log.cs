using System;
using System.IO;

namespace ZoneQuanta.Core;

public static class Log
{
    private const long MaxBytes = 128 * 1024;
    private static readonly object Gate = new();
    private static readonly string PathName = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZoneQuanta", "app.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathName)!);
                var info = new FileInfo(PathName);
                if (info.Exists && info.Length > MaxBytes) File.Move(PathName, PathName + ".1", true);
                File.AppendAllText(PathName, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public static void Error(string context, Exception ex)
    {
        string frame = ex.StackTrace?.Split(Environment.NewLine, 2)[0].Trim() ?? string.Empty;
        string file = ex is FileNotFoundException f && f.FileName is { Length: > 0 } n ? $" [{n}]" : string.Empty;
        Write($"{context}: {ex.GetType().Name}: {ex.Message}{file} {frame}".TrimEnd());
    }
}
