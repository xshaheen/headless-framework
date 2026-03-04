// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Aggregated execution count for a given UTC date and status,
/// used by dashboard graph endpoints.
/// </summary>
public sealed class ExecutionStatusCount
{
    /// <summary>The UTC date (time component is midnight).</summary>
    public required DateTimeOffset Date { get; init; }

    /// <summary>The execution status name (e.g. "Succeeded", "Failed").</summary>
    public required string Status { get; init; }

    /// <summary>Number of executions with this status on this date.</summary>
    public required int Count { get; init; }
}
