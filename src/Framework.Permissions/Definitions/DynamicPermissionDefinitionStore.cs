// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Caching;
using Framework.Permissions.PermissionManagement;
using Framework.ResourceLocks;
using Microsoft.Extensions.Options;
using Nito.AsyncEx;

namespace Framework.Permissions.Definitions;

public interface IDynamicPermissionDefinitionStore
{
    Task<PermissionDefinition?> GetOrNullAsync(string name, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PermissionDefinition>> GetPermissionsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PermissionGroupDefinition>> GetGroupsAsync(CancellationToken cancellationToken = default);
}

public sealed class DynamicPermissionDefinitionStore(
    IPermissionGroupDefinitionRecordRepository permissionGroupRepository,
    IPermissionDefinitionRecordRepository permissionRepository,
    IPermissionDefinitionSerializer permissionDefinitionSerializer,
    IDynamicPermissionDefinitionStoreInMemoryCache storeCache,
    IResourceLockProvider resourceLockProvider,
    ICache cache,
    IOptions<PermissionManagementOptions> permissionManagementOptions
) : IDynamicPermissionDefinitionStore
{
    private readonly IPermissionDefinitionSerializer _permissionDefinitionSerializer = permissionDefinitionSerializer;
    private readonly PermissionManagementOptions _permissionManagementOptions = permissionManagementOptions.Value;

    public async Task<PermissionDefinition?> GetOrNullAsync(string name, CancellationToken cancellationToken = default)
    {
        if (!_permissionManagementOptions.IsDynamicPermissionStoreEnabled)
        {
            return null;
        }

        using (await storeCache.SyncSemaphore.LockAsync(cancellationToken))
        {
            await _EnsureCacheIsUptoDateAsync();
            return storeCache.GetPermissionOrDefault(name);
        }
    }

    public async Task<IReadOnlyList<PermissionDefinition>> GetPermissionsAsync(
        CancellationToken cancellationToken = default
    )
    {
        if (!_permissionManagementOptions.IsDynamicPermissionStoreEnabled)
        {
            return Array.Empty<PermissionDefinition>();
        }

        using (await storeCache.SyncSemaphore.LockAsync(cancellationToken))
        {
            await _EnsureCacheIsUptoDateAsync();
            return storeCache.GetPermissions().ToImmutableList();
        }
    }

    public async Task<IReadOnlyList<PermissionGroupDefinition>> GetGroupsAsync(
        CancellationToken cancellationToken = default
    )
    {
        if (!_permissionManagementOptions.IsDynamicPermissionStoreEnabled)
        {
            return Array.Empty<PermissionGroupDefinition>();
        }

        using (await storeCache.SyncSemaphore.LockAsync(cancellationToken))
        {
            await _EnsureCacheIsUptoDateAsync();
            return storeCache.GetGroups().ToImmutableList();
        }
    }

    private async Task _EnsureCacheIsUptoDateAsync()
    {
        if (
            storeCache.LastCheckTime.HasValue
            && DateTime.Now.Subtract(storeCache.LastCheckTime.Value).TotalSeconds < 30
        )
        {
            // We get the latest permission with a small delay for optimization
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
        var permissionGroupRecords = await permissionGroupRepository.GetListAsync();
        var permissionRecords = await permissionRepository.GetListAsync();

        await storeCache.FillAsync(permissionGroupRecords, permissionRecords);
    }

    private async Task<string> _GetOrSetStampInDistributedCache()
    {
        var cacheKey = _GetCommonStampCacheKey();

        var stampInDistributedCache = await cache.GetAsync<string>(cacheKey);

        if (!stampInDistributedCache.IsNull)
        {
            return stampInDistributedCache.Value;
        }

        await using var commonLockHandle =
            await resourceLockProvider.TryAcquireAsync(_GetCommonDistributedLockKey(), TimeSpan.FromMinutes(2))
            ?? throw new InvalidOperationException(
                "Could not acquire distributed lock for permission definition common stamp check!"
            ); // This request will fail

        stampInDistributedCache = await cache.GetAsync<string>(cacheKey);

        if (!stampInDistributedCache.IsNull)
        {
            return stampInDistributedCache.Value;
        }

        var newStamp = Guid.NewGuid().ToString();

        await cache.UpsertAsync(cacheKey, newStamp, TimeSpan.FromDays(30));

        return newStamp;
    }

    private static string _GetCommonStampCacheKey()
    {
        return "_AbpInMemoryPermissionCacheStamp";
    }

    private static string _GetCommonDistributedLockKey()
    {
        return "_Common_AbpPermissionUpdateLock";
    }
}
