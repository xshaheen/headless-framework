// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Immutable;
using System.Text.Json.Serialization.Metadata;
using Framework.Abstractions;
using Framework.Caching;
using Framework.Features.Entities;
using Framework.Features.Models;
using Framework.Features.Repositories;
using Framework.ResourceLocks;
using Framework.Serializer.Modifiers;
using Microsoft.Extensions.Options;
using Nito.AsyncEx;

namespace Framework.Features.Definitions;

/// <summary>Store for feature definitions that defined dynamically from an external source like a database.</summary>
public interface IDynamicFeatureDefinitionStore
{
    Task<FeatureDefinition?> GetOrDefaultAsync(string name, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FeatureDefinition>> GetFeaturesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FeatureGroupDefinition>> GetGroupsAsync(CancellationToken cancellationToken = default);

    /// <summary>Save the application static features to the dynamic store.</summary>
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

    /// <summary>
    /// A lock key for the application features update to allow only one instance to try
    /// to save the changes at a time.
    /// </summary>
    private readonly string _appSaveLockKey = $"features:{application.ApplicationName}_update_lock";

    /// <summary>A hash of the application features to check if there are changes and need to save them.</summary>
    private readonly string _appSaveFeaturesHashCacheKey = $"features:{application.ApplicationName}_hash";

    #region Get Methods

    public async Task<FeatureDefinition?> GetOrDefaultAsync(string name, CancellationToken cancellationToken = default)
    {
        if (!_options.IsDynamicFeatureStoreEnabled)
        {
            return null;
        }

        // Fast path: lock-free read if cache is fresh
        if (!_IsUpdateMemoryCacheRequired())
        {
            var cache = _featureMemoryCache; // Eventual consistency: may read stale data if cache invalidated after freshness check
            return cache.GetValueOrDefault(name);
        }

        // Slow path: acquire lock and refresh if needed
        using (await _syncSemaphore.LockAsync(cancellationToken))
        {
            await _EnsureMemoryCacheIsUptoDateAsync(cancellationToken);

            var cache = _featureMemoryCache; // Capture local reference
            return cache.GetValueOrDefault(name);
        }
    }

    public async Task<IReadOnlyList<FeatureDefinition>> GetFeaturesAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.IsDynamicFeatureStoreEnabled)
        {
            return [];
        }

        // Fast path: lock-free read if cache is fresh
        if (!_IsUpdateMemoryCacheRequired())
        {
            var cache = _featureMemoryCache; // Eventual consistency: may read stale data if cache invalidated after freshness check
            return cache.Values.ToImmutableList();
        }

