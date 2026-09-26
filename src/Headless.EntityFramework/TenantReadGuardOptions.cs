// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.EntityFramework;

/// <summary>Reports the EF tenant read guard state configured by <c>GuardTenantReads()</c>.</summary>
[PublicAPI]
public sealed class TenantReadGuardOptions
{
    /// <summary>
    /// Gets a value indicating whether queries over tenant-owned entities with a required tenant column
    /// throw <see cref="Headless.MultiTenancy.MissingTenantContextException"/> when no ambient tenant is set.
    /// </summary>
    public bool IsEnabled { get; internal set; }
}
