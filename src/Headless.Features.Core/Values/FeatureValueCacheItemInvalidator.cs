// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Domain;
using Headless.Features.Entities;

namespace Headless.Features.Values;

/// <summary>
/// Domain-event handler that evicts the <see cref="FeatureValueCacheItem"/> for a
/// <see cref="FeatureValueRecord"/> whenever the record is created, updated, or deleted.
/// </summary>
public sealed class FeatureValueCacheItemInvalidator(ICache<FeatureValueCacheItem> cache)
    : IDomainEventHandler<EntityChangedEventData<FeatureValueRecord>>
{
    /// <inheritdoc/>
    public async ValueTask HandleAsync(
        EntityChangedEventData<FeatureValueRecord> message,
        CancellationToken cancellationToken = default
    )
    {
        var cacheKey = _CalculateCacheKey(message.Entity.Name, message.Entity.ProviderName, message.Entity.ProviderKey);

        await cache.RemoveAsync(cacheKey, cancellationToken);
    }

    private static string _CalculateCacheKey(string name, string providerName, string? providerKey)
    {
        return FeatureValueCacheItem.CalculateCacheKey(name, providerName, providerKey);
    }
}
