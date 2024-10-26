// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Caching;
using Framework.Features.Entities;
using Framework.Kernel.Domains;

namespace Framework.Features.Values;

public class FeatureValueCacheItemInvalidator : ILocalMessageHandler<EntityChangedEventData<FeatureValueRecord>>
{
    private readonly ICache<FeatureValueCacheItem> _cache;

    public FeatureValueCacheItemInvalidator(ICache<FeatureValueCacheItem> cache)
    {
        _cache = cache;
    }

    public virtual async Task HandleAsync(
        EntityChangedEventData<FeatureValueRecord> message,
        CancellationToken cancellationToken = default
    )
    {
        var cacheKey = CalculateCacheKey(message.Entity.Name, message.Entity.ProviderName, message.Entity.ProviderKey);

        await _cache.RemoveAsync(cacheKey, cancellationToken);
    }

    protected virtual string CalculateCacheKey(string name, string providerName, string? providerKey)
    {
        return FeatureValueCacheItem.CalculateCacheKey(name, providerName, providerKey);
    }
}
