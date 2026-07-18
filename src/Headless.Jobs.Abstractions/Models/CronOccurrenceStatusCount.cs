// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Enums;

namespace Headless.Jobs.Models;

/// <summary>
/// Storage projection used by the Jobs dashboard cron-occurrence graph. Regular entries carry a UTC date,
/// lifecycle status, and count; range-boundary entries identify the exact inclusive graph window without
/// requiring providers to materialize occurrence entities for empty dates.
/// </summary>
[PublicAPI]
public sealed record CronOccurrenceStatusCount
{
    /// <summary>The UTC calendar date represented by this entry.</summary>
    public required DateTime Date { get; init; }

    /// <summary>The lifecycle status counted by this entry.</summary>
    public JobStatus Status { get; init; }

    /// <summary>Number of occurrences with <see cref="Status"/> on <see cref="Date"/>.</summary>
    public int Count { get; init; }

    /// <summary>
    /// <see langword="true"/> when this entry only marks an inclusive graph-range boundary. Boundary entries
    /// have a zero <see cref="Count"/> and their <see cref="Status"/> value must be ignored.
    /// </summary>
    public bool IsRangeBoundary { get; init; }
}

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
        return counts
            .Append(new CronOccurrenceStatusCount { Date = range.StartDate, IsRangeBoundary = true })
            .Append(new CronOccurrenceStatusCount { Date = range.EndDate, IsRangeBoundary = true })
            .ToArray();
    }
}
