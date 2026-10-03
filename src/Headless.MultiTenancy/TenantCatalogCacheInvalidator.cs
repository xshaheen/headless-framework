// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Checks;

namespace Headless.MultiTenancy;

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
