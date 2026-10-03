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
