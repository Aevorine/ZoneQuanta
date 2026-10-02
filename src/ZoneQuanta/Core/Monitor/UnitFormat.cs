using System;
using System.Globalization;

namespace ZoneQuanta.Core.Monitor;

public static class UnitFormat
{
    private static readonly string[] ByteUnits = { "B", "KB", "MB", "GB", "TB" };
    private static readonly string[] BitUnits = { "b", "Kb", "Mb", "Gb", "Tb" };

    public static string Speed(double bytesPerSec, bool bits, string unit)
    {
        var (v, u) = Scale(bytesPerSec, bits, unit);
        return $"{Number(v)} {u}/s";
    }

    public static string Size(double bytes, bool bits, string unit)
    {
        var (v, u) = Scale(bytes, bits, unit);
        return $"{Number(v)} {u}";
    }

    private static (double, string) Scale(double bytes, bool bits, string unit)
    {
        double value = bits ? bytes * 8 : bytes;
        double step = bits ? 1000 : 1024;
        string[] names = bits ? BitUnits : ByteUnits;

        int index = unit switch { "K" => 1, "M" => 2, "G" => 3, _ => -1 };
        if (index < 0)
        {
            index = 0;
            double v = value;
            while (v >= 1000 && index < names.Length - 1)
            {
                v /= step;
                index++;
            }
        }
        return (value / Math.Pow(step, index), names[index]);
    }

    private static string Number(double v) =>
        v >= 100 ? v.ToString("0", CultureInfo.InvariantCulture)
        : v >= 10 ? v.ToString("0.0", CultureInfo.InvariantCulture)
        : v.ToString("0.00", CultureInfo.InvariantCulture);
}
