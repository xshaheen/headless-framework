// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Framework.Caching;
using Framework.Features.Entities;
using Framework.Features.Models;
using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.ResourceLocks;
using Framework.Serializer.Json.Modifiers;
using Humanizer;
using Microsoft.Extensions.Options;
using Nito.AsyncEx;

namespace Framework.Features.Definitions;

public interface IDynamicFeatureDefinitionStore
{
    Task<FeatureDefinition?> GetOrDefaultAsync(string name, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FeatureDefinition>> GetFeaturesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FeatureGroupDefinition>> GetGroupsAsync(CancellationToken cancellationToken = default);

    /// <summary>Save the application static settings to the dynamic store.</summary>
    Task SaveAsync(CancellationToken cancellationToken = default);
}

public sealed class DynamicFeatureDefinitionStore(
    IFeatureDefinitionRecordRepository repository,
    IStaticFeatureDefinitionStore staticStore,
    IFeatureDefinitionSerializer serializer,
    ICache distributedCache,
    IResourceLockProvider resourceLockProvider,
    IGuidGenerator guidGenerator,
    IApplicationInformationAccessor application,
    IOptions<FeatureManagementOptions> optionsAccessor,
    IOptions<FeatureManagementProvidersOptions> providersAccessor,
    TimeProvider timeProvider
) : IDynamicFeatureDefinitionStore, IDisposable
{
    private readonly FeatureManagementOptions _options = optionsAccessor.Value;
    private readonly FeatureManagementProvidersOptions _providers = providersAccessor.Value;

    private const string _StampCacheKey = "FeaturesUpdatedLocalStamp";
    private const string _CommonLockKey = "Common_FeaturesUpdateLock";
    private readonly string _appLockKey = $"{application.ApplicationName}_FeaturesUpdateLock";
    private readonly string _hashCacheKey = $"{application.ApplicationName}_FeaturesHash";

    #region Get Methods

    public async Task<FeatureDefinition?> GetOrDefaultAsync(string name, CancellationToken cancellationToken = default)
    {
        if (!_options.IsDynamicFeatureStoreEnabled)
        {
            return null;
        }

        using (await _syncSemaphore.LockAsync(cancellationToken))
        {
            await _EnsureMemoryCacheIsUptoDateAsync(cancellationToken);
        }

        return _featuresMemoryCache.GetOrDefault(name);
    }

    public async Task<IReadOnlyList<FeatureDefinition>> GetFeaturesAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.IsDynamicFeatureStoreEnabled)
        {
            return Array.Empty<FeatureDefinition>();
        }

