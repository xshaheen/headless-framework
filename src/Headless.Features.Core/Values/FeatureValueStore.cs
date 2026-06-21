// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Caching;
using Headless.Features.Definitions;
using Headless.Features.Entities;
using Headless.Features.Repositories;
using Humanizer;

namespace Headless.Features.Values;

/// <summary>Persistent store for per-provider feature values.</summary>
/// <remarks>
/// The default implementation caches values per provider/key in a distributed cache. On the first
/// read for a given provider scope all values are fetched from the database and cached together,
/// so subsequent reads for the same scope are served from cache. Mutations (<see cref="SetAsync"/>,
/// <see cref="DeleteAsync"/>) update both the database and the cache immediately.
/// </remarks>
public interface IFeatureValueStore
{
    /// <summary>Returns the stored value for feature <paramref name="name"/> under <paramref name="providerName"/>/<paramref name="providerKey"/>, or <see langword="null"/> if not set.</summary>
    /// <param name="name">The feature name.</param>
    /// <param name="providerName">The provider name (e.g. <c>"Tenant"</c>).</param>
    /// <param name="providerKey">An optional key that qualifies the provider scope.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The stored string value, or <see langword="null"/> when no value has been persisted.</returns>
    Task<string?> GetOrDefaultAsync(
        string name,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>Upserts the value of feature <paramref name="name"/> for <paramref name="providerName"/>/<paramref name="providerKey"/>.</summary>
    /// <param name="name">The feature name.</param>
    /// <param name="value">The value to store.</param>
    /// <param name="providerName">The provider name.</param>
    /// <param name="providerKey">An optional key that qualifies the provider scope.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task SetAsync(
        string name,
        string value,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>Deletes all stored values for feature <paramref name="name"/> matching <paramref name="providerName"/>/<paramref name="providerKey"/>.</summary>
    /// <param name="name">The feature name.</param>
    /// <param name="providerName">The provider name.</param>
    /// <param name="providerKey">An optional key that qualifies the provider scope.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task DeleteAsync(
        string name,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );
}

/// <summary>
/// Cache-backed implementation of <see cref="IFeatureValueStore"/> that reads from cache on first access
/// (populating all values for the provider in one shot) and delegates persistence to
/// <see cref="IFeatureValueRecordRepository"/>.
/// </summary>
public sealed class FeatureValueStore(
    IFeatureDefinitionManager featureDefinitionManager,
    IFeatureValueRecordRepository repository,
    IGuidGenerator guidGenerator,
    ICache cache
) : IFeatureValueStore
{
    private readonly TimeSpan _cacheExpiration = 5.Hours();

    /// <inheritdoc/>
    public async Task<string?> GetOrDefaultAsync(
        string name,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var cacheKey = FeatureValueCacheItem.CalculateCacheKey(name, providerName, providerKey);
        var existValueCacheItem = await cache
            .GetAsync<FeatureValueCacheItem>(cacheKey, cancellationToken)
            .ConfigureAwait(false);

        if (existValueCacheItem.HasValue)
        {
            return existValueCacheItem.Value?.Value;
        }

        var valueCacheItem = await _CacheAllAndGetAsync(
                providerName,
                providerKey,
                featureNameToFind: name,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        return valueCacheItem;
    }

    /// <inheritdoc/>
    public async Task SetAsync(
        string name,
        string value,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var featureValue = await repository
            .FindAsync(name, providerName, providerKey, cancellationToken)
            .ConfigureAwait(false);

        if (featureValue is null)
        {
            featureValue = new FeatureValueRecord(guidGenerator.Create(), name, value, providerName, providerKey);
            await repository.InsertAsync(featureValue, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            featureValue.Value = value;
            await repository.UpdateAsync(featureValue, cancellationToken).ConfigureAwait(false);
        }

        await cache
            .UpsertAsync(
                key: FeatureValueCacheItem.CalculateCacheKey(name, providerName, providerKey),
                value: new FeatureValueCacheItem(featureValue.Value),
                expiration: _cacheExpiration,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(
        string name,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var features = await repository
            .FindAllAsync(name, providerName, providerKey, cancellationToken)
            .ConfigureAwait(false);

        if (features.Count == 0)
        {
            return;
        }

        await repository.DeleteAsync(features, cancellationToken).ConfigureAwait(false);

        foreach (var featureValue in features)
        {
            var cacheKey = FeatureValueCacheItem.CalculateCacheKey(name, providerName, featureValue.ProviderKey);
            await cache.RemoveAsync(cacheKey, cancellationToken).ConfigureAwait(false);
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
        var definitions = await featureDefinitionManager.GetFeaturesAsync(cancellationToken).ConfigureAwait(false);
        var dbValuesMap = await _GetProviderValuesMapAsync(providerName, providerKey, cancellationToken)
            .ConfigureAwait(false);

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

        await cache.UpsertAllAsync(cacheItems, _cacheExpiration, cancellationToken).ConfigureAwait(false);

        return featureValueToFind;
    }

    private async Task<Dictionary<string, string>> _GetProviderValuesMapAsync(
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken
    )
    {
        var dbValues = await repository
            .GetListAsync(providerName, providerKey, cancellationToken)
            .ConfigureAwait(false);
        return dbValues.ToDictionary(s => s.Name, s => s.Value, StringComparer.Ordinal);
    }

    #endregion
}
