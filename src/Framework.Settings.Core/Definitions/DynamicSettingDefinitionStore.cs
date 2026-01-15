// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Serialization.Metadata;
using Framework.Abstractions;
using Framework.Caching;
using Framework.ResourceLocks;
using Framework.Serializer.Modifiers;
using Framework.Settings.Entities;
using Framework.Settings.Models;
using Framework.Settings.Repositories;
using Humanizer;
using Microsoft.Extensions.Options;
using Nito.AsyncEx;

namespace Framework.Settings.Definitions;

/// <summary>
/// Store for setting definitions that defined dynamically from an external source like a database.
/// </summary>
public interface IDynamicSettingDefinitionStore
{
    Task<IReadOnlyList<SettingDefinition>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<SettingDefinition?> GetOrDefaultAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Save the current application static settings to the dynamic store.</summary>
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
    private readonly string _appSaveLockKey = $"settings:{application.ApplicationName}_update_lock";

    /// <summary>A hash of the application features to check if there are changes and need to save them.</summary>
    private readonly string _appSaveFeaturesHashCacheKey = $"settings:{application.ApplicationName}_hash";

    #region Get Methods

    public async Task<SettingDefinition?> GetOrDefaultAsync(string name, CancellationToken cancellationToken = default)
    {
        if (!_options.IsDynamicSettingStoreEnabled)
        {
            return null;
        }

        using (await _syncSemaphore.LockAsync(cancellationToken).AnyContext())
        {
            await _EnsureMemoryCacheIsUptoDateAsync(cancellationToken).AnyContext();

            var cache = _memoryCache; // Capture local reference
            return cache.GetOrDefault(name);
        }
    }

