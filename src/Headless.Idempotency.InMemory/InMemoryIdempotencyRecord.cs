// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Fencing;

namespace Headless.Idempotency.InMemory;

/// <summary>
/// One stored idempotency record. Immutable, and built only from immutable parts (the fingerprint, result, and recovery point
/// copy their bytes), so a change replaces the row and a reader always holds a consistent snapshot.
/// </summary>
/// <param name="Lease">
/// The record's row lease, moved only through <see cref="RowLeaseRules" />, as the relational providers move theirs
/// through the fenced-row columns. A completed record is one whose lease is settled.
/// </param>
/// <param name="Fingerprint">The fingerprint of the request that owns the key.</param>
/// <param name="Result">The stored result when completed; otherwise <see langword="null" />.</param>
/// <param name="RetentionUntil">When the record's retention ends, by the store's clock.</param>
/// <param name="RecoveryPoint">The last recovery point an attempt recorded, or <see langword="null" /> when none is kept.</param>
internal sealed record InMemoryIdempotencyRecord(
    RowLeaseSnapshot Lease,
    IdempotencyFingerprint Fingerprint,
    IdempotentResult? Result,
    DateTimeOffset RetentionUntil,
    IdempotentRecoveryPoint? RecoveryPoint = null
);
