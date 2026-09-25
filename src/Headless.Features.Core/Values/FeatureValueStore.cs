// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Caching;
using Headless.Checks;
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

    /// <summary>
    /// Stores or clears several feature values under one provider scope in a single repository transaction:
    /// either every value changes or none does.
    /// </summary>
    /// <remarks>
    /// When another writer inserts or deletes one of these rows between the read and the save, the save fails as a
    /// whole; the store then reads the rows again, plans the batch against them, and retries, up to three attempts.
    /// The last writer's value wins. Any other failure is rethrown unchanged.
    /// </remarks>
    /// <param name="values">
    /// The values keyed by feature name. A <see langword="null"/> value removes the stored entry for that feature.
    /// </param>
    /// <param name="providerName">The provider name.</param>
    /// <param name="providerKey">An optional key that qualifies the provider scope.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> or <paramref name="providerName"/> is <see langword="null"/>.</exception>
    Task SetAllAsync(
        IReadOnlyDictionary<string, string?> values,
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

    /// <summary>How many times a batch is planned and saved before a concurrent-writer collision is surfaced.</summary>
    private const int _MaxSaveAttempts = 3;

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
    public async Task SetAllAsync(
        IReadOnlyDictionary<string, string?> values,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(values);
        Argument.IsNotNull(providerName);

        if (values.Count == 0)
        {
            return;
        }

        var names = values.Keys.ToHashSet(StringComparer.Ordinal);

        for (var attempt = 1; ; attempt++)
        {
            var existingRecords = await repository
                .GetListAsync(names, providerName, providerKey, cancellationToken)
                .ConfigureAwait(false);

            var readIds = existingRecords.Select(x => x.Id).ToHashSet();
            var changes = _PlanChanges(values, existingRecords, providerName, providerKey);

            try
            {
                await repository
                    .SaveAsync(changes.Inserted, changes.Updated, changes.Deleted, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception) when (attempt < _MaxSaveAttempts && !cancellationToken.IsCancellationRequested)
            {
                // A concurrent writer that inserted or deleted one of these rows between the read and the save fails
                // the whole batch (a unique-key violation, or an update that found no row). Only that collision is
                // worth planning again; a failure against an unchanged scope is the caller's to see.
                var currentRecords = await repository
                    .GetListAsync(names, providerName, providerKey, cancellationToken)
                    .ConfigureAwait(false);

                if (currentRecords.Select(x => x.Id).ToHashSet().SetEquals(readIds))
                {
                    throw;
                }

                continue;
            }

            if (changes.CacheItems.Count != 0)
            {
                await cache
                    .UpsertAllAsync(changes.CacheItems, _cacheExpiration, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (changes.RemovedCacheKeys.Count != 0)
            {
                await cache.RemoveAllAsync(changes.RemovedCacheKeys, cancellationToken).ConfigureAwait(false);
            }

            return;
        }
    }

    /// <summary>Splits the requested values into the inserts, updates, and deletes that bring the stored rows to them.</summary>
    private BatchChanges _PlanChanges(
        IReadOnlyDictionary<string, string?> values,
        List<FeatureValueRecord> existingRecords,
        string providerName,
        string? providerKey
    )
    {
        var existingByName = existingRecords.ToDictionary(x => x.Name, StringComparer.Ordinal);
        var changes = new BatchChanges();

        foreach (var (name, value) in values)
        {
            var cacheKey = FeatureValueCacheItem.CalculateCacheKey(name, providerName, providerKey);
            existingByName.TryGetValue(name, out var existing);

            if (value is null)
            {
                if (existing is not null)
                {
                    changes.Deleted.Add(existing);
                }

                changes.RemovedCacheKeys.Add(cacheKey);

                continue;
            }

            if (existing is null)
            {
                changes.Inserted.Add(
                    new FeatureValueRecord(guidGenerator.Create(), name, value, providerName, providerKey)
                );
            }
            else
            {
                existing.Value = value;
                changes.Updated.Add(existing);
            }

            changes.CacheItems[cacheKey] = new FeatureValueCacheItem(value);
        }

        return changes;
    }

    private sealed class BatchChanges
    {
        public List<FeatureValueRecord> Inserted { get; } = [];

        public List<FeatureValueRecord> Updated { get; } = [];

        public List<FeatureValueRecord> Deleted { get; } = [];

        public Dictionary<string, FeatureValueCacheItem> CacheItems { get; } = new(StringComparer.Ordinal);

        public List<string> RemovedCacheKeys { get; } = [];
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
