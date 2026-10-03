using System;

namespace ZoneQuanta.Core.Time;

public readonly record struct SunTimes(double RiseMinutes, double SetMinutes, bool PolarDay, bool PolarNight);

public static class SunCalc
{
    private const double Zenith = 90.833;

    public static SunTimes For(DateTime date, double latitude, double longitude, double utcOffsetMinutes)
    {
        double g = 2 * Math.PI / 365.0 * (date.DayOfYear - 1);
        double eqTime = 229.18 * (0.000075 + 0.001868 * Math.Cos(g) - 0.032077 * Math.Sin(g)
                                  - 0.014615 * Math.Cos(2 * g) - 0.040849 * Math.Sin(2 * g));
        double decl = 0.006918 - 0.399912 * Math.Cos(g) + 0.070257 * Math.Sin(g)
                      - 0.006758 * Math.Cos(2 * g) + 0.000907 * Math.Sin(2 * g)
                      - 0.002697 * Math.Cos(3 * g) + 0.00148 * Math.Sin(3 * g);

        double lat = latitude * Math.PI / 180;
        double cosHa = Math.Cos(Zenith * Math.PI / 180) / (Math.Cos(lat) * Math.Cos(decl)) - Math.Tan(lat) * Math.Tan(decl);
        if (cosHa > 1) return new SunTimes(0, 0, false, true);
        if (cosHa < -1) return new SunTimes(0, 0, true, false);

        double ha = Math.Acos(cosHa) * 180 / Math.PI;
        double rise = 720 - 4 * (longitude + ha) - eqTime + utcOffsetMinutes;
        double set = 720 - 4 * (longitude - ha) - eqTime + utcOffsetMinutes;
        rise = Wrap(rise);
        set = Wrap(set);
        if (set < rise) set += 1440;
        return new SunTimes(rise, set, false, false);
    }

    private static double Wrap(double m) => ((m % 1440) + 1440) % 1440;

    public static (bool IsDay, double Progress) Phase(SunTimes s, double minuteOfDay)
    {
        if (s.PolarDay) return (true, minuteOfDay / 1440.0);
        if (s.PolarNight) return (false, minuteOfDay / 1440.0);

        double m = minuteOfDay;
        if (m < s.RiseMinutes) m += 1440;
        double dayLen = s.SetMinutes - s.RiseMinutes;
        if (m < s.SetMinutes) return (true, Math.Clamp((m - s.RiseMinutes) / dayLen, 0, 1));
        return (false, Math.Clamp((m - s.SetMinutes) / (1440 - dayLen), 0, 1));
    }
}
