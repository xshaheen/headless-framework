// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Checks;

namespace Headless.MultiTenancy;

/// <summary>
/// Evicts tenant catalog cache entries after the application changes a tenant in its store. The store SPI is
/// read-only, so the catalog cannot see those writes; without an eviction a change reaches resolution only when
/// the cached entry expires (<see cref="TenantCatalogOptions.CacheExpiration"/>, or
/// <see cref="TenantCatalogOptions.UnknownIdentifierCacheExpiration"/> for an unknown identifier).
/// </summary>
/// <remarks>
/// <para>
/// Each method removes one exact cache key; neither reads the cache to discover related entries, because the
/// local node may not hold an entry a peer still serves. Name every key that changed:
/// </para>
/// <list type="bullet">
/// <item>Identifier re-pointed to a different tenant: <see cref="InvalidateIdentifierAsync"/> for that identifier.</item>
/// <item>Tenant disabled, enabled, or its metadata changed: <see cref="InvalidateTenantAsync"/> for its id.</item>
/// <item>Identifier renamed: <see cref="InvalidateTenantAsync"/> plus <see cref="InvalidateIdentifierAsync"/> for the old and the new identifier.</item>
/// <item>Tenant created under an identifier that was probed while unknown: <see cref="InvalidateIdentifierAsync"/> for it.</item>
/// </list>
/// <para>
/// Invalidate after the store write commits. A lookup that read the store before the commit can still write the
/// old answer back after the eviction, and a hybrid cache tells peers to drop their local copy only when the
/// shared (L2) cache actually held the key, so a peer whose L2 copy had already been evicted serves its local
/// copy until that expires. Both windows are bounded by the configured expirations, which is why retiring an
/// identifier for at least <see cref="TenantCatalogOptions.CacheExpiration"/> before reusing it stays the safe
/// default.
/// </para>
/// <para>
/// Cache faults propagate: an eviction that did not happen must not look like one that did.
/// </para>
/// </remarks>
[PublicAPI]
public interface ITenantCatalogCacheInvalidator
{
    /// <summary>Removes the cached identifier→id mapping, positive or negative, for <paramref name="identifier"/>.</summary>
    /// <param name="identifier">The identifier; trimmed and lowercased the same way resolution normalizes it.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="ArgumentNullException"><paramref name="identifier"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="identifier"/> is empty or white space.</exception>
    Task InvalidateIdentifierAsync(string identifier, CancellationToken cancellationToken = default);

    /// <summary>Removes the cached id→<see cref="TenantInfo"/> entry for <paramref name="id"/>.</summary>
    /// <param name="id">The canonical tenant id.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="ArgumentNullException"><paramref name="id"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="id"/> is empty or white space.</exception>
    Task InvalidateTenantAsync(string id, CancellationToken cancellationToken = default);
}

/// <summary>Default <see cref="ITenantCatalogCacheInvalidator"/> over the catalog service's two caches.</summary>
internal sealed class TenantCatalogCacheInvalidator(
    ICache<TenantIdentifierCacheItem> identifierCache,
    ICache<TenantInfoCacheItem> infoCache
) : ITenantCatalogCacheInvalidator
{
    /// <inheritdoc/>
    public async Task InvalidateIdentifierAsync(string identifier, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrWhiteSpace(identifier);

        var cacheKey = TenantIdentifierCacheItem.CalculateCacheKey(identifier.Trim().ToLowerInvariant());

        await identifierCache.RemoveAsync(cacheKey, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task InvalidateTenantAsync(string id, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrWhiteSpace(id);

        await infoCache.RemoveAsync(TenantInfoCacheItem.CalculateCacheKey(id), cancellationToken).ConfigureAwait(false);
    }
}
