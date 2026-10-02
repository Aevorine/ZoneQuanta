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
