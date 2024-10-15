// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using FluentDate;
using Framework.Caching;
using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.ResourceLocks;
using Framework.Serializer.Json.Modifiers;
using Framework.Settings.Entities;
using Framework.Settings.Models;
using Microsoft.Extensions.Options;

namespace Framework.Settings.Definitions;

public abstract class SettingDefinitionRecordRepository(
    IStaticSettingDefinitionStore staticStore,
    ISettingDefinitionSerializer settingSerializer,
    ICache cache,
    IResourceLockProvider resourceLockProvider,
    IGuidGenerator guidGenerator,
    IApplicationInformationAccessor application,
    IOptions<SettingManagementOptions> optionsAccessor
)
{
    private readonly SettingManagementOptions _options = optionsAccessor.Value;
    private const string _CommonResourceLockKey = SettingsConstants.CommonUpdateLockKey;
    private readonly string _appResourceLockKey = SettingsConstants.GetApplicationLockKey(application.ApplicationName);
    private readonly string _hashCacheKey = $"{application.ApplicationName}_SettingsHash";

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

        var cachedHash = await cache.GetAsync<string>(_hashCacheKey, cancellationToken);
        var records = settingSerializer.Serialize(await staticStore.GetAllAsync());
        var currentHash = _CalculateHash(records, _options.DeletedSettings);

        if (string.Equals(cachedHash.Value, currentHash, StringComparison.Ordinal))
        {
            return; // No changes
        }

        await using var commonLockHandle =
            await resourceLockProvider.TryAcquireAsync(_CommonResourceLockKey, TimeSpan.FromMinutes(5))
            ?? throw new InvalidOperationException("Could not acquire distributed lock for saving static Settings!"); // It will re-try

        // Execute in trans
        var (newRecords, changedRecords, deletedRecords) = await _UpdateChangedSettingsAsync(records);

        var hasChangesInSettings = newRecords.Count != 0 || changedRecords.Count != 0 || deletedRecords.Count != 0;

        if (hasChangesInSettings)
        {
            await SaveAsync(newRecords, changedRecords, deletedRecords, cancellationToken);

            // Change the cache stamp to notify other instances to update their local caches
            var stamp = guidGenerator.Create().ToString();

            await cache.UpsertAsync(
                SettingsConstants.SettingUpdatedStampCacheKey,
                stamp,
                TimeSpan.FromDays(30),
                cancellationToken
            );
        }

        await cache.UpsertAsync(_hashCacheKey, currentHash, TimeSpan.FromDays(30), cancellationToken);
    }

    public abstract Task<List<SettingDefinitionRecord>> GetListAsync(CancellationToken cancellationToken = default);

    protected abstract Task SaveAsync(
        List<SettingDefinitionRecord> addedRecords,
        List<SettingDefinitionRecord> changedRecords,
        List<SettingDefinitionRecord> deletedRecords,
        CancellationToken cancellationToken
    );

    #region Helpers

    private async Task<(
        List<SettingDefinitionRecord> NewRecords,
        List<SettingDefinitionRecord> ChangedRecords,
        List<SettingDefinitionRecord> DeletedRecords
    )> _UpdateChangedSettingsAsync(List<SettingDefinitionRecord> settingRecords)
    {
        var dbSettingsRecords = await GetListAsync();
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
        if (_options.DeletedSettings.Count != 0)
        {
            deletedRecords.AddRange(dbSettingRecordsMap.Values.Where(x => _options.DeletedSettings.Contains(x.Name)));
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
