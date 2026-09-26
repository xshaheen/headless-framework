// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Idempotency.InMemory;

/// <summary>
/// One stored idempotency record. Immutable, and built only from immutable parts (the fingerprint and result copy their
/// bytes), so a change replaces the row and a reader always holds a consistent snapshot.
/// </summary>
/// <param name="Status">The record's state.</param>
/// <param name="Fingerprint">The fingerprint of the request that owns the key.</param>
/// <param name="Generation">The last admitted attempt's generation, or <see langword="null" /> when no attempt holds it.</param>
/// <param name="LeaseExpiresAt">When the admitted attempt's lease expires, or <see langword="null" /> without a lease.</param>
/// <param name="Result">The stored result when completed; otherwise <see langword="null" />.</param>
/// <param name="RetentionUntil">When the record's retention ends, by the store's clock.</param>
internal sealed record InMemoryIdempotencyRecord(
    IdempotencyRecordStatus Status,
    IdempotencyFingerprint Fingerprint,
    long? Generation,
    DateTimeOffset? LeaseExpiresAt,
    IdempotentResult? Result,
    DateTimeOffset RetentionUntil
);
