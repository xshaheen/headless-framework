// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

/// <summary>
/// Decorator over <see cref="ICache"/> that prefixes all keys with a dynamic scope,
/// providing transparent cache isolation for any cache item type.
/// </summary>
/// <remarks>
/// <para>
/// Safe to register as singleton — <paramref name="scopeProvider"/> is invoked on each
/// operation, so the scope can change between calls (e.g. per-request tenant context).
/// </para>
/// <para>
/// The scope provider must return a non-null string. The resulting cache key format
/// is <c>{scope}:{originalKey}</c>.
/// </para>
/// </remarks>
[PublicAPI]
public class ScopedCache<T>(ICache cache, Func<string> scopeProvider) : ICache<T>
{
    private string _Prefix() => $"{scopeProvider()}:";
    private string _ScopeKey(string key) => $"{_Prefix()}{key}";

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
        // Build a scoped->original mapping to reliably restore keys
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
        var scopePrefix = _Prefix();
        var result = await cache.GetByPrefixAsync<T>($"{scopePrefix}{prefix}", cancellationToken);

        var unscoped = new Dictionary<string, CacheValue<T>>(result.Count, StringComparer.Ordinal);

        foreach (var (key, value) in result)
        {
            unscoped[key[scopePrefix.Length..]] = value;
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
        return cache.RemoveByPrefixAsync($"{_Prefix()}{prefix}", cancellationToken);
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
