// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Fencing;

/// <summary>What a fence read found for a generation.</summary>
[PublicAPI]
public enum LeaseFenceStatus
{
    /// <summary>The generation is current, the lease is active, and it has not expired: the write may proceed.</summary>
    Current = 0,

    /// <summary>A newer grant replaced this generation, or the lease no longer exists.</summary>
    Stale = 1,

    /// <summary>The generation is current but the lease expired.</summary>
    Expired = 2,

    /// <summary>This generation's attempt already settled.</summary>
    Settled = 3,

    /// <summary>This generation's attempt was released.</summary>
    Released = 4,

    /// <summary>A sweep abandoned this generation's expired attempt.</summary>
    Abandoned = 5,
}
