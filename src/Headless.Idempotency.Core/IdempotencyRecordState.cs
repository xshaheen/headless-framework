// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Idempotency;

/// <summary>
/// A locked idempotency record as a provider read it, inside the transaction that holds its row lock.
/// </summary>
/// <param name="Inserted">
/// Whether the row did not exist and was inserted by this call as <see cref="IdempotencyRecordStatus.Pending" /> with
/// the caller's fingerprint and no generation.
/// </param>
/// <param name="Status">The record's state.</param>
/// <param name="Fingerprint">
/// The stored fingerprint. Built from the stored algorithm tag as is, even when this version does not know it.
/// </param>
/// <param name="Generation">
/// The generation of the last admitted attempt, or <see langword="null" /> when no attempt holds the record (just
/// inserted, or released). A completed record keeps the generation that completed it.
/// </param>
/// <param name="LeaseExpiresAt">
/// When the admitted attempt's lease expires, or <see langword="null" /> when no attempt holds the record (just
/// inserted, released, or completed).
/// </param>
/// <param name="Result">The stored result when <paramref name="Status" /> is completed; otherwise <see langword="null" />.</param>
/// <param name="RetentionUntil">When the record's retention ends.</param>
/// <param name="IsRetentionElapsed">
/// Whether <paramref name="RetentionUntil" /> is at or before the database clock, read after the row lock was held. The
/// application clock never decides it.
/// </param>
/// <param name="IsLeaseLive">
/// Whether <paramref name="LeaseExpiresAt" /> is after the database clock read with <paramref name="IsRetentionElapsed" />;
/// <see langword="false" /> when there is no lease.
/// </param>
[PublicAPI]
public sealed record IdempotencyRecordState(
    bool Inserted,
    IdempotencyRecordStatus Status,
    IdempotencyFingerprint Fingerprint,
    long? Generation,
    DateTimeOffset? LeaseExpiresAt,
    IdempotentResult? Result,
    DateTimeOffset RetentionUntil,
    bool IsRetentionElapsed,
    bool IsLeaseLive
)
{
    /// <summary>
    /// Gets whether a live attempt holds the record: it is pending, names a generation, and that attempt's lease has
    /// not expired.
    /// </summary>
    public bool IsHeld => Status == IdempotencyRecordStatus.Pending && Generation is not null && IsLeaseLive;

    /// <summary>Classifies what this record means for the attempt that holds <paramref name="generation" />.</summary>
    /// <param name="generation">The attempt's generation.</param>
    /// <returns>What the attempt finds; <see cref="IdempotentLeaseStatus.Current" /> only when it still owns the key.</returns>
    public IdempotentLeaseStatus ClassifyFor(long generation)
    {
        return IdempotencyLeaseClassifier.Classify(Status, Generation, IsLeaseLive, generation);
    }
}
