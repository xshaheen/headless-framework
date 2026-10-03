// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.MultiTenancy;

/// <summary>
/// Cached entry for the catalog service's id→<see cref="TenantInfo"/> resolution axis. Always holds the
/// canonical base <see cref="TenantInfo"/> shape (never an app-defined subclass) — the cache-holds-base-shape
/// rule. There is no negative variant: an id with no matching tenant is never cached on this axis.
/// </summary>
/// <param name="tenantInfo">The cached tenant metadata, already coerced to the base <see cref="TenantInfo"/> shape.</param>
public sealed class TenantInfoCacheItem(TenantInfo tenantInfo)
{
    /// <summary>The cached tenant metadata.</summary>
    public TenantInfo TenantInfo { get; } = tenantInfo;

    /// <summary>Computes the cache key for an id→<see cref="TenantInfo"/> entry.</summary>
    /// <param name="id">The canonical tenant id.</param>
    public static string CalculateCacheKey(string id)
    {
        return $"tenancy:catalog:id:{id}";
    }
}
