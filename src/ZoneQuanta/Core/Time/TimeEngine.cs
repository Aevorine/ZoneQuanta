using System;
using System.Collections.Generic;
using ZoneQuanta.Core.Settings;

namespace ZoneQuanta.Core.Time;

public readonly record struct ZoneSnapshot(
    string Label,
    DateTime Time,
    int OffsetMinutes,
    int RelativeToLocalMinutes,
    int DayDelta,
    bool IsDst,
    bool IsLocal,
    bool IsDay,
    double DayProgress);

public sealed record DstInfo(string Name, bool Supports, bool IsDst, int OffsetMinutes, DateTime? NextChange, int? OffsetAfter, bool? NextIsDst, string? YearSpan);

public sealed class TimeEngine
{
    private readonly Dictionary<string, TimeZoneInfo> _cache = new();

    public void Refresh() => _cache.Clear();

    public ZoneSnapshot Compute(ZoneConfig zone, DateTimeOffset utcNow)
    {
        bool isLocal = zone.TimeZoneId == "local";
        TimeZoneInfo tz = Resolve(zone.TimeZoneId);
        TimeZoneInfo local = TimeZoneInfo.Local;

        DateTimeOffset zoned = TimeZoneInfo.ConvertTime(utcNow, tz);
        DateTimeOffset localNow = TimeZoneInfo.ConvertTime(utcNow, local);

        int offset = (int)zoned.Offset.TotalMinutes;
        int relative = offset - (int)localNow.Offset.TotalMinutes;
        int dayDelta = (zoned.Date - localNow.Date).Days;

        double minutes = zoned.Hour * 60 + zoned.Minute + zoned.Second / 60.0;
        bool isDay = minutes >= 360 && minutes < 1080;
        double since = isDay ? minutes - 360 : (minutes >= 1080 ? minutes - 1080 : minutes + 360);

        return new ZoneSnapshot(zone.Label, zoned.DateTime, offset, relative, dayDelta,
            tz.IsDaylightSavingTime(zoned), isLocal, isDay, since / 720.0);
    }

    public DstInfo GetDstInfo(ZoneConfig zone, DateTimeOffset utcNow)
    {
        TimeZoneInfo tz = Resolve(zone.TimeZoneId);
        string name = zone.TimeZoneId == "local" ? TimeZoneInfo.Local.DisplayName : tz.DisplayName;
        var now = TimeZoneInfo.ConvertTime(utcNow, tz);
        int offset = (int)now.Offset.TotalMinutes;
        if (!tz.SupportsDaylightSavingTime)
            return new DstInfo(name, false, false, offset, null, null, null, null);

        bool isDst = tz.IsDaylightSavingTime(now);
        var next = FindChange(tz, utcNow);

        var yearStart = new DateTimeOffset(now.Year, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var first = FindChange(tz, yearStart);
        var second = first is null ? null : FindChange(tz, first.Value.Utc.AddHours(1));
        string? year = null;
        if (first is { } a && second is { } b && a.Local.Year == now.Year)
        {
            var (start, end) = a.ToDst ? (a, b) : (b, a);
            year = $"{now.Year} 年夏令时：{start.Local:M月d日 HH:mm} 至 {end.Local:M月d日 HH:mm}";
        }
        return new DstInfo(name, true, isDst, offset, next?.Local, next is null ? null : (int)next.Value.OffsetAfter, next?.ToDst, year);
    }

    private readonly record struct Change(DateTimeOffset Utc, DateTime Local, bool ToDst, double OffsetAfter);

    private static Change? FindChange(TimeZoneInfo tz, DateTimeOffset fromUtc)
    {
        bool state = tz.IsDaylightSavingTime(fromUtc);
        DateTimeOffset t = fromUtc;
        for (int d = 0; d < 400; d++)
        {
            DateTimeOffset nextDay = t.AddDays(1);
            if (tz.IsDaylightSavingTime(nextDay) != state)
            {
                DateTimeOffset h = t;
                while (tz.IsDaylightSavingTime(h.AddHours(1)) == state) h = h.AddHours(1);
                DateTimeOffset m = h;
                while (tz.IsDaylightSavingTime(m.AddMinutes(1)) == state) m = m.AddMinutes(1);
                DateTimeOffset hit = m.AddMinutes(1);
                var after = TimeZoneInfo.ConvertTime(hit, tz);
                var before = TimeZoneInfo.ConvertTime(hit.AddMinutes(-1), tz);
                return new Change(hit, before.DateTime.AddMinutes(1), !state, after.Offset.TotalMinutes);
            }
            t = nextDay;
        }
        return null;
    }

    private TimeZoneInfo Resolve(string id)
    {
        if (id == "local") return TimeZoneInfo.Local;
        if (_cache.TryGetValue(id, out var tz)) return tz;
        try { tz = TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { tz = TimeZoneInfo.Utc; }
        catch (InvalidTimeZoneException) { tz = TimeZoneInfo.Utc; }
        _cache[id] = tz;
        return tz;
    }
}
