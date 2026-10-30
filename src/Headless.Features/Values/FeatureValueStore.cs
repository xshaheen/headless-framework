// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Checks;
using Headless.UnitOfWork;
using Microsoft.Extensions.Logging;

namespace Headless.Features;

/// <summary>
/// Cache-backed implementation of <see cref="IFeatureValueStore"/> that reads from cache on first access
/// (populating all values for the provider in one shot) and delegates persistence to
/// <see cref="IFeatureValueRecordRepository"/>.
/// </summary>
public sealed class FeatureValueStore(
    IFeatureDefinitionManager featureDefinitionManager,
    IFeatureValueRecordRepository repository,
    IGuidGenerator guidGenerator,
    ICache cache,
    ILogger<FeatureValueStore> logger
) : IFeatureValueStore
{
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromHours(5);

    /// <summary>How many times a batch is planned and saved before a concurrent-writer collision is surfaced.</summary>
    private const int _MaxSaveAttempts = 3;

    private static void _EnsureKey(string? providerName, string? providerKey, string? name = null)
    {
        Argument.IsPortableKey(providerName);
        Argument.IsPortableKey(providerKey);
        Argument.IsPortableKey(name);
    }

    private static void _EnsureNames(IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            Argument.IsPortableKey(name);
        }
    }

    /// <inheritdoc/>
    public async Task<string?> GetOrDefaultAsync(
        string name,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        _EnsureKey(providerName, providerKey, name);

        var cacheKey = FeatureValueCacheItem.CalculateCacheKey(name, providerName, providerKey);
        var existValueCacheItem = await cache
            .GetAsync<FeatureValueCacheItem>(cacheKey, cancellationToken)
            .ConfigureAwait(false);

        if (existValueCacheItem.HasValue)
        {
            return existValueCacheItem.Value?.Value;
        }

        return await _CacheAllAndGetAsync(
                providerName,
                providerKey,
                featureNameToFind: name,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<List<FeatureValue>> GetAllAsync(
        HashSet<string> names,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(names);
        _EnsureKey(providerName, providerKey);
        _EnsureNames(names);

        if (names.Count == 1)
        {
            var name = names.First();
            var value = await GetOrDefaultAsync(name, providerName, providerKey, cancellationToken)
                .ConfigureAwait(false);

            return [new FeatureValue(name, value)];
        }

        return await _GetCachedItemsAsync(names, providerName, providerKey, cancellationToken).ConfigureAwait(false);
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
        _EnsureKey(providerName, providerKey, name);

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
        _EnsureKey(providerName, providerKey);

        foreach (var name in values.Keys)
        {
            Argument.IsPortableKey(name);
        }

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
        _EnsureKey(providerName, providerKey, name);

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

    /// <inheritdoc/>
    public async Task<string?> GetOrDefaultEnlistedAsync(
        IUnitOfWork unitOfWork,
        string name,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(unitOfWork);
        _EnsureKey(providerName, providerKey, name);

        // Not through the cache: the unit may already have written this value, and the cache only learns of it after
        // the commit.
        var records = await repository
            .GetListEnlistedAsync(unitOfWork, [name], providerName, providerKey, cancellationToken)
            .ConfigureAwait(false);

        return records is [var record, ..] ? record.Value : null;
    }

    /// <inheritdoc/>
    public async Task SetAllEnlistedAsync(
        IUnitOfWork unitOfWork,
        IReadOnlyDictionary<string, string?> values,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(unitOfWork);
        Argument.IsNotNull(values);
        Argument.IsNotNull(providerName);
        _EnsureKey(providerName, providerKey);
        _EnsureNames(values.Keys);

        if (values.Count == 0)
        {
            return;
        }

        // One attempt, unlike SetAllAsync: a failed statement aborts the unit's transaction, so only the unit's owner
        // can decide whether to run the whole unit again.
        var existingRecords = await repository
            .GetListEnlistedAsync(
                unitOfWork,
                values.Keys.ToHashSet(StringComparer.Ordinal),
                providerName,
                providerKey,
                cancellationToken
            )
            .ConfigureAwait(false);

        var changes = _PlanChanges(values, existingRecords, providerName, providerKey);

        await repository
            .SaveEnlistedAsync(unitOfWork, changes.Inserted, changes.Updated, changes.Deleted, cancellationToken)
            .ConfigureAwait(false);

        FeatureValueCacheInvalidation.RemoveAfterCommit(
            unitOfWork,
            cache,
            [.. changes.CacheItems.Keys, .. changes.RemovedCacheKeys],
            logger
        );
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> DeleteEnlistedAsync(
        IUnitOfWork unitOfWork,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(unitOfWork);
        _EnsureKey(providerName, providerKey);

        var records = await repository
            .GetListEnlistedAsync(unitOfWork, providerName, providerKey, cancellationToken)
            .ConfigureAwait(false);

        if (records.Count == 0)
        {
            return [];
        }

        await repository.SaveEnlistedAsync(unitOfWork, [], [], records, cancellationToken).ConfigureAwait(false);

        FeatureValueCacheInvalidation.RemoveAfterCommit(
            unitOfWork,
            cache,
            [.. records.Select(x => FeatureValueCacheItem.CalculateCacheKey(x.Name, providerName, providerKey))],
            logger
        );

        return records.ConvertAll(x => x.Name);
    }

    #region Helpers

    /// <summary>Returns feature values from the cache, fetching and caching any misses from the repository.</summary>
    private async Task<List<FeatureValue>> _GetCachedItemsAsync(
        HashSet<string> names,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken
    )
    {
        var keyToName = new Dictionary<string, string>(names.Count, StringComparer.Ordinal);
        var cacheKeys = new List<string>(names.Count);

        foreach (var name in names)
        {
            var key = FeatureValueCacheItem.CalculateCacheKey(name, providerName, providerKey);
            cacheKeys.Add(key);
            keyToName[key] = name;
        }

        var existCacheItemsMap = await cache
            .GetAllAsync<FeatureValueCacheItem>(cacheKeys, cancellationToken)
            .ConfigureAwait(false);
        var existCacheItems = existCacheItemsMap.ToList();

        if (existCacheItems.TrueForAll(x => x.Value.HasValue))
        {
            return existCacheItems.ConvertAll(item => new FeatureValue(
                keyToName.TryGetValue(item.Key, out var n) ? n : _GetFeatureNameFromCacheKey(item.Key),
                item.Value.Value?.Value
            ));
        }

        // Some cache items aren't found in the cache, get them from the database
        var notCacheNames = existCacheItems
            .Where(x => !x.Value.HasValue)
            .Select(x => keyToName.TryGetValue(x.Key, out var n) ? n : _GetFeatureNameFromCacheKey(x.Key))
            .ToHashSet(StringComparer.Ordinal);

        var newCacheItemsMap = await _CacheSomeAsync(notCacheNames, providerName, providerKey, cancellationToken)
            .ConfigureAwait(false);

        var result = new List<FeatureValue>(cacheKeys.Count);

        foreach (var cacheKey in cacheKeys)
        {
            var featureName = keyToName.TryGetValue(cacheKey, out var n) ? n : _GetFeatureNameFromCacheKey(cacheKey);

            if (newCacheItemsMap.TryGetValue(cacheKey, out var newCachedValue))
            {
                result.Add(new FeatureValue(featureName, newCachedValue.Value));

                continue;
            }

            if (existCacheItemsMap.TryGetValue(cacheKey, out var cacheItem))
            {
                result.Add(new FeatureValue(featureName, cacheItem.Value?.Value));

                continue;
            }

            result.Add(new FeatureValue(featureName, Value: null));
        }

        return result;
    }

    /// <summary>Fetches the specified feature names from the repository and populates the cache.</summary>
    private async Task<Dictionary<string, FeatureValueCacheItem>> _CacheSomeAsync(
        HashSet<string> names,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var definitions = await _GetDbFeatureDefinitionsAsync(names, cancellationToken).ConfigureAwait(false);
        var dbValuesMap = await _GetProviderValuesMapAsync(names, providerName, providerKey, cancellationToken)
            .ConfigureAwait(false);

        var cacheItems = new Dictionary<string, FeatureValueCacheItem>(StringComparer.Ordinal);

        foreach (var definition in definitions)
        {
            var cacheKey = FeatureValueCacheItem.CalculateCacheKey(definition.Name, providerName, providerKey);
            var featureValue = dbValuesMap.GetOrDefault(definition.Name);
            cacheItems[cacheKey] = new FeatureValueCacheItem(featureValue);
        }

        await cache.UpsertAllAsync(cacheItems, _cacheExpiration, cancellationToken).ConfigureAwait(false);

        return cacheItems;
    }

    /// <summary>Loads all feature values for the provider/key into the cache and returns the value for <c>featureNameToFind</c>.</summary>
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
            cacheItems[cacheKey] = new FeatureValueCacheItem(featureValue);

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

    /// <summary>Returns feature definitions that match the requested <paramref name="names"/>.</summary>
    private async Task<IEnumerable<FeatureDefinition>> _GetDbFeatureDefinitionsAsync(
        HashSet<string> names,
        CancellationToken cancellationToken = default
    )
    {
        if (names.Count == 0)
        {
            return [];
        }

        var definitions = await featureDefinitionManager.GetFeaturesAsync(cancellationToken).ConfigureAwait(false);

        return definitions.Where(definition => names.Contains(definition.Name));
    }

    /// <summary>Fetches stored feature values for the given <paramref name="names"/> under a provider/key pair and returns them as a name-to-value map.</summary>
    private async Task<Dictionary<string, string>> _GetProviderValuesMapAsync(
        HashSet<string> names,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken
    )
    {
        var dbValues = await repository
            .GetListAsync(names, providerName, providerKey, cancellationToken)
            .ConfigureAwait(false);
        return dbValues.ToDictionary(s => s.Name, s => s.Value, StringComparer.Ordinal);
    }

    /// <summary>Extracts the feature name from a cache key, throwing if the key is malformed.</summary>
    /// <exception cref="InvalidOperationException">The <paramref name="key"/> does not match the expected cache key format.</exception>
    private static string _GetFeatureNameFromCacheKey(string key)
    {
        var featureName = FeatureValueCacheItem.GetFeatureNameFromCacheKey(key);
        Ensure.True(featureName is not null, $"Invalid feature cache key `{key}` feature name not found");

        return featureName;
    }

    #endregion Helpers
}

/// <summary>Persistent store for per-provider feature values.</summary>
/// <remarks>
/// The default implementation caches values per provider/key in a distributed cache. On the first
/// read for a given provider scope all values are fetched from the database and cached together,
/// so subsequent reads for the same scope are served from cache. Mutations (<see cref="SetAsync"/>,
/// <see cref="DeleteAsync"/>) update both the database and the cache immediately.
/// <para>
/// Every member throws <see cref="ArgumentException"/> before touching storage when a feature name, provider name, or
/// provider key is text some provider would not keep unchanged: surrounding white space (SQL Server ignores trailing spaces when
/// it compares keys, so <c>"acme"</c> and <c>"acme "</c> would address one row there and two on PostgreSQL), a NUL
/// character (PostgreSQL cannot store it), or an unpaired UTF-16 surrogate (SqlClient rewrites it to U+FFFD, merging
/// distinct keys). See <c>Argument.IsPortableKey</c>.
/// </para>
/// <para>
/// New members arrive as default interface members. The enlisted members (<c>*EnlistedAsync</c>) default to throwing
/// <see cref="NotSupportedException" />.
/// </para>
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

    /// <summary>Returns the stored values for the specified feature <paramref name="names"/>.</summary>
    /// <param name="names">The set of feature names to retrieve.</param>
    /// <param name="providerName">The provider name.</param>
    /// <param name="providerKey">An optional key that qualifies the provider scope.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A list of <see cref="FeatureValue"/> entries; a <see langword="null"/> <c>Value</c> means the feature has no stored entry.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="names"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="names"/> is empty.</exception>
    Task<List<FeatureValue>> GetAllAsync(
        HashSet<string> names,
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

    /// <summary>
    /// Reads the stored value inside <paramref name="unitOfWork" />'s transaction, bypassing the cache, so a value the
    /// unit already wrote is seen.
    /// </summary>
    /// <param name="unitOfWork">The unit whose transaction runs the read.</param>
    /// <param name="name">The feature name.</param>
    /// <param name="providerName">The provider name.</param>
    /// <param name="providerKey">The provider-scoped key, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">The abort token.</param>
    /// <returns>The stored value, or <see langword="null"/> when the scope stores none.</returns>
    /// <exception cref="NotSupportedException">This store or its repository cannot run inside a unit of work.</exception>
    Task<string?> GetOrDefaultEnlistedAsync(
        IUnitOfWork unitOfWork,
        string name,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken
    )
    {
        throw _NotEnlistable();
    }

    /// <summary>
    /// Writes several values for one scope inside <paramref name="unitOfWork" />'s transaction: the same writes as
    /// <see cref="SetAllAsync"/> in one attempt, with the cache cleared after the unit commits.
    /// </summary>
    /// <param name="unitOfWork">The unit whose transaction holds the writes.</param>
    /// <param name="values">The values; a <see langword="null"/> value deletes the stored value.</param>
    /// <param name="providerName">The provider name.</param>
    /// <param name="providerKey">The provider-scoped key, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">The abort token.</param>
    /// <exception cref="InvalidOperationException">The unit cannot host the writes.</exception>
    /// <exception cref="NotSupportedException">This store or its repository cannot run inside a unit of work.</exception>
    Task SetAllEnlistedAsync(
        IUnitOfWork unitOfWork,
        IReadOnlyDictionary<string, string?> values,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken
    )
    {
        throw _NotEnlistable();
    }

    /// <summary>
    /// Deletes every value of one scope inside <paramref name="unitOfWork" />'s transaction, and clears their cache
    /// entries after the unit commits.
    /// </summary>
    /// <param name="unitOfWork">The unit whose transaction holds the delete.</param>
    /// <param name="providerName">The provider name.</param>
    /// <param name="providerKey">The provider-scoped key, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">The abort token.</param>
    /// <returns>The names whose values were deleted; empty when the scope stored none.</returns>
    /// <exception cref="InvalidOperationException">The unit cannot host the delete.</exception>
    /// <exception cref="NotSupportedException">This store or its repository cannot run inside a unit of work.</exception>
    Task<IReadOnlyList<string>> DeleteEnlistedAsync(
        IUnitOfWork unitOfWork,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken
    )
    {
        throw _NotEnlistable();
    }

    private static NotSupportedException _NotEnlistable()
    {
        return new NotSupportedException("The configured feature value store cannot run inside a unit of work.");
    }
}
