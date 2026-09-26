// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;

namespace Headless.Fencing;

/// <summary>The stored identity of one lease: one row per key.</summary>
/// <remarks>
/// Every part is non-null because every part is a primary-key column. The host scope (no current tenant) is stored
/// as an empty <paramref name="TenantId" />, which no real tenant id can be. Keys are built by the lease services
/// after validation; providers store them as given and compare them ordinally.
/// </remarks>
/// <param name="TenantId">The owning tenant, or empty for the host scope.</param>
/// <param name="Kind">The lease kind.</param>
/// <param name="Resource">The leased resource.</param>
[PublicAPI]
[StructLayout(LayoutKind.Auto)]
public readonly record struct LeaseKey(string TenantId, string Kind, string Resource)
{
    /// <summary>Gets the public tenant id: <see langword="null" /> for the host scope.</summary>
    public string? PublicTenantId => TenantId.Length == 0 ? null : TenantId;

    /// <summary>Returns the lease this key carries at <paramref name="generation" />.</summary>
    /// <param name="generation">The lease's generation.</param>
    /// <returns>The lease, with the host scope mapped back to a <see langword="null" /> tenant.</returns>
    public FencedLease ToLease(long generation)
    {
        return new FencedLease(PublicTenantId, Kind, Resource, generation);
    }

    /// <summary>Returns the expired attempt this key carries, as handed to a sweep handler.</summary>
    /// <param name="generation">The abandoned attempt's generation.</param>
    /// <param name="expiresAt">When the attempt's lease expired.</param>
    /// <returns>The expired lease, with the host scope mapped back to a <see langword="null" /> tenant.</returns>
    public ExpiredLease ToExpiredLease(long generation, DateTimeOffset expiresAt)
    {
        return new ExpiredLease(PublicTenantId, Kind, Resource, generation, expiresAt);
    }
}
