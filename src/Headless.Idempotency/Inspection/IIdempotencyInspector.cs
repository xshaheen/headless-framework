// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Idempotency;

/// <summary>
/// Operator reads of the idempotency store: one key's metadata and the store's configured defaults. Every read is
/// lock-free, never admits, never changes a record, and never returns a stored result or recovery-state bytes.
/// </summary>
/// <remarks>
/// Releasing, expiring, or deleting a record has no operator API: releasing a live attempt could run its operation
/// twice. Call <see cref="IIdempotentOperations.GetResultAsync" /> only when the stored body itself is needed.
/// </remarks>
[PublicAPI]
public interface IIdempotencyInspector
{
    /// <summary>
    /// Reads what the store holds for <paramref name="key" /> in the current tenant (state, generation, lease,
    /// fingerprint, retention, and the size and contract of its result and recovery point), with the same lock-free
    /// read <see cref="IIdempotentOperations.PeekAsync" /> takes.
    /// </summary>
    /// <param name="key">The idempotency key.</param>
    /// <param name="cancellationToken">Token used to cancel the store call.</param>
    /// <returns>
    /// The record's snapshot; <see langword="null" /> when no record exists for the key in the current tenant, or its
    /// retention elapsed and no live attempt holds it. A live attempt keeps its snapshot past retention, because the
    /// record is kept until that attempt ends.
    /// </returns>
    /// <exception cref="ArgumentException">The key or current tenant id is invalid.</exception>
    ValueTask<IdempotencyRecordSnapshot?> InspectAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Describes the store this host runs on and the configured admission and purge defaults, without I/O.</summary>
    /// <returns>The provider, where its records live, and the current defaults.</returns>
    IdempotencyStoreDescription Describe();
}
