// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.DashboardDtos;

/// <summary>
/// Per-day summary of time job outcomes, used to populate the dashboard execution graph.
/// Each entry in <see cref="Results"/> carries the execution count for one lifecycle status on that day.
/// </summary>
[PublicAPI]
public sealed class JobGraphData
{
    /// <summary>The UTC date this summary covers.</summary>
    public required DateTime Date { get; init; }

    /// <summary>Per-status execution counts on this date. Empty when the date has no executions.</summary>
    public required JobStatusCount[] Results { get; init; }
}
