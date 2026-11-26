// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;

namespace Headless.Fencing;

/// <summary>Which tenant's leases an <see cref="IFencedLeaseInspector" /> list or count reads.</summary>
/// <remarks>The default value is <see cref="Any" />.</remarks>
[PublicAPI]
[StructLayout(LayoutKind.Auto)]
public readonly record struct LeaseTenantFilter
{
    private LeaseTenantFilter(string? tenantId)
    {
        IsScoped = true;
        TenantId = tenantId;
    }

    /// <summary>Gets the filter that reads the leases of every tenant and of the host scope.</summary>
    public static LeaseTenantFilter Any => default;

    /// <summary>Gets the filter that reads only the host scope's leases: those granted with no current tenant.</summary>
    public static LeaseTenantFilter Host => new(tenantId: null);

    /// <summary>Gets whether the filter reads every tenant and the host scope.</summary>
    public bool IsAny => !IsScoped;

    /// <summary>
    /// Gets the one tenant the filter reads, or <see langword="null" /> when it reads the host scope or every tenant.
    /// </summary>
    public string? TenantId { get; }

    // Stored inverted so that the default value reads every tenant.
    private bool IsScoped { get; }

    /// <summary>Returns the filter that reads only the leases of <paramref name="tenantId" />.</summary>
    /// <param name="tenantId">The tenant id, or <see langword="null" /> for the host scope.</param>
    /// <returns>The filter.</returns>
    public static LeaseTenantFilter Of(string? tenantId) => new(tenantId);
}
