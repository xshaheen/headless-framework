// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs;

/// <summary>Why a running job stopped owning its row, as the lease-loss warning reports it.</summary>
internal enum JobsLeaseLossReason
{
    /// <summary>The lease renewal matched no row: a reclaim, another owner, or a terminal write took it.</summary>
    RenewalUnmatched = 0,

    /// <summary>The store did not confirm the renewal within one renewal interval.</summary>
    RenewalTimedOut = 1,

    /// <summary>The renewal write threw, so the node can no longer vouch for the lease.</summary>
    RenewalFailed = 2,

    /// <summary>The node lost its membership, so its identity no longer owns any row.</summary>
    MembershipLost = 3,

    /// <summary>The cancellation check found the row absent, reclaimed, terminal, or owned by another node.</summary>
    RowNotOwned = 4,

    /// <summary>The write that charges a failed attempt before an in-process retry matched no row.</summary>
    RetryWriteUnmatched = 5,
}
