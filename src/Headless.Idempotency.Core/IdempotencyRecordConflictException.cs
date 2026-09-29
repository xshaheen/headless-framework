// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;

namespace Headless.Idempotency;

/// <summary>
/// Thrown by an optimistic <see cref="IIdempotencyRecordStore" /> when another caller changed the record between this
/// call's read and its write, so the write was not applied.
/// </summary>
/// <remarks>
/// A locking provider never throws it: its row lock keeps the record unchanged from read to write. An optimistic
/// provider (one that checks at write time with a compare-and-swap instead of locking at read time) throws it from a
/// write verb, and the autonomous <see cref="IIdempotentOperations" /> calls run again in a fresh owned unit, deciding
/// from the record as it now is. The retry is bounded; the last conflict propagates to the caller.
/// </remarks>
/// <param name="key">The record whose write lost the race.</param>
[PublicAPI]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class IdempotencyRecordConflictException(IdempotencyRecordKey key)
    : InvalidOperationException(
        $"The idempotency record for key '{key.Key}' changed while this call decided on it, so its write was not "
            + "applied; the call can be retried from a fresh read."
    )
{
    /// <summary>Gets the record whose write lost the race.</summary>
    public IdempotencyRecordKey Key { get; } = key;
}
