// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.EntityFramework;

/// <summary>Options for the opt-in EF tenant write guard.</summary>
[PublicAPI]
public sealed class TenantWriteGuardOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether tenant-owned writes require an ambient tenant
    /// unless a scoped bypass is active.
    /// </summary>
    public bool IsEnabled { get; set; }
}
