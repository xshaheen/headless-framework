// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Caching;
using Framework.Domain;
using Framework.Features.Entities;

namespace Framework.Features.Values;

public sealed class FeatureValueCacheItemInvalidator(ICache<FeatureValueCacheItem> cache)
    : ILocalMessageHandler<EntityChangedEventData<FeatureValueRecord>>
{
    public async Task HandleAsync(
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
