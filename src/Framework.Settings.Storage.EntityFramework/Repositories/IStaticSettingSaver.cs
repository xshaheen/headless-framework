using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Framework.Caching;
using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.ResourceLocks;
using Framework.Serializer.Json.Modifiers;
using Framework.Settings.Definitions;
using Framework.Settings.Entities;
using Microsoft.Extensions.Options;

namespace Framework.Settings.Repositories;

public interface IStaticSettingSaver
{
    Task SaveAsync();
}

public interface ISettingsUnitOfWorkManager
{
    IDisposable Begin(bool requiresNew, bool isTransactional);

    Task CompleteAsync();

    Task RollbackAsync();
}

public sealed class StaticSettingSaver(
    IStaticSettingDefinitionStore staticStore,
    ISettingDefinitionRecordRepository settingRepository,
    ISettingDefinitionSerializer settingSerializer,
    ICache cache,
    IResourceLockProvider resourceLockProvider,
    IGuidGenerator guidGenerator,
    IApplicationInformationAccessor applicationInfoAccessor,
    ICancellationTokenProvider cancellationTokenProvider,
    IOptions<FrameworkSettingOptions> settingOptions,
    IOptions<CacheOptions> cacheOptions,
    ISettingsUnitOfWorkManager unitOfWorkManager
) : IStaticSettingSaver
{
    private readonly FrameworkSettingOptions _settingOptions = settingOptions.Value;
    private readonly CacheOptions _cacheOptions = cacheOptions.Value;

    public async Task SaveAsync()
    {
        await using var applicationLockHandle = await resourceLockProvider.TryAcquireAsync(
            _GetApplicationDistributedLockKey()
        );

        if (applicationLockHandle is null)
        {
            // Another application instance is already doing it
            return;
        }

        var cacheKey = _GetApplicationHashCacheKey();
        var cachedHash = await cache.GetAsync<string>(cacheKey, cancellationTokenProvider.Token);

        var settingRecords = settingSerializer.Serialize(await staticStore.GetAllAsync());
        var currentHash = _CalculateHash(settingRecords, _settingOptions.DeletedSettings);

        if (string.Equals(cachedHash.Value, currentHash, StringComparison.Ordinal))
        {
            return;
        }

        await using (
            var commonLockHandle = await resourceLockProvider.TryAcquireAsync(
                _GetCommonDistributedLockKey(),
                TimeSpan.FromMinutes(5)
            )
        )
        {
            if (commonLockHandle == null)
            {
                /* It will re-try */
                throw new InvalidOperationException("Could not acquire distributed lock for saving static Settings!");
            }

            using var unitOfWork = unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);

            try
            {
                var hasChangesInSettings = await _UpdateChangedSettingsAsync(settingRecords);

                if (hasChangesInSettings)
                {
                    await cache.UpsertAsync(
                        _GetCommonStampCacheKey(),
                        guidGenerator.Create().ToString(),
                        TimeSpan.FromDays(30),
                        cancellationTokenProvider.Token
                    );
                }
            }
            catch
            {
                try
                {
                    await unitOfWork.RollbackAsync();
                }
#pragma warning disable ERP022
                catch
                {
                    /* ignored */
                }
#pragma warning restore ERP022

                throw;
            }

            await unitOfWork.CompleteAsync();
        }

        await cache.UpsertAsync(cacheKey, currentHash, TimeSpan.FromDays(30), cancellationTokenProvider.Token);
    }

    #region Helpers

    private async Task<bool> _UpdateChangedSettingsAsync(List<SettingDefinitionRecord> settingRecords)
    {
        var newRecords = new List<SettingDefinitionRecord>();
        var changedRecords = new List<SettingDefinitionRecord>();

        var settingRecordsInDatabase = (await settingRepository.GetListAsync()).ToDictionary(
            x => x.Name,
            StringComparer.Ordinal
        );

        foreach (var record in settingRecords)
        {
            var settingRecordInDatabase = settingRecordsInDatabase.GetOrDefault(record.Name);

            if (settingRecordInDatabase is null)
            {
                /* New group */
                newRecords.Add(record);
                continue;
            }

            if (record.HasSameData(settingRecordInDatabase))
            {
                /* Not changed */
                continue;
            }

            /* Changed */
            settingRecordInDatabase.Patch(record);
            changedRecords.Add(settingRecordInDatabase);
        }

        /* Deleted */
        var deletedRecords = new List<SettingDefinitionRecord>();

        if (_settingOptions.DeletedSettings.Count != 0)
        {
            deletedRecords.AddRange(
                settingRecordsInDatabase.Values.Where(x => _settingOptions.DeletedSettings.Contains(x.Name))
            );
        }

        if (newRecords.Count != 0)
        {
            await settingRepository.InsertManyAsync(newRecords);
        }

        if (changedRecords.Count != 0)
        {
            await settingRepository.UpdateManyAsync(changedRecords);
        }

        if (deletedRecords.Count != 0)
        {
            await settingRepository.DeleteManyAsync(deletedRecords);
        }

        return newRecords.Count != 0 || changedRecords.Count != 0 || deletedRecords.Count != 0;
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

    private string _GetApplicationDistributedLockKey()
    {
        return $"{_cacheOptions.KeyPrefix}_{applicationInfoAccessor.ApplicationName}_SettingUpdateLock";
    }

    private string _GetCommonDistributedLockKey()
    {
        return $"{_cacheOptions.KeyPrefix}_Common_SettingUpdateLock";
    }

    private string _GetApplicationHashCacheKey()
    {
        return $"{_cacheOptions.KeyPrefix}_{applicationInfoAccessor.ApplicationName}_SettingsHash";
    }

    private string _GetCommonStampCacheKey()
    {
        return $"{_cacheOptions.KeyPrefix}_InMemorySettingCacheStamp";
    }

    #endregion
}