        using (await _syncSemaphore.LockAsync(cancellationToken))
        {
            await _EnsureMemoryCacheIsUptoDateAsync(cancellationToken);
            return _featuresMemoryCache.Values.ToImmutableList();
        }
    }

    public async Task<IReadOnlyList<FeatureGroupDefinition>> GetGroupsAsync(
        CancellationToken cancellationToken = default
    )
    {
        if (!_options.IsDynamicFeatureStoreEnabled)
        {
            return Array.Empty<FeatureGroupDefinition>();
        }

        using (await _syncSemaphore.LockAsync(cancellationToken))
        {
            await _EnsureMemoryCacheIsUptoDateAsync(cancellationToken);
            return _groupMemoryCache.Values.ToImmutableList();
        }
    }

    #endregion

    #region Get Helpers

    private string? _cacheStamp;
    private DateTimeOffset? _lastCheckTime;
    private readonly SemaphoreSlim _syncSemaphore = new(1, 1);
    private readonly Dictionary<string, FeatureGroupDefinition> _groupMemoryCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FeatureDefinition> _featuresMemoryCache = new(StringComparer.Ordinal);

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

        await _UpdateInMemoryStoreCache(cancellationToken);
        _cacheStamp = cacheStamp;
        _lastCheckTime = timeProvider.GetUtcNow();
    }

    private async Task<string> _GetOrSetDistributedCacheStampAsync(CancellationToken cancellationToken)
    {
        var cachedStamp = await distributedCache.GetAsync<string>(_StampCacheKey, cancellationToken);

        if (!cachedStamp.IsNull)
        {
            return cachedStamp.Value;
        }

        await using var resourceLock =
            await resourceLockProvider.TryAcquireAsync(_CommonLockKey, 2.Minutes())
            ?? throw new InvalidOperationException(
                "Could not acquire distributed lock for feature definition common stamp check!"
            ); // This request will fail

        cancellationToken.ThrowIfCancellationRequested();
        cachedStamp = await distributedCache.GetAsync<string>(_StampCacheKey, cancellationToken);

        if (!cachedStamp.IsNull)
        {
            return cachedStamp.Value;
        }

        return await _ChangeCommonStamp(cancellationToken);
    }

    private async Task _UpdateInMemoryStoreCache(CancellationToken cancellationToken)
    {
        var featureGroupRecords = await repository.GetGroupsListAsync(cancellationToken);
        var featureRecords = await repository.GetFeaturesListAsync(cancellationToken);

        _groupMemoryCache.Clear();
        _featuresMemoryCache.Clear();

        var context = new FeatureDefinitionContext();

        foreach (var featureGroupRecord in featureGroupRecords)
        {
            var featureGroup = context.AddGroup(featureGroupRecord.Name, featureGroupRecord.DisplayName);

            _groupMemoryCache[featureGroup.Name] = featureGroup;

            foreach (var property in featureGroupRecord.ExtraProperties)
            {
                featureGroup[property.Key] = property.Value;
            }

            var featureRecordsInThisGroup = featureRecords.Where(p =>
                string.Equals(p.GroupName, featureGroup.Name, StringComparison.Ordinal)
            );

            foreach (var featureRecord in featureRecordsInThisGroup.Where(x => x.ParentName is null))
            {
                _UpdateInMemoryStoreCacheAddFeatureRecursively(featureGroup, featureRecord, featureRecords);
            }
        }

        await Task.CompletedTask;
    }

    private void _UpdateInMemoryStoreCacheAddFeatureRecursively(
        ICanCreateChildFeature featureContainer,
        FeatureDefinitionRecord featureRecord,
        List<FeatureDefinitionRecord> allFeatureRecords
    )
    {
        var feature = featureContainer.AddChild(
            featureRecord.Name,
            featureRecord.DefaultValue,
            featureRecord.DisplayName,
            featureRecord.Description,
            featureRecord.IsVisibleToClients,
            featureRecord.IsAvailableToHost
        );

        _featuresMemoryCache[feature.Name] = feature;

        if (!featureRecord.Providers.IsNullOrWhiteSpace())
        {
            feature.AllowedProviders.AddRange(featureRecord.Providers.Split(','));
        }

        foreach (var property in featureRecord.ExtraProperties)
        {
            feature[property.Key] = property.Value;
        }

        foreach (
            var subFeature in allFeatureRecords.Where(p =>
                string.Equals(p.ParentName, featureRecord.Name, StringComparison.Ordinal)
            )
        )
        {
            _UpdateInMemoryStoreCacheAddFeatureRecursively(feature, subFeature, allFeatureRecords);
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
            _appLockKey,
            timeUntilExpires: 10.Minutes(),
            acquireTimeout: 5.Minutes()
        );

        if (applicationResourceLock is null)
        {
            return; // Another application instance is already doing it
        }

        cancellationToken.ThrowIfCancellationRequested();

        // NOTE: This can be further optimized by using 4 cache values for:
        // Groups, features, deleted groups and deleted features.
        // But the code would be more complex.
        // This is enough for now.

        var cachedHash = await distributedCache.GetAsync<string>(_hashCacheKey, cancellationToken);
        var groups = await staticStore.GetGroupsAsync();
        var (featureGroupRecords, featureRecords) = serializer.Serialize(groups);

        var currentHash = _CalculateHash(
            featureGroupRecords,
            featureRecords,
            _providers.DeletedFeatureGroups,
            _providers.DeletedFeatures
        );

        if (string.Equals(cachedHash.Value, currentHash, StringComparison.Ordinal))
        {
            return; // No changes
        }

        await using var commonResourceLock =
            await resourceLockProvider.TryAcquireAsync(
                resource: _CommonLockKey,
                timeUntilExpires: 10.Minutes(),
                acquireTimeout: 5.Minutes()
            ) ?? throw new InvalidOperationException("Could not acquire distributed lock for saving static features!"); // It will re-try

        var (newGroups, updatedGroups, deletedGroups) = await _UpdateChangedFeatureGroupsAsync(
            featureGroupRecords,
            cancellationToken
        );

        var (newFeatures, updatedFeatures, deletedFeatures) = await _UpdateChangedFeaturesAsync(
            featureRecords,
            cancellationToken
        );

        var hasChangesInGroups = newGroups.Count != 0 || updatedGroups.Count != 0 || deletedGroups.Count != 0;
        var hasChangesInFeatures = newFeatures.Count != 0 || updatedFeatures.Count != 0 || deletedFeatures.Count != 0;

        if (hasChangesInGroups || hasChangesInFeatures)
        {
            await _ChangeCommonStamp(cancellationToken);
        }

        await distributedCache.UpsertAsync(_hashCacheKey, currentHash, TimeSpan.FromDays(30), cancellationToken);
    }

    #endregion

    #region Save Helpers

    private static readonly JsonSerializerOptions _JsonSerializerOptions =
        new()
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver
            {
                Modifiers =
                {
                    JsonPropertiesModifiers<FeatureGroupDefinitionRecord>.CreateIgnorePropertyModifyAction(x => x.Id),
                    JsonPropertiesModifiers<FeatureDefinitionRecord>.CreateIgnorePropertyModifyAction(x => x.Id),
                },
            },
        };

    private static string _CalculateHash(
        IReadOnlyCollection<FeatureGroupDefinitionRecord> featureGroupRecords,
        IReadOnlyCollection<FeatureDefinitionRecord> featureRecords,
        IEnumerable<string> deletedFeatureGroups,
        IEnumerable<string> deletedFeatures
    )
    {
        var stringBuilder = new StringBuilder();

        stringBuilder.Append("FeatureGroupRecords:");
        stringBuilder.AppendLine(JsonSerializer.Serialize(featureGroupRecords, _JsonSerializerOptions));

        stringBuilder.Append("FeatureRecords:");
        stringBuilder.AppendLine(JsonSerializer.Serialize(featureRecords, _JsonSerializerOptions));

        stringBuilder.Append("DeletedFeatureGroups:");
        stringBuilder.AppendLine(deletedFeatureGroups.JoinAsString(","));

        stringBuilder.Append("DeletedFeature:");
        stringBuilder.Append(deletedFeatures.JoinAsString(","));

        return stringBuilder.ToString().ToMd5();
    }

    private async Task<(
        List<FeatureGroupDefinitionRecord> NewRecords,
        List<FeatureGroupDefinitionRecord> ChangedRecords,
        List<FeatureGroupDefinitionRecord> DeletedRecords
    )> _UpdateChangedFeatureGroupsAsync(
        IEnumerable<FeatureGroupDefinitionRecord> featureGroupRecords,
        CancellationToken cancellationToken
    )
    {
        var dbRecords = await repository.GetGroupsListAsync(cancellationToken);
        var dbRecordsMap = dbRecords.ToDictionary(x => x.Name, StringComparer.Ordinal);

        var newRecords = new List<FeatureGroupDefinitionRecord>();
        var changedRecords = new List<FeatureGroupDefinitionRecord>();
        var deletedRecords = new List<FeatureGroupDefinitionRecord>();

        foreach (var featureGroupRecord in featureGroupRecords)
        {
            var featureGroupRecordInDatabase = dbRecordsMap.GetOrDefault(featureGroupRecord.Name);

            if (featureGroupRecordInDatabase is null)
            {
                newRecords.Add(featureGroupRecord); // New

                continue;
            }

            if (featureGroupRecord.HasSameData(featureGroupRecordInDatabase))
            {
                continue; // Not changed
            }

            featureGroupRecordInDatabase.Patch(featureGroupRecord); // Changed
            changedRecords.Add(featureGroupRecordInDatabase);
        }

        // Handle deleted records
        if (_providers.DeletedFeatureGroups.Count != 0)
        {
            deletedRecords.AddRange(dbRecordsMap.Values.Where(x => _providers.DeletedFeatureGroups.Contains(x.Name)));
        }

        return (newRecords, changedRecords, deletedRecords);
    }

    private async Task<(
        List<FeatureDefinitionRecord> NewRecords,
        List<FeatureDefinitionRecord> ChangedRecords,
        List<FeatureDefinitionRecord> DeletedRecords
    )> _UpdateChangedFeaturesAsync(
        IEnumerable<FeatureDefinitionRecord> featureRecords,
        CancellationToken cancellationToken
    )
    {
        var dbRecords = await repository.GetFeaturesListAsync(cancellationToken);
        var dbRecordsMap = dbRecords.ToDictionary(x => x.Name, StringComparer.Ordinal);

        var newRecords = new List<FeatureDefinitionRecord>();
        var changedRecords = new List<FeatureDefinitionRecord>();
        var deletedRecords = new List<FeatureDefinitionRecord>();

        // Handle new and changed records
        foreach (var featureRecord in featureRecords)
        {
            var featureRecordInDatabase = dbRecordsMap.GetOrDefault(featureRecord.Name);

            if (featureRecordInDatabase == null)
            {
                /* New group */
                newRecords.Add(featureRecord);

                continue;
            }

            if (featureRecord.HasSameData(featureRecordInDatabase))
            {
                /* Not changed */
                continue;
            }

            /* Changed */
            featureRecordInDatabase.Patch(featureRecord);
            changedRecords.Add(featureRecordInDatabase);
        }

        // Handle deleted records
        if (_providers.DeletedFeatures.Count != 0)
        {
            deletedRecords.AddRange(dbRecordsMap.Values.Where(x => _providers.DeletedFeatures.Contains(x.Name)));
        }

        if (_providers.DeletedFeatureGroups.Count != 0)
        {
            deletedRecords.AddIfNotContains(
                dbRecordsMap.Values.Where(x => _providers.DeletedFeatureGroups.Contains(x.GroupName))
            );
        }

        return (newRecords, changedRecords, deletedRecords);
    }

    #endregion

    #region Helpers

    /// <summary>Change the cache stamp to notify other instances to update their local caches</summary>
    private async Task<string> _ChangeCommonStamp(CancellationToken cancellationToken)
    {
        var stamp = guidGenerator.Create().ToString("N");
        await distributedCache.UpsertAsync(_StampCacheKey, stamp, TimeSpan.FromDays(30), cancellationToken);

        return stamp;
    }

    #endregion
}
