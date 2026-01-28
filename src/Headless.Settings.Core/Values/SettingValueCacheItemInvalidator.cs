// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Domain;
using Headless.Settings.Entities;

namespace Headless.Settings.Values;

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
