// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Framework.Caching;
using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.ResourceLocks;
using Framework.Serializer.Json.Modifiers;
using Framework.Settings.Entities;
using Framework.Settings.Models;
using Humanizer;
using Microsoft.Extensions.Options;
using Nito.AsyncEx;

namespace Framework.Settings.Definitions;

/// <summary>Store for setting definitions that are defined dynamically from an external source like a database.</summary>
public interface IDynamicSettingDefinitionStore
{
    Task<IReadOnlyList<SettingDefinition>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<SettingDefinition?> GetOrDefaultAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Save the application static settings to the dynamic store.</summary>
    Task SaveAsync(CancellationToken cancellationToken = default);
}

public sealed class DynamicSettingDefinitionStore(
    ISettingDefinitionRecordRepository repository,
    IStaticSettingDefinitionStore staticStore,
    ISettingDefinitionSerializer serializer,
    ICache distributedCache,
    IResourceLockProvider resourceLockProvider,
    IGuidGenerator guidGenerator,
    IApplicationInformationAccessor application,
    IOptions<SettingManagementOptions> optionsAccessor,
    IOptions<SettingManagementProvidersOptions> providersAccessor,
    TimeProvider timeProvider
) : IDynamicSettingDefinitionStore, IDisposable
{
    private readonly SettingManagementOptions _options = optionsAccessor.Value;
    private readonly SettingManagementProvidersOptions _providers = providersAccessor.Value;
    private const string _CommonResourceLockKey = SettingsConstants.CommonUpdateLockKey;
    private readonly string _appResourceLockKey = SettingsConstants.GetApplicationLockKey(application.ApplicationName);
    private readonly string _hashCacheKey = $"{application.ApplicationName}_SettingsHash";

    public async Task<SettingDefinition?> GetOrDefaultAsync(string name, CancellationToken cancellationToken = default)
    {
        if (!_options.IsDynamicSettingStoreEnabled)
        {
            return null;
        }

        await _EnsureMemoryCacheIsUptoDateAsync(cancellationToken);

        return _memoryCache.GetOrDefault(name);
    }

    public async Task<IReadOnlyList<SettingDefinition>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.IsDynamicSettingStoreEnabled)
        {
            return [];
        }

        await _EnsureMemoryCacheIsUptoDateAsync(cancellationToken);

        return _memoryCache.Values.ToImmutableList();
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await using var applicationResourceLock = await resourceLockProvider.TryAcquireAsync(
            _appResourceLockKey,
            timeUntilExpires: 5.Minutes()
        );

        if (applicationResourceLock is null)
        {
            return; // Another application instance is already doing it
        }

        cancellationToken.ThrowIfCancellationRequested();
        var cachedHash = await distributedCache.GetAsync<string>(_hashCacheKey, cancellationToken);
        var records = serializer.Serialize(await staticStore.GetAllAsync());
        var currentHash = _CalculateHash(records, _providers.DeletedSettings);

        if (string.Equals(cachedHash.Value, currentHash, StringComparison.Ordinal))
        {
            return; // No changes
        }

        await using var commonLockHandle =
            await resourceLockProvider.TryAcquireAsync(_CommonResourceLockKey, TimeSpan.FromMinutes(5))
            ?? throw new InvalidOperationException("Could not acquire distributed lock for saving static Settings!"); // It will re-try

        // Execute in trans
        var (newRecords, changedRecords, deletedRecords) = await _UpdateChangedSettingsAsync(
            records,
            cancellationToken
        );

        var hasChangesInSettings = newRecords.Count != 0 || changedRecords.Count != 0 || deletedRecords.Count != 0;

        if (hasChangesInSettings)
        {
            await repository.SaveAsync(newRecords, changedRecords, deletedRecords, cancellationToken);

            // Change the cache stamp to notify other instances to update their local caches
            var stamp = guidGenerator.Create().ToString();

            await distributedCache.UpsertAsync(
                SettingsConstants.SettingUpdatedStampCacheKey,
                stamp,
                TimeSpan.FromDays(30),
                cancellationToken
            );
        }

        await distributedCache.UpsertAsync(_hashCacheKey, currentHash, TimeSpan.FromDays(30), cancellationToken);
    }

    #region Get Helpers

    private string? _cacheStamp;
    private DateTimeOffset? _lastCheckTime;
    private readonly SemaphoreSlim _syncSemaphore = new(1, 1);
    private readonly Dictionary<string, SettingDefinition> _memoryCache = new(StringComparer.Ordinal);

    private async Task _EnsureMemoryCacheIsUptoDateAsync(CancellationToken cancellationToken)
    {
        using (await _syncSemaphore.LockAsync(cancellationToken))
        {
            if (!_IsUpdateMemoryCacheRequired())
            {
                // We get the latest setting with a small delay for optimization
                return;
            }

            var cacheStamp = await _GetOrSetDistributedCacheStampAsync(cancellationToken);

            if (string.Equals(cacheStamp, _cacheStamp, StringComparison.Ordinal))
            {
                _lastCheckTime = timeProvider.GetUtcNow();

                return;
            }

            await _UpdateInMemoryCacheAsync(cancellationToken);
            _cacheStamp = cacheStamp;
            _lastCheckTime = timeProvider.GetUtcNow();
        }
    }

    private async Task _UpdateInMemoryCacheAsync(CancellationToken cancellationToken)
    {
        var records = await repository.GetListAsync(cancellationToken);

        _memoryCache.Clear();

        foreach (var record in records)
        {
            _memoryCache[record.Name] = serializer.Deserialize(record);
        }
    }

    private async Task<string> _GetOrSetDistributedCacheStampAsync(CancellationToken cancellationToken)
    {
        var cachedStamp = await distributedCache.GetAsync<string>(
            SettingsConstants.SettingUpdatedStampCacheKey,
            cancellationToken
        );

        if (!cachedStamp.IsNull)
        {
            return cachedStamp.Value;
        }

        await using var resourceLock =
            await resourceLockProvider.TryAcquireAsync(SettingsConstants.CommonUpdateLockKey, 2.Minutes())
            ?? throw new InvalidOperationException(
                "Could not acquire distributed lock for setting definition common stamp check!"
            );

        cancellationToken.ThrowIfCancellationRequested();

        cachedStamp = await distributedCache.GetAsync<string>(
            SettingsConstants.SettingUpdatedStampCacheKey,
            cancellationToken
        );

        if (!cachedStamp.IsNull)
        {
            return cachedStamp.Value;
        }

        var newStamp = guidGenerator.Create().ToString("N");

        await distributedCache.UpsertAsync(
            SettingsConstants.SettingUpdatedStampCacheKey,
            newStamp,
            TimeSpan.FromDays(30),
            cancellationToken
        );

        return newStamp;
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

    public void Dispose()
    {
        _syncSemaphore.Dispose();
    }

    #endregion

    #region Save Helpers

    private async Task<(
        List<SettingDefinitionRecord> NewRecords,
        List<SettingDefinitionRecord> ChangedRecords,
        List<SettingDefinitionRecord> DeletedRecords
    )> _UpdateChangedSettingsAsync(List<SettingDefinitionRecord> settingRecords, CancellationToken cancellationToken)
    {
        var dbSettingsRecords = await repository.GetListAsync(cancellationToken);
        var dbSettingRecordsMap = dbSettingsRecords.ToDictionary(x => x.Name, StringComparer.Ordinal);

        var newRecords = new List<SettingDefinitionRecord>();
        var changedRecords = new List<SettingDefinitionRecord>();
        var deletedRecords = new List<SettingDefinitionRecord>();

        // Handle new and changed records
        foreach (var record in settingRecords)
        {
            var dbSettingRecord = dbSettingRecordsMap.GetOrDefault(record.Name);

            if (dbSettingRecord is null)
            {
                newRecords.Add(record); // New group

                continue;
            }

            if (record.HasSameData(dbSettingRecord))
            {
                continue; // Not changed
            }

            dbSettingRecord.Patch(record); // Changed
            changedRecords.Add(dbSettingRecord);
        }

        // Handle deleted records
        if (_providers.DeletedSettings.Count != 0)
        {
            deletedRecords.AddRange(dbSettingRecordsMap.Values.Where(x => _providers.DeletedSettings.Contains(x.Name)));
        }

        return (newRecords, changedRecords, deletedRecords);
    }

    private readonly JsonSerializerOptions _jsonSerializerOptions =
        new()
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver
            {
                Modifiers =
                {
                    JsonPropertiesModifiers<SettingDefinitionRecord>.CreateIgnorePropertyModifyAction(x => x.Id),
                },
            },
        };

    private string _CalculateHash(List<SettingDefinitionRecord> settingRecords, IEnumerable<string> deletedSettings)
    {
        var stringBuilder = new StringBuilder();

        stringBuilder.Append("SettingRecords:");
        stringBuilder.AppendLine(JsonSerializer.Serialize(settingRecords, _jsonSerializerOptions));

        stringBuilder.Append("DeletedSetting:");
        stringBuilder.Append(deletedSettings.JoinAsString(","));

        return stringBuilder.ToString().ToMd5();
    }

    #endregion
}
