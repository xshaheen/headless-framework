// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Idempotency;

/// <summary>
/// What the store holds for one idempotency key, read without a row lock: its state, lease, fingerprint, retention,
/// and the size and contract of its stored result and recovery point. It never carries the result or recovery-state
/// bytes; read the result with <see cref="IIdempotentOperations.GetResultAsync" /> when it is needed.
/// </summary>
/// <remarks>
/// Every instant and every time judgement comes from the store's clock (the database clock on a relational provider),
/// read in the same statement as the row, so compare <see cref="LeaseExpiresAt" /> and <see cref="RetentionUntil" />
/// with <see cref="ObservedAt" />, never with the caller's clock. A field the provider does not store is
/// <see langword="null" />.
/// </remarks>
[PublicAPI]
public sealed record IdempotencyRecordSnapshot
{
    /// <summary>Gets the record's identity: the tenant it belongs to and the idempotency key.</summary>
    public required IdempotencyKey Key { get; init; }

    /// <summary>
    /// Gets <see cref="IdempotencyPeekStatus.Pending" /> while an admitted attempt holds a live lease,
    /// <see cref="IdempotencyPeekStatus.Completed" /> while a stored result replays, otherwise
    /// <see cref="IdempotencyPeekStatus.Released" /> (the attempt released the key, its lease expired, or no attempt
    /// was admitted yet). Never <see cref="IdempotencyPeekStatus.Absent" />: an absent record has no snapshot.
    /// </summary>
    public required IdempotencyPeekStatus Status { get; init; }

    /// <summary>
    /// Gets the generation of the last admitted attempt, kept after it completed or released; <see langword="null" />
    /// when no attempt was ever admitted on the record.
    /// </summary>
    public required long? Generation { get; init; }

    /// <summary>
    /// Gets how many admissions took the key over from an attempt whose lease had expired since an attempt last
    /// completed or released it, so a pending record with a count above zero is running after a stall; a completion or
    /// release resets it to zero. <see langword="null" /> when the provider does not count takeovers (the cache
    /// provider).
    /// </summary>
    public required int? TakeoverCount { get; init; }

    /// <summary>Gets the fingerprint of the request that owns the key.</summary>
    public required IdempotencyFingerprint Fingerprint { get; init; }

    /// <summary>
    /// Gets when the admitted attempt's lease ends, while an attempt holds the lease, live or expired;
    /// <see langword="null" /> once it completed or released, and before any admission.
    /// </summary>
    public required DateTimeOffset? LeaseExpiresAt { get; init; }

    /// <summary>
    /// Gets whether an attempt holds the lease but <see cref="LeaseExpiresAt" /> is at or before
    /// <see cref="ObservedAt" />: the attempt stalled, and the next admission takes the key over.
    /// </summary>
    public required bool IsLeaseExpired { get; init; }

    /// <summary>
    /// Gets when the record stops replaying and becomes eligible for the purge. A live attempt keeps its record past
    /// this instant, so a <see cref="IdempotencyPeekStatus.Pending" /> snapshot can show it in the past.
    /// </summary>
    public required DateTimeOffset RetentionUntil { get; init; }

    /// <summary>Gets the store's clock when it read the record.</summary>
    public required DateTimeOffset ObservedAt { get; init; }

    /// <summary>Gets the stored result's contract tag; <see langword="null" /> unless completed.</summary>
    public string? ResultContract { get; init; }

    /// <summary>Gets the stored result's size in bytes; <see langword="null" /> unless completed.</summary>
    public long? ResultSizeBytes { get; init; }

    /// <summary>Gets the name of the last recovery point an attempt recorded; <see langword="null" /> when none is kept.</summary>
    public string? RecoveryPoint { get; init; }

    /// <summary>Gets the recovery state's contract tag; <see langword="null" /> when none is kept.</summary>
    public string? RecoveryContract { get; init; }

    /// <summary>Gets the recovery state's size in bytes; <see langword="null" /> when no recovery point is kept.</summary>
    public long? RecoveryStateSizeBytes { get; init; }
}
