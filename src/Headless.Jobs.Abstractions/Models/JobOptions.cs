// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Enums;

namespace Headless.Jobs.Models;

/// <summary>Persistence-backed options for immediate and delayed one-shot jobs.</summary>
/// <remarks>
/// Priority is generated from <c>[JobFunction]</c> metadata and is intentionally not a per-enqueue option.
/// </remarks>
[PublicAPI]
public sealed record JobOptions
{
    /// <summary>Fails before scheduling effects unless a compatible live relational transaction can enlist the job write.</summary>
    public bool RequireAtomicEnlistment { get; init; }

    /// <summary>Root business correlation; defaults to the executing parent's correlation or the new row ID.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>Immediate business cause; defaults to the currently executing parent row ID.</summary>
    public string? CausationId { get; init; }

    /// <summary>Optional human-readable description displayed by operational tooling.</summary>
    public string? Description { get; init; }

    /// <summary>Maximum durable retries; null inherits configured defaults and zero disables retries.</summary>
    public int? Retries { get; init; }

    /// <summary>Optional per-retry delay intervals in seconds.</summary>
    public int[]? RetryIntervals { get; init; }

    /// <summary>Policy applied when the node executing the job dies; null inherits configured defaults.</summary>
    public NodeDeathPolicy? OnNodeDeath { get; init; }

    /// <summary>
    /// Explicit tenant to stamp on the scheduled job; wins over ambient capture. <see langword="null"/> defers to
    /// ambient capture when tenant propagation is enabled.
    /// </summary>
    public string? TenantId { get; init; }

    /// <summary>
    /// Marks a deliberate system-scope (tenantless) job that bypasses the tenant-required check. Rejected when an
    /// ambient tenant is present or an explicit <see cref="TenantId"/> is supplied.
    /// </summary>
    public bool IsSystemJob { get; init; }
}
