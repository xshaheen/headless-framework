// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Domain;
using Headless.Settings.Entities;

namespace Headless.Settings.Values;

/// <summary>Invalidates the <see cref="SettingValueCacheItem"/> cache entry whenever a <see cref="SettingValueRecord"/> is created, updated, or deleted.</summary>
public sealed class SettingValueCacheItemInvalidator(ICache<SettingValueCacheItem> cache)
    : IDomainEventHandler<EntityChangedEventData<SettingValueRecord>>
{
    /// <inheritdoc/>
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

        await cache.RemoveAsync(cacheKey, cancellationToken).ConfigureAwait(false);
    }
}
