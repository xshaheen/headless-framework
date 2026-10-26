// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs;

/// <summary>
/// Per-job policy that decides what happens to a started run (an <c>InProgress</c> row) whose owning node died
/// or stopped renewing its lease. Applied by the dead-node sweep and the stalled-lease sweep. A claimed row that
/// never started (<c>Idle</c> or <c>Queued</c>) is reclaimable once its lease lapses under every policy, because
/// reclaiming it starts the job's first run.
/// </summary>
/// <remarks>
/// New members may be added in future versions; consumers assign this enum rather than
/// <see langword="switch"/>-ing on incoming values, so existing members and their explicit values remain stable.
/// </remarks>
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
    /// and stalled-lease sweeps set a started row to Skipped instead of re-running it.
    /// </summary>
    Skip = 2,
}
