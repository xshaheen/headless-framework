// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.MultiTenancy;

/// <summary>
/// Cached entry for the catalog service's identifier→id resolution axis. A <see langword="null"/>
/// <see cref="TenantId"/> is a negative entry: the normalized identifier is known to have no matching
/// tenant, cached under <see cref="TenantCatalogOptions.UnknownIdentifierCacheExpiration"/> so repeated
/// probes of the same unknown identifier do not reach the store.
/// </summary>
/// <param name="tenantId">The canonical tenant id the identifier maps to, or <see langword="null"/> for a negative entry.</param>
public sealed class TenantIdentifierCacheItem(string? tenantId)
{
    /// <summary>The canonical tenant id, or <see langword="null"/> when this is a negative (unknown-identifier) entry.</summary>
    public string? TenantId { get; } = tenantId;

    /// <summary>Computes the cache key for an identifier→id entry.</summary>
    /// <param name="normalizedIdentifier">The already-normalized (trimmed, lowercased) identifier.</param>
    public static string CalculateCacheKey(string normalizedIdentifier)
    {
        return $"tenancy:catalog:identifier:{normalizedIdentifier}";
    }
}

/// <summary>
/// Cached entry for the catalog service's id→<see cref="TenantInfo"/> resolution axis. Always holds the
/// canonical base <see cref="TenantInfo"/> shape (never an app-defined subclass) — R13's cache-holds-base-shape
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
