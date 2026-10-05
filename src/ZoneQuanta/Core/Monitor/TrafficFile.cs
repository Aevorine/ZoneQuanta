using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace ZoneQuanta.Core.Monitor;

public sealed class TrafficFile
{
    public const string TotalKey = "*";
    private const long Hour = 3600, Day = 86400, HourlyKeepSeconds = 90L * Day;

    private readonly Dictionary<(long Bucket, string App), (long Up, long Down)> _data = new();

    public static string DefaultDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZoneQuanta");

    public static TrafficFile Load(string path)
    {
        var f = new TrafficFile();
        try
        {
            if (!File.Exists(path)) return f;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                var p = line.Split('\t');
                if (p.Length != 4) continue;
                if (long.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long b)
                    && long.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long up)
                    && long.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out long down))
                    f.Add(b, p[1], up, down);
            }
        }
        catch (IOException) { }
        return f;
    }

    public void Add(long bucket, string app, long up, long down)
    {
        var key = (bucket, app);
        _data.TryGetValue(key, out var cur);
        _data[key] = (cur.Up + up, cur.Down + down);
    }

    public void AddNow(string app, long up, long down) => Add(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / Hour * Hour, app, up, down);

    public void Compact()
    {
        long cutoff = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - HourlyKeepSeconds;
        var old = new List<KeyValuePair<(long Bucket, string App), (long Up, long Down)>>();
        foreach (var kv in _data)
            if (kv.Key.Bucket < cutoff && kv.Key.Bucket % Day != 0) old.Add(kv);
        foreach (var kv in old)
        {
            _data.Remove(kv.Key);
            Add(kv.Key.Bucket / Day * Day, kv.Key.App, kv.Value.Up, kv.Value.Down);
        }
    }

    public void Save(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string tmp = path + ".tmp";
            using (var w = new StreamWriter(tmp))
            {
                foreach (var kv in _data)
                    w.WriteLine(string.Join('\t', kv.Key.Bucket.ToString(CultureInfo.InvariantCulture), kv.Key.App,
                        kv.Value.Up.ToString(CultureInfo.InvariantCulture), kv.Value.Down.ToString(CultureInfo.InvariantCulture)));
            }
            File.Move(tmp, path, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public Dictionary<string, (long Up, long Down)> Query(long fromUnix)
    {
        var result = new Dictionary<string, (long Up, long Down)>();
        long cutoff = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - HourlyKeepSeconds;
        foreach (var kv in _data)
        {
            long duration = kv.Key.Bucket < cutoff && kv.Key.Bucket % Day == 0 ? Day : Hour;
            if (kv.Key.Bucket + duration <= fromUnix) continue;
            result.TryGetValue(kv.Key.App, out var cur);
            result[kv.Key.App] = (cur.Up + kv.Value.Up, cur.Down + kv.Value.Down);
        }
        return result;
    }

    // Recent records use hourly buckets. Do not treat a UTC-midnight bucket
    // as a whole day: doing so includes yesterday in local today queries.
    public long TodayTotal()
    {
        long from = RangeStart("Today");
        long until = new DateTimeOffset(DateTime.Now.Date.AddDays(1)).ToUnixTimeSeconds();
        long total = 0;
        foreach (var kv in _data)
            if (kv.Key.App == TotalKey && kv.Key.Bucket + Hour > from && kv.Key.Bucket < until)
                total += kv.Value.Up + kv.Value.Down;
        return total;
    }

    public static long RangeStart(string range)
    {
        var now = DateTime.Now;
        DateTime from = range switch
        {
            "Today" => now.Date,
            "Day" => now.AddHours(-24),
            "Week" => now.Date.AddDays(-(((int)now.DayOfWeek + 6) % 7)),
            "Month" => new DateTime(now.Year, now.Month, 1),
            "Year" => new DateTime(now.Year, 1, 1),
            _ => DateTime.MinValue,
        };
        if (from == DateTime.MinValue) return 0;
        return new DateTimeOffset(from).ToUnixTimeSeconds();
    }
}
