using System;
using System.IO;
using System.Text;

namespace ZoneQuanta.Core;

// Power-loss safe text files: the new text reaches the disk (write-through) before it replaces the file
// in one step, and the previous version stays as ".bak". Reading falls back to ".bak" when the file is
// missing, empty or does not parse, so a cut-off write never resets the user's settings.
public static class DurableFile
{
    public static void Write(string path, string text)
    {
        string tmp = path + ".tmp", bak = path + ".bak";
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            fs.Write(bytes, 0, bytes.Length);
            fs.Flush(flushToDisk: true);
        }
        if (!File.Exists(path))
        {
            File.Move(tmp, path);
            return;
        }
        try { File.Replace(tmp, path, bak, ignoreMetadataErrors: true); }
        catch (IOException)
        {
            // ReplaceFile can refuse (e.g. the file is briefly open elsewhere); copy-then-move still keeps a .bak.
            if (File.Exists(path)) File.Copy(path, bak, overwrite: true);
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    File.Move(tmp, path, overwrite: true);
                    return;
                }
                catch (IOException) when (attempt < 5) { System.Threading.Thread.Sleep(20); } // a reader may hold it a moment
            }
        }
    }

    // The first of path / path.bak whose text `parse` accepts (non-null); null when neither does.
    public static T? Read<T>(string path, Func<string, T?> parse, string what) where T : class
    {
        foreach (string p in new[] { path, path + ".bak" })
        {
            try
            {
                if (!File.Exists(p)) continue;
                string text = File.ReadAllText(p);
                if (text.Trim().Length > 0 && parse(text) is { } value)
                {
                    if (p != path)
                    {
                        Log.Write($"{what}: main file unreadable, restored from .bak");
                        // Repair now; if that fails the next save writes a good main file anyway.
                        try { File.Copy(p, path, overwrite: true); }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Error($"{what}-repair", ex); }
                    }
                    return value;
                }
                Log.Write($"{what}: {Path.GetFileName(p)} is empty");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or NotSupportedException)
            {
                Log.Error($"{what}-read {Path.GetFileName(p)}", ex);
            }
        }
        return null;
    }
}
