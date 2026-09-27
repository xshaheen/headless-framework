// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.EntityFramework;

/// <summary>
/// Reports the EF tenant guards configured by <c>GuardTenantWrites()</c> and <c>GuardTenantReads()</c>.
/// Each guard is enabled independently through the tenancy builder; consumers cannot set these values.
/// </summary>
[PublicAPI]
public sealed class TenantGuardOptions
{
    /// <summary>
    /// Gets a value indicating whether tenant-owned writes require an ambient tenant
    /// unless a scoped bypass is active.
    /// </summary>
    public bool GuardWrites { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether queries over tenant-owned entities with a required tenant column
    /// throw <see cref="Headless.MultiTenancy.MissingTenantContextException"/> when no ambient tenant is set.
    /// </summary>
    public bool GuardReads { get; internal set; }
}
