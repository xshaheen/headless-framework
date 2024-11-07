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

/// <summary>Store for setting definitions that defined dynamically from an external source like a database.</summary>
public interface IDynamicSettingDefinitionStore
{
    Task<IReadOnlyList<SettingDefinition>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<SettingDefinition?> GetOrDefaultAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Save the application static settings to the dynamic store.</summary>
    Task SaveAsync(CancellationToken cancellationToken = default);
}

public sealed class DynamicSettingDefinitionStore(
    ISettingDefinitionRecordRepository definitionRepository,
    IStaticSettingDefinitionStore staticStore,
    ISettingDefinitionSerializer definitionSerializer,
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

    /// <summary>
    /// A lock key for the application features update to allow only one instance to try
    /// to save the changes at a time.
    /// </summary>
    private readonly string _appSaveLockKey = $"{application.ApplicationName}_SettingsUpdateLock";

    /// <summary>A hash of the application features to check if there are changes and need to save them.</summary>
    private readonly string _appSaveFeaturesHashCacheKey = $"{application.ApplicationName}_SettingsHash";

    #region Get Methods

    public async Task<SettingDefinition?> GetOrDefaultAsync(string name, CancellationToken cancellationToken = default)
    {
        if (!_options.IsDynamicSettingStoreEnabled)
        {
            return null;
        }

        using (await _syncSemaphore.LockAsync(cancellationToken))
        {
            await _EnsureMemoryCacheIsUptoDateAsync(cancellationToken);

            return _memoryCache.GetOrDefault(name);
        }
    }

    public async Task<IReadOnlyList<SettingDefinition>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.IsDynamicSettingStoreEnabled)
        {
            return [];
        }

        using (await _syncSemaphore.LockAsync(cancellationToken))
        {
            await _EnsureMemoryCacheIsUptoDateAsync(cancellationToken);

            return _memoryCache.Values.ToImmutableList();
        }
    }

    #endregion

    #region Get Helpers

    private string? _cacheStamp;
    private DateTimeOffset? _lastCheckTime;
    private readonly SemaphoreSlim _syncSemaphore = new(1, 1);
    private readonly Dictionary<string, SettingDefinition> _memoryCache = new(StringComparer.Ordinal);

    private async Task _EnsureMemoryCacheIsUptoDateAsync(CancellationToken cancellationToken)
    {
        if (!_IsUpdateMemoryCacheRequired())
        {
            return; // Get the latest setting with a small delay for optimization
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

    private async Task<string> _GetOrSetDistributedCacheStampAsync(CancellationToken cancellationToken)
    {
        var cachedStamp = await distributedCache.GetAsync<string>(
            _options.CommonSettingsUpdatedStampCacheKey,
            cancellationToken
        );

        if (!cachedStamp.IsNull)
        {
            return cachedStamp.Value;
        }

        await using var resourceLock =
            await resourceLockProvider.TryAcquireAsync(
                resource: _options.CrossApplicationsCommonLockKey,
                timeUntilExpires: _options.CrossApplicationsCommonLockExpiration,
                acquireTimeout: _options.CrossApplicationsCommonLockAcquireTimeout,
                acquireAbortToken: cancellationToken
            )
            ?? throw new InvalidOperationException(
                "Could not acquire distributed lock for setting definition common stamp check!"
            ); // This request will fail

        cancellationToken.ThrowIfCancellationRequested();

        cachedStamp = await distributedCache.GetAsync<string>(
            _options.CommonSettingsUpdatedStampCacheKey,
            cancellationToken
        );

        if (!cachedStamp.IsNull)
        {
            return cachedStamp.Value;
        }

        return await _ChangeCommonStampAsync(cancellationToken);
    }

    private async Task _UpdateInMemoryCacheAsync(CancellationToken cancellationToken)
    {
        var records = await definitionRepository.GetListAsync(cancellationToken);

        _memoryCache.Clear();

        foreach (var record in records)
        {
            _memoryCache[record.Name] = definitionSerializer.Deserialize(record);
        }
    }

    private bool _IsUpdateMemoryCacheRequired()
    {
        if (_lastCheckTime is null)
        {
            return true;
        }

        var elapsedSinceLastCheck = timeProvider.GetUtcNow().Subtract(_lastCheckTime.Value);

        return elapsedSinceLastCheck.TotalSeconds > 30;
    }

    public void Dispose()
    {
        _syncSemaphore.Dispose();
    }

    #endregion

    #region Save Method

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await using var applicationResourceLock = await resourceLockProvider.TryAcquireAsync(
            _appSaveLockKey,
            timeUntilExpires: _options.ApplicationSaveLockExpiration,
            acquireTimeout: _options.ApplicationSaveLockAcquireTimeout,
            acquireAbortToken: cancellationToken
        );

        if (applicationResourceLock is null)
        {
            return; // Another application instance is already doing it
        }

        cancellationToken.ThrowIfCancellationRequested();
        var cachedHash = await distributedCache.GetAsync<string>(_appSaveFeaturesHashCacheKey, cancellationToken);
        var records = definitionSerializer.Serialize(await staticStore.GetAllAsync(cancellationToken));
        var currentHash = _CalculateHash(records, _providers.DeletedSettings);

        if (string.Equals(cachedHash.Value, currentHash, StringComparison.Ordinal))
        {
            return; // No changes
        }

        await using var commonResourceLock =
            await resourceLockProvider.TryAcquireAsync(
                _options.CrossApplicationsCommonLockKey,
                timeUntilExpires: 10.Minutes(),
                acquireTimeout: 5.Minutes(),
                acquireAbortToken: cancellationToken
            ) ?? throw new InvalidOperationException("Could not acquire distributed lock for saving static Settings!"); // It will re-try

        var (newRecords, changedRecords, deletedRecords) = await _UpdateChangedSettingsAsync(
            records,
            cancellationToken
        );

        var hasChangesInSettings = newRecords.Count != 0 || changedRecords.Count != 0 || deletedRecords.Count != 0;

        if (hasChangesInSettings)
        {
            await definitionRepository.SaveAsync(newRecords, changedRecords, deletedRecords, cancellationToken);
            await _ChangeCommonStampAsync(cancellationToken);
        }

        await distributedCache.UpsertAsync(
            _appSaveFeaturesHashCacheKey,
            currentHash,
            _options.SettingsHashCacheExpiration,
            cancellationToken
        );
    }

    #endregion

    #region Save Helpers

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

    private async Task<(
        List<SettingDefinitionRecord> NewRecords,
        List<SettingDefinitionRecord> ChangedRecords,
        List<SettingDefinitionRecord> DeletedRecords
    )> _UpdateChangedSettingsAsync(List<SettingDefinitionRecord> settingRecords, CancellationToken cancellationToken)
    {
        var dbRecords = await definitionRepository.GetListAsync(cancellationToken);
        var dbRecordsMap = dbRecords.ToDictionary(x => x.Name, StringComparer.Ordinal);

        var newRecords = new List<SettingDefinitionRecord>();
        var changedRecords = new List<SettingDefinitionRecord>();
        var deletedRecords = new List<SettingDefinitionRecord>();

        // Handle new and changed records
        foreach (var record in settingRecords)
        {
            var dbSettingRecord = dbRecordsMap.GetOrDefault(record.Name);

            if (dbSettingRecord is null)
            {
                newRecords.Add(record); // New

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
            deletedRecords.AddRange(dbRecordsMap.Values.Where(x => _providers.DeletedSettings.Contains(x.Name)));
        }

        return (newRecords, changedRecords, deletedRecords);
    }

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

    #region Helpers

    /// <summary>Change the cache stamp to notify other instances to update their local caches</summary>
    private async Task<string> _ChangeCommonStampAsync(CancellationToken cancellationToken)
    {
        var stamp = guidGenerator.Create().ToString("N");

        await distributedCache.UpsertAsync(
            _options.CommonSettingsUpdatedStampCacheKey,
            stamp,
            _options.CommonSettingsUpdatedStampCacheExpiration,
            cancellationToken
        );

        return stamp;
    }

    #endregion
}
