// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.Entities;

/// <summary>
/// Durable idempotency reservation for a one-shot enqueue. Its lifecycle is deliberately independent of the
/// reserved job row: completion, failure, cancellation, and retention deletion of the job never release an
/// unexpired reservation — only expiry (or an atomic replace after expiry) does. Uniqueness is the composite
/// primary key, so no permanent idempotency unique index exists on retained <c>TimeJobEntity</c> rows and no
/// nullable-tenant unique-index semantics are relied upon.
/// </summary>
/// <remarks>
/// Identity is <c>(ScopeKey, Function, ContractVersion, IdempotencyKey)</c>. <see cref="ScopeKey"/> is the
/// canonical non-null scope (<c>S</c> for system, <c>T:{tenant-id}</c> for tenant); <see cref="TenantId"/> is
/// stored separately for audit and querying only. The payload is intentionally not part of the identity:
/// idempotency here is an enqueue deduplication window, not exactly-once execution.
/// </remarks>
[PublicAPI]
public class JobIdempotencyReservationEntity
{
    /// <summary>Canonical non-null scope: <c>S</c> (system) or <c>T:{tenant-id}</c>.</summary>
    public virtual string ScopeKey { get; set; } = null!;

    /// <summary>Logical function name of the reserved enqueue.</summary>
    public virtual string Function { get; set; } = null!;

    /// <summary>Payload schema version of the reserved enqueue.</summary>
    public virtual string ContractVersion { get; set; } = null!;

    /// <summary>Caller-supplied idempotency key; ordinal, never trimmed or case-folded.</summary>
    public virtual string IdempotencyKey { get; set; } = null!;

    /// <summary>Nullable tenant ID stored for audit/querying; uniqueness lives on <see cref="ScopeKey"/>.</summary>
    public virtual string? TenantId { get; set; }

    /// <summary>The job identifier the reservation currently owns; may outlive the job row.</summary>
    public virtual Guid JobId { get; set; }

    /// <summary>UTC instant after which the reservation may be atomically replaced by a new enqueue.</summary>
    public virtual DateTime ExpiresAt { get; set; }

    /// <summary>UTC instant the reservation was first created.</summary>
    public virtual DateTime CreatedAt { get; set; }

    /// <summary>UTC instant the reservation was last written (creation or post-expiry replacement).</summary>
    public virtual DateTime UpdatedAt { get; set; }
}
