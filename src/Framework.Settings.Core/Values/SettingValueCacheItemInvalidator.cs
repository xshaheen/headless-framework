// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Caching;
using Framework.Domain;
using Framework.Settings.Entities;

namespace Framework.Settings.Values;

public sealed class SettingValueCacheItemInvalidator(ICache<SettingValueCacheItem> cache)
    : ILocalMessageHandler<EntityChangedEventData<SettingValueRecord>>
{
    public async ValueTask HandleAsync(
        EntityChangedEventData<SettingValueRecord> message,
        CancellationToken cancellationToken = default
    )
    {
        var cacheKey = SettingValueCacheItem.CalculateCacheKey(
            message.Entity.Name,
            message.Entity.ProviderName,
            message.Entity.ProviderKey
        );

        await cache.RemoveAsync(cacheKey, cancellationToken).AnyContext();
    }
}