    public async Task<IReadOnlyList<SettingDefinition>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.IsDynamicSettingStoreEnabled)
        {
            return [];
        }

        using (await _syncSemaphore.LockAsync(cancellationToken).AnyContext())
        {
            await _EnsureMemoryCacheIsUptoDateAsync(cancellationToken).AnyContext();

            var cache = _memoryCache; // Capture local reference
            return cache.Values.ToImmutableList();
        }
    }

    #endregion

    #region Get Helpers

    private string? _cacheStamp;
    private DateTimeOffset? _lastCheckTime;
    private readonly SemaphoreSlim _syncSemaphore = new(1, 1);
    private volatile Dictionary<string, SettingDefinition> _memoryCache = new(StringComparer.Ordinal);

    private async Task _EnsureMemoryCacheIsUptoDateAsync(CancellationToken cancellationToken)
    {
        if (!_IsUpdateMemoryCacheRequired())
        {
            return; // Get the latest setting with a small delay for optimization
        }

        var cacheStamp = await _GetOrSetDistributedCacheStampAsync(cancellationToken).AnyContext();

        if (string.Equals(cacheStamp, _cacheStamp, StringComparison.Ordinal))
        {
            _lastCheckTime = timeProvider.GetUtcNow();

            return;
        }

        await _UpdateInMemoryCacheAsync(cancellationToken).AnyContext();
        _cacheStamp = cacheStamp;
        _lastCheckTime = timeProvider.GetUtcNow();
    }

    private async Task<string> _GetOrSetDistributedCacheStampAsync(CancellationToken cancellationToken)
    {
        var cachedStamp = await distributedCache
            .GetAsync<string>(_options.CommonSettingsUpdatedStampCacheKey, cancellationToken)
            .AnyContext();

        if (!cachedStamp.IsNull)
        {
            return cachedStamp.Value;
        }

        await using var resourceLock =
            await resourceLockProvider
                .TryAcquireAsync(
                    resource: _options.CrossApplicationsCommonLockKey,
                    timeUntilExpires: _options.CrossApplicationsCommonLockExpiration,
                    acquireTimeout: _options.CrossApplicationsCommonLockAcquireTimeout,
                    cancellationToken: cancellationToken
                )
                .AnyContext()
            ?? throw new InvalidOperationException(
                "Could not acquire distributed lock for setting definition common stamp check!"
            ); // This request will fail

        cancellationToken.ThrowIfCancellationRequested();

        cachedStamp = await distributedCache
            .GetAsync<string>(_options.CommonSettingsUpdatedStampCacheKey, cancellationToken)
            .AnyContext();

        if (!cachedStamp.IsNull)
        {
            return cachedStamp.Value;
        }

        return await _ChangeCommonStampAsync(cancellationToken).AnyContext();
    }

    private async Task _UpdateInMemoryCacheAsync(CancellationToken cancellationToken)
    {
        var records = await definitionRepository.GetListAsync(cancellationToken).AnyContext();

        var newCache = new Dictionary<string, SettingDefinition>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            newCache[record.Name] = definitionSerializer.Deserialize(record);
        }

        _memoryCache = newCache; // Atomic swap via volatile
    }

    private bool _IsUpdateMemoryCacheRequired()
    {
        if (_lastCheckTime is null)
        {
            return true;
        }

        var elapsedSinceLastCheck = timeProvider.GetUtcNow().Subtract(_lastCheckTime.Value);

        return elapsedSinceLastCheck > _options.DynamicDefinitionsMemoryCacheExpiration;
    }

    public void Dispose()
    {
        _syncSemaphore.Dispose();
    }

    #endregion

    #region Save Method

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await using var applicationResourceLock = await resourceLockProvider
            .TryAcquireAsync(
                _appSaveLockKey,
                timeUntilExpires: _options.ApplicationSaveLockExpiration,
                acquireTimeout: _options.ApplicationSaveLockAcquireTimeout,
                cancellationToken: cancellationToken
            )
            .AnyContext();

        if (applicationResourceLock is null)
        {
            return; // Another application instance is already doing it
        }

        cancellationToken.ThrowIfCancellationRequested();
        var cachedHash = await distributedCache
            .GetAsync<string>(_appSaveFeaturesHashCacheKey, cancellationToken)
            .AnyContext();
        var records = definitionSerializer.Serialize(await staticStore.GetAllAsync(cancellationToken).AnyContext());
        var currentHash = _CalculateHash(records, _providers.DeletedSettings);

        if (string.Equals(cachedHash.Value, currentHash, StringComparison.Ordinal))
        {
            return; // No changes
        }

        await using var commonResourceLock =
            await resourceLockProvider
                .TryAcquireAsync(
                    _options.CrossApplicationsCommonLockKey,
                    timeUntilExpires: 10.Minutes(),
                    acquireTimeout: 5.Minutes(),
                    cancellationToken: cancellationToken
                )
                .AnyContext()
            ?? throw new InvalidOperationException("Could not acquire distributed lock for saving static Settings!"); // It will re-try

        var (newRecords, changedRecords, deletedRecords) = await _UpdateChangedSettingsAsync(records, cancellationToken)
            .AnyContext();

        var hasChangesInSettings = newRecords.Count != 0 || changedRecords.Count != 0 || deletedRecords.Count != 0;

        if (hasChangesInSettings)
        {
            await definitionRepository
                .SaveAsync(newRecords, changedRecords, deletedRecords, cancellationToken)
                .AnyContext();
            await _ChangeCommonStampAsync(cancellationToken).AnyContext();
        }

        await distributedCache
            .UpsertAsync(
                _appSaveFeaturesHashCacheKey,
                currentHash,
                _options.SettingsHashCacheExpiration,
                cancellationToken
            )
            .AnyContext();
    }

    #endregion

    #region Save Helpers

    private readonly JsonSerializerOptions _jsonSerializerOptions = new()
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
        var dbRecords = await definitionRepository.GetListAsync(cancellationToken).AnyContext();
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

        await distributedCache
            .UpsertAsync(
                _options.CommonSettingsUpdatedStampCacheKey,
                stamp,
                _options.CommonSettingsUpdatedStampCacheExpiration,
                cancellationToken
            )
            .AnyContext();

        return stamp;
    }

    #endregion
}
