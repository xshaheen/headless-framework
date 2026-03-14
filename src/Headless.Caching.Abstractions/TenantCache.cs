// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

/// <summary>
/// Decorator over <see cref="ICache"/> that prefixes all keys with <c>t:{tenantId}:</c>,
/// providing transparent tenant isolation for any cache item type.
/// </summary>
/// <remarks>
/// Safe to register as singleton — <paramref name="tenantIdProvider"/> is invoked on each
/// operation, reading from ambient tenant context (e.g. <c>ICurrentTenant.Id</c>).
/// </remarks>
public sealed class TenantCache<T>(ICache cache, Func<string?> tenantIdProvider) : ICache<T>
{
    private string _GetPrefix() => $"t:{tenantIdProvider()}:";
    private string _ScopeKey(string key) => $"{_GetPrefix()}{key}";

    public ValueTask<CacheValue<T>> GetOrAddAsync(
        string key,
        Func<CancellationToken, ValueTask<T?>> factory,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.GetOrAddAsync(
            _ScopeKey(key),
            factory,
            expiration,
            cancellationToken
        );
    }

    #region Update

    public ValueTask<bool> UpsertAsync(
        string cacheKey,
        T? cacheValue,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.UpsertAsync(_ScopeKey(cacheKey), cacheValue, expiration, cancellationToken);
    }

    public ValueTask<int> UpsertAllAsync(
        IDictionary<string, T> value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        var scoped = new Dictionary<string, T>(value.Count, StringComparer.Ordinal);

        foreach (var (key, val) in value)
        {
            scoped[_ScopeKey(key)] = val;
        }

        return cache.UpsertAllAsync(scoped, expiration, cancellationToken);
    }

    public ValueTask<bool> TryAddAsync(
        string cacheKey,
        T? cacheValue,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.TryInsertAsync(_ScopeKey(cacheKey), cacheValue, expiration, cancellationToken);
    }

    public ValueTask<bool> TryReplaceAsync(
        string key,
        T? value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.TryReplaceAsync(_ScopeKey(key), value, expiration, cancellationToken);
    }

    public ValueTask<bool> TryReplaceIfEqualAsync(
        string key,
        T? value,
        T? expected,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.TryReplaceIfEqualAsync(_ScopeKey(key), expected, value, expiration, cancellationToken);
    }

    public ValueTask<long> SetAddAsync(
        string key,
        IEnumerable<T> value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.SetAddAsync(_ScopeKey(key), value, expiration, cancellationToken);
    }

    #endregion

    #region Get

    public async ValueTask<IDictionary<string, CacheValue<T>>> GetAllAsync(
        IEnumerable<string> cacheKeys,
        CancellationToken cancellationToken = default
    )
    {
        // Build a scoped→original mapping to reliably restore keys
        var keyMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var scopedKeys = new List<string>();

        foreach (var key in cacheKeys)
        {
            var scoped = _ScopeKey(key);
            keyMap[scoped] = key;
            scopedKeys.Add(scoped);
        }

        var result = await cache.GetAllAsync<T>(scopedKeys, cancellationToken);

        var unscoped = new Dictionary<string, CacheValue<T>>(result.Count, StringComparer.Ordinal);

        foreach (var (key, value) in result)
        {
            unscoped[keyMap[key]] = value;
        }

        return unscoped;
    }

    public async ValueTask<IDictionary<string, CacheValue<T>>> GetByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    )
    {
        var tenantPrefix = _GetPrefix();
        var result = await cache.GetByPrefixAsync<T>($"{tenantPrefix}{prefix}", cancellationToken);

        var unscoped = new Dictionary<string, CacheValue<T>>(result.Count, StringComparer.Ordinal);

        foreach (var (key, value) in result)
        {
            unscoped[key[tenantPrefix.Length..]] = value;
        }

        return unscoped;
    }

    public ValueTask<CacheValue<T>> GetAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        return cache.GetAsync<T>(_ScopeKey(cacheKey), cancellationToken);
    }

    public ValueTask<CacheValue<ICollection<T>>> GetSetAsync(string key, int? pageIndex = null, int pageSize = 100)
    {
        return cache.GetSetAsync<T>(_ScopeKey(key), pageIndex, pageSize);
    }

    #endregion

    #region Remove

    public ValueTask<bool> RemoveAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        return cache.RemoveAsync(_ScopeKey(cacheKey), cancellationToken);
    }

    public ValueTask<bool> RemoveIfEqualAsync(string cacheKey, T? expected)
    {
        return cache.RemoveIfEqualAsync(_ScopeKey(cacheKey), expected);
    }

    public ValueTask<int> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        return cache.RemoveByPrefixAsync($"{_GetPrefix()}{prefix}", cancellationToken);
    }

    public ValueTask<long> SetRemoveAsync(
        string key,
        IEnumerable<T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.SetRemoveAsync(_ScopeKey(key), value, expiration, cancellationToken);
    }

    #endregion
}
