// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.Enums;

/// <summary>
/// Per-job policy that decides what happens to a row the owning node was mid-execution on when that node
/// dies. Applied by the dead-node sweep (terminal transitions) and by the claim predicate's lease-expiry arm
/// (only <see cref="Retry"/> rows are speculatively re-claimable once their lease expires).
/// </summary>
[PublicAPI]
public enum NodeDeathPolicy
{
    /// <summary>
    /// Default. The row is reclaimable: a dead node's in-flight row is released for re-claim and counts toward
    /// the retry budget. Safe only for idempotent jobs — a still-running job whose lease expires may be
    /// speculatively re-claimed and run again.
    /// </summary>
    Retry = 0,

    /// <summary>Terminal failure on the first node death — no retry. The dead-node sweep sets the row to Failed.</summary>
    MarkFailed = 1,

    /// <summary>
    /// Terminal Skipped on node death, for idempotency-critical jobs that must never run twice. The dead-node
    /// sweep sets the row to Skipped; the lease-expiry arm never re-claims it.
    /// </summary>
    Skip = 2,
}
