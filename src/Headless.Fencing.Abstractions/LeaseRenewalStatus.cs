// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Fencing;

/// <summary>The outcome of renewing a lease by its generation.</summary>
[PublicAPI]
public enum LeaseRenewalStatus
{
    /// <summary>The generation is current and the lease was live; its expiry moved forward.</summary>
    Renewed = 0,

    /// <summary>
    /// The generation is current but the lease already expired, so it was not extended: another grant may take it
    /// over, or a sweep may abandon it, at any moment.
    /// </summary>
    Expired = 1,

    /// <summary>A newer grant replaced this generation, or the lease no longer exists.</summary>
    Stale = 2,

    /// <summary>This generation's attempt already settled.</summary>
    Settled = 3,

    /// <summary>This generation's attempt was released.</summary>
    Released = 4,

    /// <summary>A sweep abandoned this generation's expired attempt.</summary>
    Abandoned = 5,
}
