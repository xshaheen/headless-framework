// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Idempotency;

/// <summary>
/// A locked idempotency record as a provider read it, inside the transaction that holds its row lock.
/// </summary>
/// <param name="Inserted">
/// Whether the row did not exist and was inserted by this call as <see cref="IdempotencyRecordStatus.Pending" /> with
/// the caller's fingerprint and no lease generation.
/// </param>
/// <param name="Status">The record's state.</param>
/// <param name="Fingerprint">
/// The stored fingerprint. Built from the stored algorithm tag as is, even when this version does not know it.
/// </param>
/// <param name="LeaseGeneration">
/// The generation of the last admitted attempt's lease, or <see langword="null" /> when no attempt holds the record
/// (just inserted, or released).
/// </param>
/// <param name="Result">The stored result when <paramref name="Status" /> is completed; otherwise <see langword="null" />.</param>
/// <param name="RetentionUntil">When the record's retention ends.</param>
/// <param name="IsRetentionElapsed">
/// Whether <paramref name="RetentionUntil" /> is at or before the database clock, read in the statement that locked the
/// row. The application clock never decides it.
/// </param>
[PublicAPI]
public sealed record IdempotencyRecordState(
    bool Inserted,
    IdempotencyRecordStatus Status,
    IdempotencyFingerprint Fingerprint,
    long? LeaseGeneration,
    IdempotentResult? Result,
    DateTimeOffset RetentionUntil,
    bool IsRetentionElapsed
);
