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
