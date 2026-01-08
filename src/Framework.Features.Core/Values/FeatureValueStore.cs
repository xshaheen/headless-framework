// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Abstractions;
using Framework.Caching;
using Framework.Features.Definitions;
using Framework.Features.Entities;
using Framework.Features.Repositories;
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
    IFeatureValueRecordRepository repository,
    IGuidGenerator guidGenerator,
    IDistributedCache<FeatureValueCacheItem> cache
) : IFeatureValueStore
{
    private readonly TimeSpan _cacheExpiration = 5.Hours();

    public async Task<string?> GetOrDefaultAsync(
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
            return existValueCacheItem.Value?.Value;
        }

        var valueCacheItem = await _CacheAllAndGetAsync(
            providerName,
            providerKey,
            featureNameToFind: name,
            cancellationToken: cancellationToken
        );

        return valueCacheItem;
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

        await cache.UpsertAsync(
            cacheKey: FeatureValueCacheItem.CalculateCacheKey(name, providerName, providerKey),
            cacheValue: new FeatureValueCacheItem(featureValue.Value),
            expiration: _cacheExpiration,
            cancellationToken: cancellationToken
        );
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

    private async Task<string?> _CacheAllAndGetAsync(
        string providerName,
        string? providerKey,
        string featureNameToFind,
        CancellationToken cancellationToken = default
    )
    {
        var definitions = await featureDefinitionManager.GetFeaturesAsync(cancellationToken);
        var dbValues = await repository.GetListAsync(providerName, providerKey, cancellationToken);
        var dbValuesMap = dbValues.ToDictionary(s => s.Name, s => s.Value, StringComparer.Ordinal);

        Dictionary<string, FeatureValueCacheItem> cacheItems = new(StringComparer.Ordinal);
        string? featureValueToFind = null;

        foreach (var featureDefinition in definitions)
        {
            var cacheKey = FeatureValueCacheItem.CalculateCacheKey(featureDefinition.Name, providerName, providerKey);
            var featureValue = dbValuesMap.GetOrDefault(featureDefinition.Name);
            var featureValueCacheItem = new FeatureValueCacheItem(featureValue);
            cacheItems[cacheKey] = featureValueCacheItem;

            if (string.Equals(featureDefinition.Name, featureNameToFind, StringComparison.Ordinal))
            {
                featureValueToFind = featureValue;
            }
        }

        await cache.UpsertAllAsync(cacheItems, _cacheExpiration, cancellationToken);

        return featureValueToFind;
    }

    #endregion
}
