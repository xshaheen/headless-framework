// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Idempotency;

/// <summary>
/// What the store found for an admitted attempt, named by its generation, when it renewed, released, fenced, or
/// completed it.
/// </summary>
[PublicAPI]
public enum IdempotentLeaseStatus
{
    /// <summary>
    /// The attempt still owns the key: its generation is the record's, the record is pending, and its lease has not
    /// expired by the database clock.
    /// </summary>
    Current = 0,

    /// <summary>
    /// The generation is still the record's, but its lease expired: the next admission of the key takes the operation
    /// over at any moment.
    /// </summary>
    Expired = 1,

    /// <summary>
    /// A later admission replaced this attempt, or the record no longer exists. An attempt that a newer one replaced
    /// reads stale even after that newer attempt completed or released the key.
    /// </summary>
    Stale = 2,

    /// <summary>This attempt already completed the operation; its result is stored.</summary>
    Completed = 3,

    /// <summary>The record is pending with no admitted attempt: this attempt released the key.</summary>
    Released = 4,
}
