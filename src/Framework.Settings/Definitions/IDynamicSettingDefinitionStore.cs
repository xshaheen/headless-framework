// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Caching;
using Framework.ResourceLocks;
using Framework.Settings.Entities;
using Framework.Settings.Models;
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
    ISettingDefinitionRecordRepository repository,
    ICache distributedCache,
    IResourceLockProvider resourceLockProvider,
    IOptions<FrameworkSettingOptions> optionsAccessor
) : IDynamicSettingDefinitionStore, IDisposable
{
    private string? _cacheStamp;
    private DateTime? _lastCheckTime;
    private readonly SemaphoreSlim _syncSemaphore = new(1, 1);
    private readonly Dictionary<string, SettingDefinition> _settingDefinitions = new(StringComparer.Ordinal);
    private readonly FrameworkSettingOptions _options = optionsAccessor.Value;

    public async Task<SettingDefinition?> GetOrDefaultAsync(string name)
    {
        if (!_options.IsDynamicSettingStoreEnabled)
        {
            return null;
        }

        using (await _syncSemaphore.LockAsync())
        {
            await _EnsureCacheIsUptoDateAsync();

            return _settingDefinitions.GetOrDefault(name);
        }
    }

    public async Task<IReadOnlyList<SettingDefinition>> GetAllAsync()
    {
        if (!_options.IsDynamicSettingStoreEnabled)
        {
            return [];
        }

        using (await _syncSemaphore.LockAsync())
        {
            await _EnsureCacheIsUptoDateAsync();

            return _settingDefinitions.Values.ToImmutableList();
        }
    }

    #region Helpers

    private async Task _EnsureCacheIsUptoDateAsync()
    {
        if (_lastCheckTime.HasValue && DateTime.Now.Subtract(_lastCheckTime.Value).TotalSeconds < 30)
        {
            // We get the latest setting with a small delay for optimization
            return;
        }

        var stampInDistributedCache = await _GetOrSetStampInDistributedCache();

        if (string.Equals(stampInDistributedCache, _cacheStamp, StringComparison.Ordinal))
        {
            _lastCheckTime = DateTime.Now;

            return;
        }

        await _UpdateInMemoryStoreCache();
        _cacheStamp = stampInDistributedCache;
        _lastCheckTime = DateTime.Now;
    }

    private async Task<string> _GetOrSetStampInDistributedCache()
    {
        const string cacheKey = "InMemorySettingCacheStamp";

        var stampInDistributedCache = await distributedCache.GetAsync<string>(cacheKey);

        if (!stampInDistributedCache.IsNull)
        {
            return stampInDistributedCache.Value;
        }

        await using var commonLockHandle =
            await resourceLockProvider.TryAcquireAsync("CommonSettingUpdateLock", TimeSpan.FromMinutes(2))
            ?? throw new InvalidOperationException(
                "Could not acquire distributed lock for setting definition common stamp check!"
            );

        stampInDistributedCache = await distributedCache.GetAsync<string>(cacheKey);

        if (!stampInDistributedCache.IsNull)
        {
            return stampInDistributedCache.Value;
        }

        var newStamp = Guid.NewGuid().ToString();

        await distributedCache.UpsertAsync(cacheKey, newStamp, TimeSpan.FromDays(30));

        return newStamp;
    }

    private async Task _UpdateInMemoryStoreCache()
    {
        _Cache(await repository.GetListAsync());
    }

    private void _Cache(List<SettingDefinitionRecord> records)
    {
        _settingDefinitions.Clear();

        foreach (var record in records)
        {
            var settingDefinition = new SettingDefinition(
                record.Name,
                record.DefaultValue,
                record.DisplayName,
                record.Description,
                record.IsVisibleToClients,
                record.IsInherited,
                record.IsEncrypted
            );

            if (!record.Providers.IsNullOrWhiteSpace())
            {
                settingDefinition.Providers.AddRange(
                    record.Providers.Split(',', StringSplitOptions.RemoveEmptyEntries)
                );
            }

            foreach (var property in record.ExtraProperties)
            {
                settingDefinition[property.Key] = property.Value;
            }

            _settingDefinitions[record.Name] = settingDefinition;
        }
    }

    public void Dispose()
    {
        _syncSemaphore.Dispose();
    }

    #endregion
}
