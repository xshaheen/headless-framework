// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Features.Models;
using Framework.ResourceLocks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Nito.AsyncEx;

namespace Framework.Features.Definitions;

public interface IDynamicFeatureDefinitionStore
{
    Task<FeatureDefinition?> GetOrNullAsync(string name);

    Task<IReadOnlyList<FeatureDefinition>> GetFeaturesAsync();

    Task<IReadOnlyList<FeatureGroupDefinition>> GetGroupsAsync();
}

public sealed class DynamicFeatureDefinitionStore : IDynamicFeatureDefinitionStore
{
    private readonly IFeatureGroupDefinitionRecordRepository _featureGroupRepository;
    private readonly IFeatureDefinitionRecordRepository _featureRepository;
    private readonly IFeatureDefinitionSerializer _featureDefinitionSerializer;
    private readonly IDynamicFeatureDefinitionStoreInMemoryCache _storeCache;
    private readonly IDistributedCache _distributedCache;
    private readonly IResourceLockProvider _distributedLock;
    private readonly FeatureManagementOptions _featureManagementOptions;
    private AbpDistributedCacheOptions _cacheOptions;

    public DynamicFeatureDefinitionStore(
        IFeatureGroupDefinitionRecordRepository featureGroupRepository,
        IFeatureDefinitionRecordRepository featureRepository,
        IFeatureDefinitionSerializer featureDefinitionSerializer,
        IDynamicFeatureDefinitionStoreInMemoryCache storeCache,
        IDistributedCache distributedCache,
        IOptions<AbpDistributedCacheOptions> cacheOptions,
        IOptions<FeatureManagementOptions> featureManagementOptions,
        IResourceLockProvider distributedLock
    )
    {
        _featureGroupRepository = featureGroupRepository;
        _featureRepository = featureRepository;
        _featureDefinitionSerializer = featureDefinitionSerializer;
        _storeCache = storeCache;
        _distributedCache = distributedCache;
        _distributedLock = distributedLock;
        _featureManagementOptions = featureManagementOptions.Value;
        _cacheOptions = cacheOptions.Value;
    }

    public async Task<FeatureDefinition?> GetOrNullAsync(string name)
    {
        if (!_featureManagementOptions.IsDynamicFeatureStoreEnabled)
        {
            return null;
        }

        using (await _storeCache.SyncSemaphore.LockAsync())
        {
            await _EnsureCacheIsUptoDateAsync();
            return _storeCache.GetFeatureOrNull(name);
        }
    }

    public async Task<IReadOnlyList<FeatureDefinition>> GetFeaturesAsync()
    {
        if (!_featureManagementOptions.IsDynamicFeatureStoreEnabled)
        {
            return Array.Empty<FeatureDefinition>();
        }

        using (await _storeCache.SyncSemaphore.LockAsync())
        {
            await _EnsureCacheIsUptoDateAsync();
            return _storeCache.GetFeatures().ToImmutableList();
        }
    }

    public async Task<IReadOnlyList<FeatureGroupDefinition>> GetGroupsAsync()
    {
        if (!_featureManagementOptions.IsDynamicFeatureStoreEnabled)
        {
            return Array.Empty<FeatureGroupDefinition>();
        }

        using (await _storeCache.SyncSemaphore.LockAsync())
        {
            await _EnsureCacheIsUptoDateAsync();
            return _storeCache.GetGroups().ToImmutableList();
        }
    }

    private async Task _EnsureCacheIsUptoDateAsync(CancellationToken cancellationToken = default)
    {
        if (
            _storeCache.LastCheckTime.HasValue
            && DateTime.Now.Subtract(_storeCache.LastCheckTime.Value).TotalSeconds < 30
        )
        {
            // We get the latest feature with a small delay for optimization
            return;
        }

        var stampInDistributedCache = await _GetOrSetStampInDistributedCache();

        if (string.Equals(stampInDistributedCache, _storeCache.CacheStamp, StringComparison.Ordinal))
        {
            _storeCache.LastCheckTime = DateTime.Now;
            return;
        }

        await _UpdateInMemoryStoreCache(cancellationToken);

        _storeCache.CacheStamp = stampInDistributedCache;
        _storeCache.LastCheckTime = DateTime.Now;
    }

    private async Task _UpdateInMemoryStoreCache(CancellationToken cancellationToken)
    {
        var featureGroupRecords = await _featureGroupRepository.GetListAsync(cancellationToken;
        var featureRecords = await _featureRepository.GetListAsync(cancellationToken);

        await _storeCache.FillAsync(featureGroupRecords, featureRecords);
    }

    private async Task<string> _GetOrSetStampInDistributedCache()
    {
        var cacheKey = _GetCommonStampCacheKey();

        var stampInDistributedCache = await _distributedCache.GetStringAsync(cacheKey);

        if (stampInDistributedCache != null)
        {
            return stampInDistributedCache;
        }

        await using var commonLockHandle = await _distributedLock.TryAcquireAsync(
            _GetCommonDistributedLockKey(),
            TimeSpan.FromMinutes(2)
        );

        if (commonLockHandle == null)
        {
            // This request will fail
            throw new InvalidOperationException("Could not acquire distributed lock for feature definition common stamp check!");
        }

        stampInDistributedCache = await _distributedCache.GetStringAsync(cacheKey);
        if (stampInDistributedCache != null)
        {
            return stampInDistributedCache;
        }

        stampInDistributedCache = Guid.NewGuid().ToString();

        await _distributedCache.SetStringAsync(
            cacheKey,
            stampInDistributedCache,
            new DistributedCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromDays(
                    30
                ) //TODO: Make it configurable?
               ,
            }
        );

        return stampInDistributedCache;
    }

    private string _GetCommonStampCacheKey()
    {
        return $"{_cacheOptions.KeyPrefix}_AbpInMemoryFeatureCacheStamp";
    }

    private string _GetCommonDistributedLockKey()
    {
        return $"{_cacheOptions.KeyPrefix}_Common_AbpFeatureUpdateLock";
    }
}
