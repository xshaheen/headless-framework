// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.DashboardDtos;

/// <summary>
/// Per-day summary of time job outcomes, used to populate the dashboard execution graph.
/// Each result tuple carries the succeeded and failed job counts for that day.
/// </summary>
public class JobGraphData
{
    /// <summary>The UTC date this summary covers.</summary>
    public DateTime Date { get; set; }

    /// <summary>
    /// Succeeded/failed pair for each job type on this date.
    /// Item1 = succeeded count, Item2 = failed count.
    /// </summary>
    public required Tuple<int, int>[] Results { get; set; }
}
