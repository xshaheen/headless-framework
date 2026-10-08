// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Fencing;

/// <summary>The outcome of a grant.</summary>
[PublicAPI]
public enum LeaseGrantStatus
{
    /// <summary>
    /// The lease had no live holder (it never existed, or its last attempt ended), and the caller now holds a new
    /// generation.
    /// </summary>
    Granted = 0,

    /// <summary>A live attempt holds the lease; the caller got nothing.</summary>
    Held = 1,

    /// <summary>
    /// The previous attempt's lease expired without ending, and the caller took it over with a new generation. The
    /// previous attempt's later writes are refused.
    /// </summary>
    Takeover = 2,

    /// <summary>
    /// The previous attempt's lease expired without ending, and the grant asked for
    /// <see cref="LeaseTakeover.AfterSweep" />, so the caller got nothing. The lease becomes grantable once a sweep
    /// abandons the expired attempt, or once it is settled or released.
    /// </summary>
    Expired = 3,
}
