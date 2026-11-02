// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Idempotency;

/// <summary>
/// A cheap, lock-free read of an idempotency record's status for the current tenant, without touching its lease.
/// </summary>
[PublicAPI]
public enum IdempotencyPeekStatus
{
    /// <summary>No record exists for the key, or its retention already elapsed.</summary>
    Absent = 0,

    /// <summary>A record exists and is not completed, and an admitted attempt holds it under a live lease.</summary>
    Pending = 1,

    /// <summary>A record exists with a stored result that still replays.</summary>
    Completed = 2,

    /// <summary>
    /// A record exists and is not completed, but no attempt holds a live lease on it: the last attempt released it, or
    /// its lease expired. The next admission with the same fingerprint takes the key.
    /// </summary>
    Released = 3,
}
