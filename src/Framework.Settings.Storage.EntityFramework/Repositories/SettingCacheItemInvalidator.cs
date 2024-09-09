using Framework.Caching;
using Framework.Kernel.Domains;
using Framework.Settings.Entities;

namespace Framework.Settings.Repositories;

public sealed class SettingCacheItemInvalidator : ILocalMessageHandler<EntityChangedEventData<SettingRecord>>
{
    private ICache<SettingCacheItem> Cache { get; }

    public SettingCacheItemInvalidator(ICache<SettingCacheItem> cache) => Cache = cache;

    public async Task HandleAsync(EntityChangedEventData<SettingRecord> message, CancellationToken abortToken = default)
    {
        var cacheKey = SettingCacheItem.CalculateCacheKey(
            message.Entity.Name,
            message.Entity.ProviderName,
            message.Entity.ProviderKey
        );

        await Cache.RemoveAsync(cacheKey, abortToken);
    }
}
