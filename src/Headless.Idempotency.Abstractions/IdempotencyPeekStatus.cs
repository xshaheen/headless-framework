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

    /// <summary>A record exists and is not completed: no attempt holds it, or one is admitted or in flight.</summary>
    Pending = 1,

    /// <summary>A record exists with a stored result that still replays.</summary>
    Completed = 2,
}