        // Slow path: acquire lock and refresh if needed
        using (await _syncSemaphore.LockAsync(cancellationToken))
        {
            await _EnsureMemoryCacheIsUptoDateAsync(cancellationToken);

            var cache = _featureMemoryCache; // Capture local reference
            return cache.Values.ToImmutableList();
        }
    }

    public async Task<IReadOnlyList<FeatureGroupDefinition>> GetGroupsAsync(
        CancellationToken cancellationToken = default
    )
    {
        if (!_options.IsDynamicFeatureStoreEnabled)
        {
            return [];
        }

        // Fast path: lock-free read if cache is fresh
        if (!_IsUpdateMemoryCacheRequired())
        {
            var cache = _groupMemoryCache; // Capture local reference for thread safety
            return cache.Values.ToImmutableList();
        }

        // Slow path: acquire lock and refresh if needed
        using (await _syncSemaphore.LockAsync(cancellationToken))
        {
            await _EnsureMemoryCacheIsUptoDateAsync(cancellationToken);

            var cache = _groupMemoryCache; // Capture local reference
            return cache.Values.ToImmutableList();
        }
    }

    #endregion

    #region Get Helpers

    private string? _cacheStamp;
    private DateTimeOffset? _lastCheckTime;
    private readonly SemaphoreSlim _syncSemaphore = new(1, 1);
    private volatile ImmutableDictionary<string, FeatureGroupDefinition> _groupMemoryCache = ImmutableDictionary<
        string,
        FeatureGroupDefinition
    >.Empty.WithComparers(StringComparer.Ordinal);
    private volatile ImmutableDictionary<string, FeatureDefinition> _featureMemoryCache = ImmutableDictionary<
        string,
        FeatureDefinition
    >.Empty.WithComparers(StringComparer.Ordinal);

    private async Task _EnsureMemoryCacheIsUptoDateAsync(CancellationToken cancellationToken)
    {
        if (!_IsUpdateMemoryCacheRequired())
        {
            return; // Get the latest feature with a small delay for optimization
        }

        var cacheStamp = await _GetOrSetDistributedCacheStampAsync(cancellationToken);

        if (string.Equals(cacheStamp, _cacheStamp, StringComparison.Ordinal))
        {
            _lastCheckTime = timeProvider.GetUtcNow();

            return;
        }

        await _UpdateInMemoryStoreCacheAsync(cancellationToken);
        _cacheStamp = cacheStamp;
        _lastCheckTime = timeProvider.GetUtcNow();
    }

    private async Task<string> _GetOrSetDistributedCacheStampAsync(CancellationToken cancellationToken)
    {
        var cacheKey = _options.CommonFeaturesUpdatedStampCacheKey;
        var cachedStamp = await distributedCache.GetAsync<string>(cacheKey, cancellationToken);

        if (!cachedStamp.IsNull)
        {
            return cachedStamp.Value;
        }

        await using var resourceLock =
            await resourceLockProvider.TryAcquireAsync(
                resource: _options.CrossApplicationsCommonLockKey,
                timeUntilExpires: _options.CrossApplicationsCommonLockExpiration,
                acquireTimeout: _options.CrossApplicationsCommonLockAcquireTimeout,
                cancellationToken: cancellationToken
            )
            ?? throw new InvalidOperationException(
                "Could not acquire distributed lock for feature definition common stamp check!"
            ); // This request will fail

        cancellationToken.ThrowIfCancellationRequested();

        cachedStamp = await distributedCache.GetAsync<string>(cacheKey, cancellationToken);

        if (!cachedStamp.IsNull)
        {
            return cachedStamp.Value;
        }

        return await _ChangeCommonStampAsync(cancellationToken);
    }

    private async Task _UpdateInMemoryStoreCacheAsync(CancellationToken cancellationToken)
    {
        var featureGroupRecords = await repository.GetGroupsListAsync(cancellationToken);
        var featureRecords = await repository.GetFeaturesListAsync(cancellationToken);

        // Build new caches instead of mutating existing ones
        var newGroupCache = ImmutableDictionary.CreateBuilder<string, FeatureGroupDefinition>(StringComparer.Ordinal);
        var newFeatureCache = ImmutableDictionary.CreateBuilder<string, FeatureDefinition>(StringComparer.Ordinal);

        var context = new FeatureDefinitionContext();

        foreach (var featureGroupRecord in featureGroupRecords)
        {
            var featureGroup = context.AddGroup(featureGroupRecord.Name, featureGroupRecord.DisplayName);

            newGroupCache[featureGroup.Name] = featureGroup;

            foreach (var property in featureGroupRecord.ExtraProperties)
            {
                featureGroup[property.Key] = property.Value;
            }

            var featureRecordsInThisGroup = featureRecords.Where(p =>
                string.Equals(p.GroupName, featureGroup.Name, StringComparison.Ordinal)
            );

            foreach (var featureRecord in featureRecordsInThisGroup.Where(x => x.ParentName is null))
            {
                _UpdateInMemoryStoreCacheAddFeatureRecursively(
                    featureGroup,
                    featureRecord,
                    featureRecords,
                    newFeatureCache
                );
            }
        }

        // Swap references to new immutable caches. Each assignment is atomic, but the two updates are not atomic as a unit,
        // so readers may briefly observe one cache updated while the other is stale. This transient state is acceptable here.
        _groupMemoryCache = newGroupCache.ToImmutable();
        _featureMemoryCache = newFeatureCache.ToImmutable();
    }

    private void _UpdateInMemoryStoreCacheAddFeatureRecursively(
        ICanCreateChildFeature featureContainer,
        FeatureDefinitionRecord featureRecord,
        List<FeatureDefinitionRecord> allFeatureRecords,
        ImmutableDictionary<string, FeatureDefinition>.Builder featureCacheBuilder
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

        featureCacheBuilder[feature.Name] = feature;

        if (!featureRecord.Providers.IsNullOrWhiteSpace())
        {
            feature.Providers.AddRange(featureRecord.Providers.Split(','));
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
            _UpdateInMemoryStoreCacheAddFeatureRecursively(feature, subFeature, allFeatureRecords, featureCacheBuilder);
        }
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
        await using var appResourceLock = await resourceLockProvider.TryAcquireAsync(
            _appSaveLockKey,
            timeUntilExpires: _options.ApplicationSaveLockExpiration,
            acquireTimeout: _options.ApplicationSaveLockAcquireTimeout,
            cancellationToken: cancellationToken
        );

        if (appResourceLock is null)
        {
            return; // Another application instance is already doing it
        }

        cancellationToken.ThrowIfCancellationRequested();

        // NOTE: This can be further optimized by using 4 cache values for:
        // Groups, features, deleted groups and deleted features.
        // But the code would be more complex.
        // This is enough for now.

        var cachedHash = await distributedCache.GetAsync<string>(_appSaveFeaturesHashCacheKey, cancellationToken);
        var groups = await staticStore.GetGroupsAsync(cancellationToken);
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
                resource: _options.CrossApplicationsCommonLockKey,
                timeUntilExpires: _options.CrossApplicationsCommonLockExpiration,
                acquireTimeout: _options.CrossApplicationsCommonLockAcquireTimeout,
                cancellationToken: cancellationToken
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
            await repository.SaveAsync(
                newGroups,
                updatedGroups,
                deletedGroups,
                newFeatures,
                updatedFeatures,
                deletedFeatures,
                cancellationToken
            );

            await _ChangeCommonStampAsync(cancellationToken);
        }

        await distributedCache.UpsertAsync(
            _appSaveFeaturesHashCacheKey,
            currentHash,
            _options.FeaturesHashCacheExpiration,
            cancellationToken
        );
    }

    #endregion

    #region Save Helpers

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
            var dbRecord = dbRecordsMap.GetOrDefault(featureGroupRecord.Name);

            if (dbRecord is null)
            {
                newRecords.Add(featureGroupRecord); // New

                continue;
            }

            if (featureGroupRecord.HasSameData(dbRecord))
            {
                continue; // Not changed
            }

            dbRecord.Patch(featureGroupRecord); // Changed
            changedRecords.Add(dbRecord);
        }

        // Handle deleted records
        if (_providers.DeletedFeatureGroups.Count != 0)
        {
            deletedRecords.AddRange(dbRecords.Where(x => _providers.DeletedFeatureGroups.Contains(x.Name)));
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
            var dbRecord = dbRecordsMap.GetOrDefault(featureRecord.Name);

            if (dbRecord is null) // New
            {
                newRecords.Add(featureRecord);

                continue;
            }

            if (featureRecord.HasSameData(dbRecord)) // Not changed
            {
                continue;
            }

            dbRecord.Patch(featureRecord); // Changed
            changedRecords.Add(dbRecord);
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

    private static readonly JsonSerializerOptions _JsonSerializerOptions = _CreateHashJsonSerializerOptions();

    private static JsonSerializerOptions _CreateHashJsonSerializerOptions()
    {
        return new()
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
    }

    #endregion

    #region Helpers

    private async Task<string> _ChangeCommonStampAsync(CancellationToken cancellationToken)
    {
        var stamp = guidGenerator.Create().ToString("N");

        await distributedCache.UpsertAsync(
            _options.CommonFeaturesUpdatedStampCacheKey,
            stamp,
            _options.CommonFeaturesUpdatedStampCacheExpiration,
            cancellationToken
        );

        return stamp;
    }

    #endregion
}
