using Framework.Caching;
using Framework.Kernel.Domains;
using Framework.Settings.Entities;

namespace Framework.Settings.Values;

public sealed class SettingCacheItemInvalidator(ICache<SettingValueCacheItem> cache)
    : ILocalMessageHandler<EntityChangedEventData<SettingRecord>>
{
    public async Task HandleAsync(
        EntityChangedEventData<SettingRecord> message,
        CancellationToken cancellationToken = default
    )
    {
        var cacheKey = SettingValueCacheItem.CalculateCacheKey(
            message.Entity.Name,
            message.Entity.ProviderName,
            message.Entity.ProviderKey
        );

        await cache.RemoveAsync(cacheKey, cancellationToken);
    }
}
