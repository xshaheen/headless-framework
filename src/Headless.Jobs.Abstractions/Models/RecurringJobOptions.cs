// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Enums;
using Headless.UnitOfWork;

namespace Headless.Jobs.Models;

/// <summary>Persistence-backed options for recurring job definitions and their occurrences.</summary>
/// <remarks>
/// Priority is generated from <c>[JobFunction]</c> metadata and is intentionally not a per-definition option.
/// </remarks>
[PublicAPI]
public sealed record RecurringJobOptions
{
    /// <summary>
    /// How eagerly this definition write enlists in the active unit of work. Either this call value or the
    /// function policy can require it; the host default is never applied to recurring definitions.
    /// </summary>
    public TransactionEnlistment Enlistment { get; init; }

    /// <summary>Optional root business correlation captured by each materialized occurrence.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>Optional immediate business cause captured by each materialized occurrence.</summary>
    public string? CausationId { get; init; }

    /// <summary>Optional IANA timezone identifier. A <see langword="null"/> value uses the scheduler-global timezone.</summary>
    public string? TimeZoneId { get; init; }

    /// <summary>Optional human-readable description displayed by operational tooling.</summary>
    public string? Description { get; init; }

    /// <summary>Maximum durable retries per occurrence; null inherits configured defaults.</summary>
    public int? Retries { get; init; }

    /// <summary>Optional per-retry delay intervals in seconds.</summary>
    public int[]? RetryIntervals { get; init; }

    /// <summary>Policy applied when the node executing an occurrence dies; null inherits configured defaults.</summary>
    public NodeDeathPolicy? OnNodeDeath { get; init; }
}
