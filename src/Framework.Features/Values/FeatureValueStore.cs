// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Caching;
using Framework.Features.Definitions;
using Framework.Features.Entities;
using Framework.Kernel.BuildingBlocks.Abstractions;
using Humanizer;

namespace Framework.Features.Values;

public interface IFeatureValueStore
{
    Task<string?> GetOrDefaultAsync(
        string name,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    Task SetAsync(
        string name,
        string value,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    Task DeleteAsync(
        string name,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );
}

public sealed class FeatureValueStore(
    IFeatureDefinitionManager featureDefinitionManager,
    IFeatureValueRepository repository,
    IGuidGenerator guidGenerator,
    ICache<FeatureValueCacheItem> cache
) : IFeatureValueStore
{
    public async Task<string?> GetOrDefaultAsync(
        string name,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var item = await _GetCachedItemAsync(name, providerName, providerKey, cancellationToken);

        return item.Value;
    }

    public async Task SetAsync(
        string name,
        string value,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var featureValue = await repository.FindAsync(name, providerName, providerKey, cancellationToken);

        if (featureValue is null)
        {
            featureValue = new FeatureValueRecord(guidGenerator.Create(), name, value, providerName, providerKey);
            await repository.InsertAsync(featureValue, cancellationToken);
        }
        else
        {
            featureValue.Value = value;
            await repository.UpdateAsync(featureValue, cancellationToken);
        }

        var cacheKey = FeatureValueCacheItem.CalculateCacheKey(name, providerName, providerKey);
        await cache.UpsertAsync(cacheKey, new FeatureValueCacheItem(featureValue.Value), 5.Hours(), cancellationToken);
    }

    public async Task DeleteAsync(
        string name,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var features = await repository.FindAllAsync(name, providerName, providerKey, cancellationToken);

        if (features.Count == 0)
        {
            return;
        }

        await repository.DeleteAsync(features, cancellationToken);

        foreach (var featureValue in features)
        {
            var cacheKey = FeatureValueCacheItem.CalculateCacheKey(name, providerName, featureValue.ProviderKey);
            await cache.RemoveAsync(cacheKey, cancellationToken);
        }
    }

    #region Helpers

    private async Task<FeatureValueCacheItem> _GetCachedItemAsync(
        string name,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var cacheKey = FeatureValueCacheItem.CalculateCacheKey(name, providerName, providerKey);
        var existValueCacheItem = await cache.GetAsync(cacheKey, cancellationToken);

        if (existValueCacheItem.HasValue)
        {
            return existValueCacheItem.Value ?? new FeatureValueCacheItem(value: null);
        }

        var valueCacheItem = await _CacheAllAndGetAsync(
            providerName,
            providerKey,
            nameToFind: name,
            cancellationToken: cancellationToken
        );

        return valueCacheItem;
    }

    private async Task<FeatureValueCacheItem> _CacheAllAndGetAsync(
        string providerName,
        string? providerKey,
        string nameToFind,
        CancellationToken cancellationToken = default
    )
    {
        var definitions = await featureDefinitionManager.GetAllAsync(cancellationToken);
        var dbRecords = await repository.GetListAsync(providerName, providerKey, cancellationToken);
        var dbRecordsMap = dbRecords.ToDictionary(s => s.Name, s => s.Value, StringComparer.Ordinal);

        Dictionary<string, FeatureValueCacheItem> cacheItems = new(StringComparer.Ordinal);
        FeatureValueCacheItem? featureToFind = null;

        foreach (var featureDefinition in definitions)
        {
            var cacheKey = FeatureValueCacheItem.CalculateCacheKey(featureDefinition.Name, providerName, providerKey);
            var featureValue = dbRecordsMap.GetOrDefault(featureDefinition.Name);
            var featureValueCacheItem = new FeatureValueCacheItem(featureValue);
            cacheItems[cacheKey] = featureValueCacheItem;

            if (string.Equals(featureDefinition.Name, nameToFind, StringComparison.Ordinal))
            {
                featureToFind = featureValueCacheItem;
            }
        }

        await cache.UpsertAllAsync(cacheItems, 5.Hours(), cancellationToken);

        return featureToFind ?? new FeatureValueCacheItem(value: null);
    }

    #endregion
}
