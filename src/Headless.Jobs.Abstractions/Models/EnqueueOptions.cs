// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Enums;

namespace Headless.Jobs.Models;

/// <summary>Persistence-backed options for immediate and delayed one-shot jobs.</summary>
/// <remarks>
/// Priority is generated from <c>[JobFunction]</c> metadata and is intentionally not a per-enqueue option.
/// </remarks>
[PublicAPI]
public sealed record EnqueueOptions
{
    /// <summary>Optional human-readable description displayed by operational tooling.</summary>
    public string? Description { get; init; }

    /// <summary>Maximum number of durable retry attempts. <c>0</c> means no retries.</summary>
    public int Retries { get; init; }

    /// <summary>Optional per-retry delay intervals in seconds.</summary>
    public int[]? RetryIntervals { get; init; }

    /// <summary>Policy applied when the node executing the job dies.</summary>
    public NodeDeathPolicy OnNodeDeath { get; init; } = NodeDeathPolicy.Retry;
}
