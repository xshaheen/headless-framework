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
