// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Caching;
using Headless.Checks;
using Headless.Settings.Definitions;
using Headless.Settings.Entities;
using Headless.Settings.Models;
using Headless.Settings.Repositories;
using Microsoft.Extensions.Options;

namespace Headless.Settings.Values;

/// <summary>
/// Persistence and caching layer for raw setting values. Abstracts repository access and manages
/// the <see cref="SettingValueCacheItem"/> cache so callers never interact with the store directly.
/// </summary>
public interface ISettingValueStore
{
    /// <summary>Returns the stored value for the given setting, provider, and key, or <see langword="null"/> if not set.</summary>
    /// <param name="name">The setting name.</param>
    /// <param name="providerName">The provider name (e.g. <c>Global</c>, <c>Tenant</c>).</param>
    /// <param name="providerKey">The provider-scoped key, or <see langword="null"/> for global providers.</param>
    /// <param name="cancellationToken">The abort token.</param>
    /// <returns>The stored value, or <see langword="null"/> if not found.</returns>
    Task<string?> GetOrDefaultAsync(
        string name,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>Returns all setting values stored for the given provider and key, bypassing the per-name cache.</summary>
    /// <param name="providerName">The provider name.</param>
    /// <param name="providerKey">The provider-scoped key, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">The abort token.</param>
    /// <returns>All persisted values for the provider/key pair.</returns>
    Task<List<SettingValue>> GetAllProviderValuesAsync(
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>Returns the stored values for the specified setting <paramref name="names"/>.</summary>
    /// <param name="names">The set of setting names to retrieve.</param>
    /// <param name="providerName">The provider name.</param>
    /// <param name="providerKey">The provider-scoped key, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">The abort token.</param>
    /// <returns>A list of <see cref="SettingValue"/> entries; a <see langword="null"/> <c>Value</c> means the setting has no stored entry.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="names"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="names"/> is empty.</exception>
    Task<List<SettingValue>> GetAllAsync(
        HashSet<string> names,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>Persists or updates the value for a setting.</summary>
    /// <param name="name">The setting name.</param>
    /// <param name="value">The value to store.</param>
    /// <param name="providerName">The provider name.</param>
    /// <param name="providerKey">The provider-scoped key, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">The abort token.</param>
    Task SetAsync(
        string name,
        string value,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Stores or clears several setting values under one provider scope in a single repository transaction:
    /// either every value changes or none does.
    /// </summary>
    /// <remarks>
    /// When another writer inserts or deletes one of these rows between the read and the save, the save fails as a
    /// whole; the store then reads the rows again, plans the batch against them, and retries, up to three attempts.
    /// The last writer's value wins. Any other failure is rethrown unchanged.
    /// </remarks>
    /// <param name="values">
    /// The values keyed by setting name. A <see langword="null"/> value removes the stored entry for that setting.
    /// </param>
    /// <param name="providerName">The provider name.</param>
    /// <param name="providerKey">The provider-scoped key, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">The abort token.</param>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> or <paramref name="providerName"/> is <see langword="null"/>.</exception>
    Task SetAllAsync(
        IReadOnlyDictionary<string, string?> values,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>Removes the stored value for a setting and invalidates its cache entry.</summary>
    /// <param name="name">The setting name.</param>
    /// <param name="providerName">The provider name.</param>
    /// <param name="providerKey">The provider-scoped key, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">The abort token.</param>
    Task DeleteAsync(
        string name,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );
}

/// <summary>Default <see cref="ISettingValueStore"/> implementation that reads from and writes to the repository, with read-through caching via <see cref="ICache{T}"/>.</summary>
public sealed class SettingValueStore(
    ISettingValueRecordRepository valueRepository,
    ISettingDefinitionManager definitionManager,
    IGuidGenerator guidGenerator,
    ICache<SettingValueCacheItem> cache,
    IOptions<SettingManagementOptions> options
) : ISettingValueStore
{
    private readonly TimeSpan _cacheExpiration = options.Value.ValueCacheExpiration;

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
        var cacheKey = SettingValueCacheItem.CalculateCacheKey(name, providerName, providerKey);
        var existValueCacheItem = await cache.GetAsync(cacheKey, cancellationToken).ConfigureAwait(false);

        if (existValueCacheItem.HasValue)
        {
            return existValueCacheItem.Value?.Value;
        }

        var valueCacheItem = await _CacheAllAndGetAsync(
                providerName,
                providerKey,
                nameToFind: name,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        return valueCacheItem;
    }

    /// <inheritdoc/>
    public async Task<List<SettingValue>> GetAllProviderValuesAsync(
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var settings = await valueRepository
            .GetListAsync(providerName, providerKey, cancellationToken)
            .ConfigureAwait(false);

        return settings.ConvertAll(x => new SettingValue(x.Name, x.Value));
    }

    /// <inheritdoc/>
    public async Task<List<SettingValue>> GetAllAsync(
        HashSet<string> names,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(names);

        if (names.Count == 1)
        {
            var name = names.First();
            var value = await GetOrDefaultAsync(name, providerName, providerKey, cancellationToken)
                .ConfigureAwait(false);

            return [new SettingValue(name, value)];
        }

        var cacheItems = await _GetCachedItemsAsync(names, providerName, providerKey, cancellationToken)
            .ConfigureAwait(false);

        return cacheItems;
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
        var settingValue = await valueRepository
            .FindAsync(name, providerName, providerKey, cancellationToken)
            .ConfigureAwait(false);

        if (settingValue is null)
        {
            settingValue = new SettingValueRecord(guidGenerator.Create(), name, value, providerName, providerKey);
            await valueRepository.InsertAsync(settingValue, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            settingValue.Value = value;
            await valueRepository.UpdateAsync(settingValue, cancellationToken).ConfigureAwait(false);
        }

        var cacheKey = SettingValueCacheItem.CalculateCacheKey(name, providerName, providerKey);

        await cache
            .UpsertAsync(
                cacheKey: cacheKey,
                cacheValue: new SettingValueCacheItem(settingValue.Value),
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
            var existingRecords = await valueRepository
                .GetListAsync(names, providerName, providerKey, cancellationToken)
                .ConfigureAwait(false);

            var readIds = existingRecords.Select(x => x.Id).ToHashSet();
            var changes = _PlanChanges(values, existingRecords, providerName, providerKey);

            try
            {
                await valueRepository
                    .SaveAsync(changes.Inserted, changes.Updated, changes.Deleted, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception) when (attempt < _MaxSaveAttempts && !cancellationToken.IsCancellationRequested)
            {
                // A concurrent writer that inserted or deleted one of these rows between the read and the save fails
                // the whole batch (a unique-key violation, or an update that found no row). Only that collision is
                // worth planning again; a failure against an unchanged scope is the caller's to see.
                var currentRecords = await valueRepository
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
        List<SettingValueRecord> existingRecords,
        string providerName,
        string? providerKey
    )
    {
        var existingByName = existingRecords.ToDictionary(x => x.Name, StringComparer.Ordinal);
        var changes = new BatchChanges();

        foreach (var (name, value) in values)
        {
            var cacheKey = SettingValueCacheItem.CalculateCacheKey(name, providerName, providerKey);
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
                    new SettingValueRecord(guidGenerator.Create(), name, value, providerName, providerKey)
                );
            }
            else
            {
                existing.Value = value;
                changes.Updated.Add(existing);
            }

            changes.CacheItems[cacheKey] = new SettingValueCacheItem(value);
        }

        return changes;
    }

    private sealed class BatchChanges
    {
        public List<SettingValueRecord> Inserted { get; } = [];

        public List<SettingValueRecord> Updated { get; } = [];

        public List<SettingValueRecord> Deleted { get; } = [];

        public Dictionary<string, SettingValueCacheItem> CacheItems { get; } = new(StringComparer.Ordinal);

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
        var settings = await valueRepository
            .FindAllAsync(name, providerName, providerKey, cancellationToken)
            .ConfigureAwait(false);

        if (settings.Count == 0)
        {
            return;
        }

        await valueRepository.DeleteAsync(settings, cancellationToken).ConfigureAwait(false);

        foreach (var setting in settings)
        {
            var cacheKey = SettingValueCacheItem.CalculateCacheKey(name, providerName, setting.ProviderKey);
            await cache.RemoveAsync(cacheKey, cancellationToken).ConfigureAwait(false);
        }
    }

    #region Helpers

    /// <summary>Returns setting values from the cache, fetching and caching any misses from the repository.</summary>
    private async Task<List<SettingValue>> _GetCachedItemsAsync(
        HashSet<string> names,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken
    )
    {
        var cacheKeys = names
            .Select(x => SettingValueCacheItem.CalculateCacheKey(x, providerName, providerKey))
            .ToList();

        var existCacheItemsMap = await cache.GetAllAsync(cacheKeys, cancellationToken).ConfigureAwait(false);
        var existCacheItems = existCacheItemsMap.ToList();

        if (existCacheItems.TrueForAll(x => x.Value.HasValue))
        {
            return existCacheItems.ConvertAll(item => new SettingValue(
                _GetSettingNameFromCacheKey(item.Key),
                item.Value.Value?.Value
            ));
        }

        // Some cache items aren't found in the cache, get them from the database
        var notCacheNames = existCacheItems
            .Where(x => !x.Value.HasValue)
            .Select(x => _GetSettingNameFromCacheKey(x.Key))
            .ToHashSet(StringComparer.Ordinal);

        var newCacheItemsMap = await _CacheSomeAsync(notCacheNames, providerName, providerKey, cancellationToken)
            .ConfigureAwait(false);

        var result = new List<SettingValue>(cacheKeys.Count);

        foreach (var cacheKey in cacheKeys)
        {
            var settingName = _GetSettingNameFromCacheKey(cacheKey);

            if (newCacheItemsMap.TryGetValue(cacheKey, out var newCachedValue))
            {
                result.Add(new SettingValue(settingName, newCachedValue.Value));

                continue;
            }

            if (existCacheItemsMap.TryGetValue(cacheKey, out var cacheItem))
            {
                result.Add(new SettingValue(settingName, cacheItem.Value?.Value));

                continue;
            }

            result.Add(new SettingValue(settingName, Value: null));
        }

        return result;
    }

    /// <summary>Fetches the specified setting names from the repository and populates the cache.</summary>
    private async Task<Dictionary<string, SettingValueCacheItem>> _CacheSomeAsync(
        HashSet<string> names,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var definitions = await _GetDbSettingDefinitionsAsync(names, cancellationToken).ConfigureAwait(false);
        var dbValuesMap = await _GetProviderValuesMapAsync(names, providerName, providerKey, cancellationToken)
            .ConfigureAwait(false);

        var cacheItems = new Dictionary<string, SettingValueCacheItem>(StringComparer.Ordinal);

        foreach (var definition in definitions)
        {
            var cacheKey = SettingValueCacheItem.CalculateCacheKey(definition.Name, providerName, providerKey);
            var settingValue = dbValuesMap.GetOrDefault(definition.Name);
            cacheItems[cacheKey] = new SettingValueCacheItem(settingValue);
        }

        await cache.UpsertAllAsync(cacheItems, _cacheExpiration, cancellationToken).ConfigureAwait(false);

        return cacheItems;
    }

    /// <summary>Loads all settings for the provider/key into the cache and returns the value for <c>nameToFind</c>.</summary>
    private async Task<string?> _CacheAllAndGetAsync(
        string providerName,
        string? providerKey,
        string nameToFind,
        CancellationToken cancellationToken
    )
    {
        var definitions = await definitionManager.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var dbValuesMap = await _GetProviderValuesMapAsync(providerName, providerKey, cancellationToken)
            .ConfigureAwait(false);

        Dictionary<string, SettingValueCacheItem> cacheItems = new(StringComparer.Ordinal);
        string? settingValueToFind = null;

        foreach (var settingDefinition in definitions)
        {
            var cacheKey = SettingValueCacheItem.CalculateCacheKey(settingDefinition.Name, providerName, providerKey);
            var settingValue = dbValuesMap.GetOrDefault(settingDefinition.Name);
            var settingValueCacheItem = new SettingValueCacheItem(settingValue);
            cacheItems[cacheKey] = settingValueCacheItem;

            if (string.Equals(settingDefinition.Name, nameToFind, StringComparison.Ordinal))
            {
                settingValueToFind = settingValue;
            }
        }

        await cache.UpsertAllAsync(cacheItems, _cacheExpiration, cancellationToken).ConfigureAwait(false);

        return settingValueToFind;
    }

    /// <summary>Returns setting definitions that match the requested <paramref name="names"/>.</summary>
    private async Task<IEnumerable<SettingDefinition>> _GetDbSettingDefinitionsAsync(
        HashSet<string> names,
        CancellationToken cancellationToken = default
    )
    {
        if (names.Count == 0)
        {
            return [];
        }

        var definitions = await definitionManager.GetAllAsync(cancellationToken).ConfigureAwait(false);

        return definitions.Where(definition => names.Contains(definition.Name));
    }

    /// <summary>Extracts the setting name from a cache key, throwing if the key is malformed.</summary>
    /// <exception cref="InvalidOperationException">The <paramref name="key"/> does not match the expected cache key format.</exception>
    private static string _GetSettingNameFromCacheKey(string key)
    {
        var settingName = SettingValueCacheItem.GetSettingNameFromCacheKey(key);
        Ensure.True(settingName is not null, $"Invalid setting cache key `{key}` setting name not found");

        return settingName;
    }

    /// <summary>Fetches all stored values for a provider/key pair and returns them as a name-to-value map.</summary>
    private async Task<Dictionary<string, string>> _GetProviderValuesMapAsync(
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken
    )
    {
        var dbValues = await valueRepository
            .GetListAsync(providerName, providerKey, cancellationToken)
            .ConfigureAwait(false);
        return dbValues.ToDictionary(s => s.Name, s => s.Value, StringComparer.Ordinal);
    }

    /// <summary>Fetches stored values for the given <paramref name="names"/> under a provider/key pair and returns them as a name-to-value map.</summary>
    private async Task<Dictionary<string, string>> _GetProviderValuesMapAsync(
        HashSet<string> names,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken
    )
    {
        var dbValues = await valueRepository
            .GetListAsync(names, providerName, providerKey, cancellationToken)
            .ConfigureAwait(false);
        return dbValues.ToDictionary(s => s.Name, s => s.Value, StringComparer.Ordinal);
    }

    #endregion
}
