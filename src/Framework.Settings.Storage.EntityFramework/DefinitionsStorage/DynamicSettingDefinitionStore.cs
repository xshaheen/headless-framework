using Framework.Caching;
using Framework.ResourceLocks;
using Framework.Settings.Definitions;
using Framework.Settings.Models;
using Framework.Settings.Options;
using Framework.Settings.Repositories;
using Microsoft.Extensions.Options;
using Nito.AsyncEx;

namespace Framework.Settings.DefinitionsStorage;

public sealed class DynamicSettingDefinitionStore(
    ISettingDefinitionRecordRepository textSettingRepository,
    DynamicSettingDefinitionStoreInMemoryCache storeCache,
    ICache distributedCache,
    IResourceLockProvider resourceLockProvider,
    IOptions<SettingManagementOptions> settingManagementOptions
) : IDynamicSettingDefinitionStore
{
    private readonly SettingManagementOptions _settingManagementOptions = settingManagementOptions.Value;

    public async Task<SettingDefinition?> GetOrDefaultAsync(string name)
    {
        if (!_settingManagementOptions.IsDynamicSettingStoreEnabled)
        {
            return null;
        }

        using (await storeCache.SyncSemaphore.LockAsync())
        {
            await _EnsureCacheIsUptoDateAsync();
            return storeCache.GetSettingOrDefault(name);
        }
    }

    public async Task<IReadOnlyList<SettingDefinition>> GetAllAsync()
    {
        if (!_settingManagementOptions.IsDynamicSettingStoreEnabled)
        {
            return Array.Empty<SettingDefinition>();
        }

        using (await storeCache.SyncSemaphore.LockAsync())
        {
            await _EnsureCacheIsUptoDateAsync();
            return storeCache.GetSettings().ToImmutableList();
        }
    }

    #region Helpers

    private async Task _EnsureCacheIsUptoDateAsync()
    {
        if (
            storeCache.LastCheckTime.HasValue
            && DateTime.Now.Subtract(storeCache.LastCheckTime.Value).TotalSeconds < 30
        )
        {
            /* We get the latest setting with a small delay for optimization */
            return;
        }

        var stampInDistributedCache = await _GetOrSetStampInDistributedCache();

        if (string.Equals(stampInDistributedCache, storeCache.CacheStamp, StringComparison.Ordinal))
        {
            storeCache.LastCheckTime = DateTime.Now;
            return;
        }

        await _UpdateInMemoryStoreCache();

        storeCache.CacheStamp = stampInDistributedCache;
        storeCache.LastCheckTime = DateTime.Now;
    }

    private async Task _UpdateInMemoryStoreCache()
    {
        var settingRecords = await textSettingRepository.GetListAsync();
        await storeCache.FillAsync(settingRecords);
    }

    private async Task<string> _GetOrSetStampInDistributedCache()
    {
        var cacheKey = _GetCommonStampCacheKey();

        var stampInDistributedCache = await distributedCache.GetAsync<string>(cacheKey);
        if (!stampInDistributedCache.IsNull)
        {
            return stampInDistributedCache.Value;
        }

        await using var commonLockHandle = await resourceLockProvider.TryAcquireAsync(
            _GetCommonDistributedLockKey(),
            TimeSpan.FromMinutes(2)
        );

        if (commonLockHandle is null)
        {
            /* This request will fail */
            throw new InvalidOperationException(
                "Could not acquire distributed lock for setting definition common stamp check!"
            );
        }

        stampInDistributedCache = await distributedCache.GetAsync<string>(cacheKey);
        if (!stampInDistributedCache.IsNull)
        {
            return stampInDistributedCache.Value;
        }

        var newStamp = Guid.NewGuid().ToString();

        await distributedCache.UpsertAsync(cacheKey, newStamp, TimeSpan.FromDays(30));

        return newStamp;
    }

    private static string _GetCommonStampCacheKey() => "InMemorySettingCacheStamp";

    private static string _GetCommonDistributedLockKey() => "Common_SettingUpdateLock";

    #endregion
}
