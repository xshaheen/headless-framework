// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs;

/// <summary>
/// Why a run stopped owning its row. The lease-loss warning names it, and the <c>lease_lost</c> outcome carries it as
/// <c>headless.jobs.lease_loss.reason</c>.
/// </summary>
internal enum JobsLeaseLossReason
{
    /// <summary>The lease renewal matched no row: a reclaim, another owner, or a terminal write took it.</summary>
    RenewalUnmatched = 0,

    /// <summary>The store did not confirm the renewal within one renewal interval.</summary>
    RenewalTimedOut = 1,

    /// <summary>The renewal write threw, so the node can no longer vouch for the lease.</summary>
    RenewalFailed = 2,

    /// <summary>The node lost its membership, or has no live identity, so it owns no row.</summary>
    MembershipLost = 3,

    /// <summary>The cancellation check found the row absent, reclaimed, terminal, or owned by another node.</summary>
    RowNotOwned = 4,

    /// <summary>The write that charges a failed attempt before a retry matched no row.</summary>
    RetryWriteUnmatched = 5,

    /// <summary>The terminal write matched no row: a reclaim stored its own status first.</summary>
    CompletionRejected = 6,

    /// <summary>
    /// A write handing the row back (a drain, a graceful stop, an unregistered contract version, unknown membership)
    /// matched no row.
    /// </summary>
    ReleaseUnmatched = 7,

    /// <summary>A write handing the row back threw, so the node cannot tell whether the row is still its own.</summary>
    ReleaseFailed = 8,

    /// <summary>The write that starts a chain child matched no row: the child's row was no longer the claim's to start.</summary>
    StartRejected = 9,
}
