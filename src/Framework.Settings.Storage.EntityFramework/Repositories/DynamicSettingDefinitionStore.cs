using Framework.Caching;
using Framework.ResourceLocks;
using Framework.Settings.Definitions;
using Framework.Settings.Models;
using Framework.Settings.Options;
using Microsoft.Extensions.Options;
using Nito.AsyncEx;

namespace Framework.Settings.Repositories;

public sealed class DynamicSettingDefinitionStore : IDynamicSettingDefinitionStore
{
    private readonly ISettingDefinitionRecordRepository _settingRepository;
    private readonly IDynamicSettingDefinitionStoreInMemoryCache _storeCache;
    private readonly ICache _distributedCache;
    private readonly IResourceLockProvider _resourceLockProvider;
    private readonly SettingManagementOptions _settingManagementOptions;

    public DynamicSettingDefinitionStore(
        ISettingDefinitionRecordRepository textSettingRepository,
        IDynamicSettingDefinitionStoreInMemoryCache storeCache,
        ICache distributedCache,
        IResourceLockProvider resourceLockProvider,
        IOptions<SettingManagementOptions> settingManagementOptions
    )
    {
        _settingRepository = textSettingRepository;
        _storeCache = storeCache;
        _distributedCache = distributedCache;
        _resourceLockProvider = resourceLockProvider;
        _settingManagementOptions = settingManagementOptions.Value;
    }

    public async Task<SettingDefinition?> GetOrDefaultAsync(string name)
    {
        if (!_settingManagementOptions.IsDynamicSettingStoreEnabled)
        {
            return null;
        }

        using (await _storeCache.SyncSemaphore.LockAsync())
        {
            await _EnsureCacheIsUptoDateAsync();
            return _storeCache.GetSettingOrDefault(name);
        }
    }

    public async Task<IReadOnlyList<SettingDefinition>> GetAllAsync()
    {
        if (!_settingManagementOptions.IsDynamicSettingStoreEnabled)
        {
            return Array.Empty<SettingDefinition>();
        }

        using (await _storeCache.SyncSemaphore.LockAsync())
        {
            await _EnsureCacheIsUptoDateAsync();
            return _storeCache.GetSettings().ToImmutableList();
        }
    }

    #region Helpers

    private async Task _EnsureCacheIsUptoDateAsync()
    {
        if (
            _storeCache.LastCheckTime.HasValue
            && DateTime.Now.Subtract(_storeCache.LastCheckTime.Value).TotalSeconds < 30
        )
        {
            /* We get the latest setting with a small delay for optimization */
            return;
        }

        var stampInDistributedCache = await _GetOrSetStampInDistributedCache();

        if (string.Equals(stampInDistributedCache, _storeCache.CacheStamp, StringComparison.Ordinal))
        {
            _storeCache.LastCheckTime = DateTime.Now;
            return;
        }

        await _UpdateInMemoryStoreCache();

        _storeCache.CacheStamp = stampInDistributedCache;
        _storeCache.LastCheckTime = DateTime.Now;
    }

    private async Task _UpdateInMemoryStoreCache()
    {
        var settingRecords = await _settingRepository.GetListAsync();
        await _storeCache.FillAsync(settingRecords);
    }

    private async Task<string> _GetOrSetStampInDistributedCache()
    {
        var cacheKey = _GetCommonStampCacheKey();

        var stampInDistributedCache = await _distributedCache.GetAsync<string>(cacheKey);
        if (!stampInDistributedCache.IsNull)
        {
            return stampInDistributedCache.Value;
        }

        await using var commonLockHandle = await _resourceLockProvider.TryAcquireAsync(
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

        stampInDistributedCache = await _distributedCache.GetAsync<string>(cacheKey);
        if (!stampInDistributedCache.IsNull)
        {
            return stampInDistributedCache.Value;
        }

        var newStamp = Guid.NewGuid().ToString();

        await _distributedCache.UpsertAsync(cacheKey, newStamp, TimeSpan.FromDays(30));

        return newStamp;
    }

    private static string _GetCommonStampCacheKey() => "InMemorySettingCacheStamp";

    private static string _GetCommonDistributedLockKey() => "Common_SettingUpdateLock";

    #endregion
}
