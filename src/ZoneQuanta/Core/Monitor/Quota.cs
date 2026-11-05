using System;

namespace ZoneQuanta.Core.Monitor;

public sealed record QuotaReport(
    DateTime Start, DateTime End, long Cap, long Used, long Remaining, double Fraction,
    double DaysLeft, double DailyRate, double DailyBudget, long Projected, DateTime? ExhaustAt,
    long[] Days, int TodayIndex);

// Billing-cycle math for a monthly data cap.
public static class Quota
{
    private const int RateWindowDays = 14;
    private const double RateHalfLifeDays = 3;

    // The cycle starts on resetDay (clamped to the month's length) and runs one calendar month.
    public static (DateTime Start, DateTime End) Cycle(DateTime now, int resetDay)
    {
        resetDay = Math.Clamp(resetDay, 1, 31);
        DateTime At(DateTime month) => new(month.Year, month.Month, Math.Min(resetDay, DateTime.DaysInMonth(month.Year, month.Month)));
        var month = new DateTime(now.Year, now.Month, 1);
        DateTime start = now.Date >= At(month) ? At(month) : At(month.AddMonths(-1));
        return (start, At(new DateTime(start.Year, start.Month, 1).AddMonths(1)));
    }

    public static QuotaReport Compute(TrafficFile totals, long cap, int resetDay, DateTime now)
    {
        var (start, end) = Cycle(now, resetDay);
        int cycleDays = (int)Math.Round((end - start).TotalDays);
        DateTime first = now.Date.AddDays(-RateWindowDays) < start ? now.Date.AddDays(-RateWindowDays) : start;
        int span = (int)(now.Date - first).TotalDays + 1;
        long[] all = totals.Daily(TrafficFile.TotalKey, first, span);

        int offset = (int)(start - first).TotalDays;
        var days = new long[cycleDays];
        long used = 0;
        for (int i = 0; i < cycleDays && offset + i < span; i++)
        {
            days[i] = all[offset + i];
            used += days[i];
        }

        // Daily rate: exponentially weighted mean of complete past days (half-life 3 days), so a
        // recent change in habits shows up within days while one odd day does not swing the forecast.
        DateTime? firstData = totals.FirstDay(TrafficFile.TotalKey);
        double weighted = 0, weights = 0;
        for (int k = 1; k <= RateWindowDays; k++)
        {
            var day = now.Date.AddDays(-k);
            if (firstData is null || day <= firstData) break;
            double w = Math.Pow(0.5, (k - 1) / RateHalfLifeDays);
            weighted += w * all[span - 1 - k];
            weights += w;
        }
        double todayFraction = (now - now.Date).TotalDays;
        double rate = weights > 0 ? weighted / weights
            : todayFraction > 1.0 / 24 ? all[span - 1] / todayFraction : 0;

        double daysLeft = Math.Max(0, (end - now).TotalDays);
        long remaining = Math.Max(0, cap - used);
        long projected = used + (long)(rate * daysLeft);
        DateTime? exhaust = null;
        if (cap > used && rate > 0)
        {
            var at = now.AddDays((cap - used) / rate);
            if (at < end) exhaust = at;
        }
        double budget = remaining / Math.Max(daysLeft, 1);
        double fraction = cap > 0 ? (double)used / cap : 0;
        int todayIndex = (int)(now.Date - start).TotalDays;
        return new QuotaReport(start, end, cap, used, remaining, fraction, daysLeft, rate, budget, projected, exhaust, days, todayIndex);
    }
}
