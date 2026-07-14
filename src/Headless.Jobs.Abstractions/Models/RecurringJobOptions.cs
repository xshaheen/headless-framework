// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Enums;

namespace Headless.Jobs.Models;

/// <summary>Persistence-backed options for recurring job definitions and their occurrences.</summary>
/// <remarks>
/// Priority is generated from <c>[JobFunction]</c> metadata and is intentionally not a per-definition option.
/// </remarks>
[PublicAPI]
public sealed record RecurringJobOptions
{
    /// <summary>Optional human-readable description displayed by operational tooling.</summary>
    public string? Description { get; init; }

    /// <summary>Maximum number of durable retry attempts for each occurrence.</summary>
    public int Retries { get; init; }

    /// <summary>Optional per-retry delay intervals in seconds.</summary>
    public int[]? RetryIntervals { get; init; }

    /// <summary>Policy applied when the node executing an occurrence dies.</summary>
    public NodeDeathPolicy OnNodeDeath { get; init; } = NodeDeathPolicy.Retry;
}
