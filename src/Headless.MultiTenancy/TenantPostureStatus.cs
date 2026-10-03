// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.MultiTenancy;

/// <summary>Common tenant posture status labels, ordered weakest to strongest.</summary>
/// <remarks>
/// Declaration order is load-bearing: the ordinal IS the posture precedence
/// (<c>Configured &lt; Propagating &lt; Guarded &lt; Enforcing</c>), which
/// <see cref="TenantPostureManifest.RecordSeam"/> relies on so a later contribution can only
/// strengthen a seam's posture. Keep new members in precedence order.
/// </remarks>
[PublicAPI]
public enum TenantPostureStatus
{
    /// <summary>The seam has been configured.</summary>
    Configured = 0,

    /// <summary>The seam propagates tenant context.</summary>
    Propagating = 1,

    /// <summary>The seam guards tenant-owned reads or writes; its capability labels name which.</summary>
    Guarded = 2,

    /// <summary>The seam enforces tenant context.</summary>
    Enforcing = 3,
}
