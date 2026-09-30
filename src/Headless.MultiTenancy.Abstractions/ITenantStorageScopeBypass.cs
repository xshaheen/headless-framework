// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.MultiTenancy;

/// <summary>
/// Tracks an operation-local bypass of tenant storage scoping, for intentional host-level access to blobs and cache
/// entries that belong to no tenant.
/// </summary>
/// <remarks>
/// While a bypass is active, tenant-scoped blob storage passes every location through unchanged and a tenant-scoped
/// cache addresses the shared host scope, whatever the ambient tenant is. Without it, both refuse an operation that
/// runs with no ambient tenant. The bypass does not relax the EF tenant write or read guards; those use
/// <see cref="ITenantWriteGuardBypass"/> and <c>IgnoreMultiTenancyFilter()</c>.
/// </remarks>
[PublicAPI]
public interface ITenantStorageScopeBypass
{
    /// <summary>Gets a value indicating whether the current async operation is bypassing tenant storage scoping.</summary>
    bool IsActive { get; }

    /// <summary>Begins a scoped bypass and restores the previous state when disposed.</summary>
    [MustDisposeResource]
    IDisposable BeginBypass();
}
