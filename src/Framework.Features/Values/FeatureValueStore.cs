// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Caching;
using Framework.Features.Definitions;
using Framework.Features.Entities;
using Framework.Kernel.BuildingBlocks.Abstractions;

namespace Framework.Features.Values;

public interface IFeatureStore
{
    Task<string?> GetOrDefaultAsync(
        string name,
        string? providerName,
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

public sealed class FeatureStore(
    IFeatureDefinitionManager featureDefinitionManager,
    IFeatureValueRepository repository,
    IGuidGenerator guidGenerator,
    ICache<FeatureValueCacheItem> cache
) : IFeatureStore
{
    public async Task<string?> GetOrDefaultAsync(
        string name,
        string? providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var cacheItem = await _GetCacheItemAsync(name, providerName, providerKey);

        return cacheItem.Value;
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

        if (featureValue == null)
        {
            featureValue = new FeatureValueRecord(guidGenerator.Create(), name, value, providerName, providerKey);
            await repository.InsertAsync(featureValue);
        }
        else
        {
            featureValue.Value = value;
            await repository.UpdateAsync(featureValue, cancellationToken);
        }

        await cache.UpsertAsync(
            _CalculateCacheKey(name, providerName, providerKey),
            new FeatureValueCacheItem(featureValue?.Value),
            considerUow: true
        );
    }

    public async Task DeleteAsync(
        string name,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var featureValues = await repository.FindAllAsync(name, providerName, providerKey, cancellationToken);

        foreach (var featureValue in featureValues)
        {
            await repository.DeleteAsync(featureValue, cancellationToken);
            await cache.RemoveAsync(_CalculateCacheKey(name, providerName, providerKey), cancellationToken);
        }
    }

    private async Task<FeatureValueCacheItem> _GetCacheItemAsync(
        string name,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var cacheKey = _CalculateCacheKey(name, providerName, providerKey);
        var cacheItem = await cache.GetAsync(cacheKey, cancellationToken);

        if (cacheItem != null)
        {
            return cacheItem;
        }

        cacheItem = new FeatureValueCacheItem(null);

        await _SetCacheItemsAsync(providerName, providerKey, name, cacheItem);

        return cacheItem;
    }

    private async Task _SetCacheItemsAsync(
        string providerName,
        string providerKey,
        string currentName,
        FeatureValueCacheItem currentCacheItem,
        CancellationToken cancellationToken = default
    )
    {
        var featureDefinitions = await featureDefinitionManager.GetAllAsync(cancellationToken);
        var dbRecords = await repository.GetListAsync(providerName, providerKey, cancellationToken);
        var dbRecordsMap = dbRecords.ToDictionary(s => s.Name, s => s.Value, StringComparer.Ordinal);

        var cacheItems = new List<KeyValuePair<string, FeatureValueCacheItem>>();

        foreach (var featureDefinition in featureDefinitions)
        {
            var featureValue = dbRecordsMap.GetOrDefault(featureDefinition.Name);

            cacheItems.Add(
                new KeyValuePair<string, FeatureValueCacheItem>(
                    _CalculateCacheKey(featureDefinition.Name, providerName, providerKey),
                    new FeatureValueCacheItem(featureValue)
                )
            );

            if (string.Equals(featureDefinition.Name, currentName, StringComparison.Ordinal))
            {
                currentCacheItem.Value = featureValue;
            }
        }

        await cache.SetManyAsync(cacheItems);
    }

    private static string _CalculateCacheKey(string name, string providerName, string? providerKey)
    {
        return FeatureValueCacheItem.CalculateCacheKey(name, providerName, providerKey);
    }
}
