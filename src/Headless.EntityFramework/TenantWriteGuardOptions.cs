// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.EntityFramework;

/// <summary>Reports the EF tenant write guard state configured by <c>GuardTenantWrites()</c>.</summary>
[PublicAPI]
public sealed class TenantWriteGuardOptions
{
    /// <summary>
    /// Gets a value indicating whether tenant-owned writes require an ambient tenant
    /// unless a scoped bypass is active.
    /// </summary>
    public bool IsEnabled { get; internal set; }
}
