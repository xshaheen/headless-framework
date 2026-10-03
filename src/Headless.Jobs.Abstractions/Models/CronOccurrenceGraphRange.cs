// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Enums;

namespace Headless.Jobs.Models;

internal readonly record struct CronOccurrenceGraphRange(DateTime StartDate, DateTime EndDate);

internal static class CronOccurrenceGraphRangeSelector
{
    public const int MaxTotalDays = 14;

    public static CronOccurrenceGraphRange Select(IEnumerable<DateTime> occurrenceDates, DateTime today)
    {
        today = today.Date;
        var dates = occurrenceDates.Select(x => x.Date).Distinct().ToArray();
        var pastDates = dates.Where(x => x < today).Order().ToArray();
        var futureDates = dates.Where(x => x > today).Order().ToArray();

        const int remainingSlots = MaxTotalDays - 1;
        var emptyPastSlots = Math.Max(0, (remainingSlots - futureDates.Length) / 2);
        var emptyFutureSlots = Math.Max(0, remainingSlots - pastDates.Length - emptyPastSlots);

        var firstPastDate = pastDates.FirstOrDefault(today.AddDays(-1));
        var lastFutureDate = futureDates.LastOrDefault(today.AddDays(1));

        var selectedDates = Enumerable
            .Range(1, emptyPastSlots)
            .Select(offset => firstPastDate.AddDays(-offset))
            .Concat(pastDates)
            .Append(today)
            .Concat(futureDates)
            .Concat(Enumerable.Range(1, emptyFutureSlots).Select(offset => lastFutureDate.AddDays(offset)))
            .Order()
            .Take(MaxTotalDays)
            .ToArray();

        return new CronOccurrenceGraphRange(selectedDates[0], selectedDates[^1]);
    }

    public static CronOccurrenceStatusCount[] AddRangeBoundaries(
        IEnumerable<CronOccurrenceStatusCount> counts,
        CronOccurrenceGraphRange range
    )
    {
        return
        [
            .. counts,
            new CronOccurrenceStatusCount { Date = range.StartDate, IsRangeBoundary = true },
            new CronOccurrenceStatusCount { Date = range.EndDate, IsRangeBoundary = true },
        ];
    }
}
