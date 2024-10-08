using Framework.Caching;
using Framework.Kernel.Domains;
using Framework.Settings.Entities;

namespace Framework.Settings.ValuesStorage;

public sealed class SettingCacheItemInvalidator(ICache<SettingCacheItem> cache)
    : ILocalMessageHandler<EntityChangedEventData<SettingRecord>>
{
    public async Task HandleAsync(
        EntityChangedEventData<SettingRecord> message,
        CancellationToken cancellationToken = default
    )
    {
        var cacheKey = SettingCacheItem.CalculateCacheKey(
            message.Entity.Name,
            message.Entity.ProviderName,
            message.Entity.ProviderKey
        );

        await cache.RemoveAsync(cacheKey, cancellationToken);
    }
}
