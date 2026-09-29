// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Idempotency;

/// <summary>The stored lifecycle state of an idempotency record.</summary>
[PublicAPI]
public enum IdempotencyRecordStatus
{
    /// <summary>
    /// No result is stored: the key was just inserted, an attempt is admitted or ended without a result, or it was
    /// released.
    /// </summary>
    Pending = 0,

    /// <summary>A result is stored and replays until the record's retention ends.</summary>
    Completed = 1,
}
