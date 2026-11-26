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
/// <param name="Key">The record's identity: the tenant it belongs to and the idempotency key.</param>
/// <param name="Status">
/// <see cref="IdempotencyPeekStatus.Pending" /> while an admitted attempt holds a live lease,
/// <see cref="IdempotencyPeekStatus.Completed" /> while a stored result replays, otherwise
/// <see cref="IdempotencyPeekStatus.Released" /> (the attempt released the key, its lease expired, or no attempt was
/// admitted yet). Never <see cref="IdempotencyPeekStatus.Absent" />: an absent record has no snapshot.
/// </param>
/// <param name="Generation">
/// The generation of the last admitted attempt, kept after it completed or released; <see langword="null" /> when no
/// attempt was ever admitted on the record.
/// </param>
/// <param name="TakeoverCount">
/// How many admissions took the key over from an attempt whose lease had expired since an attempt last completed or
/// released it, so a pending record with a count above zero is running after a stall; a completion or release resets
/// it to zero. <see langword="null" /> when the provider does not count takeovers (the cache provider).
/// </param>
/// <param name="Fingerprint">The fingerprint of the request that owns the key.</param>
/// <param name="LeaseExpiresAt">
/// When the admitted attempt's lease ends, while an attempt holds the lease, live or expired; <see langword="null" />
/// once it completed or released, and before any admission.
/// </param>
/// <param name="IsLeaseExpired">
/// Whether an attempt holds the lease but <see cref="LeaseExpiresAt" /> is at or before <see cref="ObservedAt" />: the
/// attempt stalled, and the next admission takes the key over.
/// </param>
/// <param name="RetentionUntil">When the record stops replaying and becomes eligible for the purge.</param>
/// <param name="ObservedAt">The store's clock when it read the record.</param>
/// <param name="ResultContract">The stored result's contract tag; <see langword="null" /> unless completed.</param>
/// <param name="ResultSizeBytes">The stored result's size in bytes; <see langword="null" /> unless completed.</param>
/// <param name="RecoveryPoint">
/// The name of the last recovery point an attempt recorded; <see langword="null" /> when none is kept.
/// </param>
/// <param name="RecoveryContract">The recovery state's contract tag; <see langword="null" /> when none is kept.</param>
/// <param name="RecoveryStateSizeBytes">
/// The recovery state's size in bytes; <see langword="null" /> when no recovery point is kept.
/// </param>
[PublicAPI]
public sealed record IdempotencyRecordSnapshot(
    IdempotencyKey Key,
    IdempotencyPeekStatus Status,
    long? Generation,
    int? TakeoverCount,
    IdempotencyFingerprint Fingerprint,
    DateTimeOffset? LeaseExpiresAt,
    bool IsLeaseExpired,
    DateTimeOffset RetentionUntil,
    DateTimeOffset ObservedAt,
    string? ResultContract,
    long? ResultSizeBytes,
    string? RecoveryPoint,
    string? RecoveryContract,
    long? RecoveryStateSizeBytes
);
