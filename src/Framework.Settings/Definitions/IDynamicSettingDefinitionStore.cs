// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Caching;
using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.ResourceLocks;
using Framework.Settings.Entities;
using Framework.Settings.Models;
using Humanizer;
using Microsoft.Extensions.Options;
using Nito.AsyncEx;

namespace Framework.Settings.Definitions;

/// <summary>Store for setting definitions that are defined dynamically from an external source like a database.</summary>
public interface IDynamicSettingDefinitionStore
{
    Task<IReadOnlyList<SettingDefinition>> GetAllAsync();

    Task<SettingDefinition?> GetOrDefaultAsync(string name);
}

public sealed class DynamicSettingDefinitionStore(
    SettingDefinitionRecordRepository repository,
    ISettingDefinitionSerializer serializer,
    ICache distributedCache,
    IResourceLockProvider resourceLockProvider,
    IGuidGenerator guidGenerator,
    IOptions<SettingManagementOptions> optionsAccessor,
    TimeProvider timeProvider
) : IDynamicSettingDefinitionStore, IDisposable
{
    private readonly SettingManagementOptions _options = optionsAccessor.Value;

    public async Task<SettingDefinition?> GetOrDefaultAsync(string name)
    {
        if (!_options.IsDynamicSettingStoreEnabled)
        {
            return null;
        }

        await _EnsureMemoryCacheIsUptoDateAsync();

        return _memoryCache.GetOrDefault(name);
    }

    public async Task<IReadOnlyList<SettingDefinition>> GetAllAsync()
    {
        if (!_options.IsDynamicSettingStoreEnabled)
        {
            return [];
        }

        await _EnsureMemoryCacheIsUptoDateAsync();

        return _memoryCache.Values.ToImmutableList();
    }

    #region Helpers

    private string? _cacheStamp;
    private DateTimeOffset? _lastCheckTime;
    private readonly SemaphoreSlim _syncSemaphore = new(1, 1);
    private readonly Dictionary<string, SettingDefinition> _memoryCache = new(StringComparer.Ordinal);

    private async Task _EnsureMemoryCacheIsUptoDateAsync()
    {
        using (await _syncSemaphore.LockAsync())
        {
            if (!_IsUpdateMemoryCacheRequired())
            {
                // We get the latest setting with a small delay for optimization
                return;
            }

            var cacheStamp = await _GetOrSetDistributedCacheStampAsync();

            if (string.Equals(cacheStamp, _cacheStamp, StringComparison.Ordinal))
            {
                _lastCheckTime = timeProvider.GetUtcNow();

                return;
            }

            await _UpdateInMemoryCacheAsync();
            _cacheStamp = cacheStamp;
            _lastCheckTime = timeProvider.GetUtcNow();
        }
    }

    private async Task _UpdateInMemoryCacheAsync()
    {
        var records = await repository.GetListAsync();

        _memoryCache.Clear();

        foreach (var record in records)
        {
            _memoryCache[record.Name] = serializer.Deserialize(record);
        }
    }

    private bool _IsUpdateMemoryCacheRequired()
    {
        if (!_lastCheckTime.HasValue)
        {
            return true;
        }

        var elapsedSinceLastCheck = timeProvider.GetUtcNow().Subtract(_lastCheckTime.Value);

        return elapsedSinceLastCheck.TotalSeconds < 30;
    }

    private async Task<string> _GetOrSetDistributedCacheStampAsync()
    {
        var cachedStamp = await distributedCache.GetAsync<string>(SettingsConstants.SettingUpdatedStampCacheKey);

        if (!cachedStamp.IsNull)
        {
            return cachedStamp.Value;
        }

        await using var resourceLock =
            await resourceLockProvider.TryAcquireAsync(SettingsConstants.CommonUpdateLockKey, 2.Minutes())
            ?? throw new InvalidOperationException(
                "Could not acquire distributed lock for setting definition common stamp check!"
            );

        cachedStamp = await distributedCache.GetAsync<string>(SettingsConstants.SettingUpdatedStampCacheKey);

        if (!cachedStamp.IsNull)
        {
            return cachedStamp.Value;
        }

        var newStamp = guidGenerator.Create().ToString("N");

        await distributedCache.UpsertAsync(
            SettingsConstants.SettingUpdatedStampCacheKey,
            newStamp,
            TimeSpan.FromDays(30)
        );

        return newStamp;
    }

    public void Dispose()
    {
        _syncSemaphore.Dispose();
    }

    #endregion
}
