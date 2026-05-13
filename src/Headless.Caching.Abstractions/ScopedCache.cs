// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Caching;

/// <summary>
/// Decorator over <see cref="ICache"/> that prefixes all keys with a dynamic scope,
/// providing transparent cache isolation for any cache item type.
/// </summary>
/// <remarks>
/// <para>
/// Safe to register as singleton — the scope provider is invoked on each
/// operation, so the scope can change between calls (e.g. per-request tenant context).
/// </para>
/// <para>
/// The scope provider must return a non-null string. The resulting cache key format
/// is <c>{scope}:{originalKey}</c>.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class ScopedCache<T> : ICache<T>
{
    private readonly ICache _cache;
    private readonly Func<string> _scopeProvider;

    /// <summary>Initializes a new instance of the <see cref="ScopedCache{T}"/> class.</summary>
    /// <param name="cache">The underlying cache.</param>
    /// <param name="scopeProvider">The provider for the scope prefix.</param>
    public ScopedCache(ICache cache, Func<string> scopeProvider)
    {
        Argument.IsNotNull(cache);
        Argument.IsNotNull(scopeProvider);

        _cache = cache;
        _scopeProvider = scopeProvider;
    }

    private string _Prefix() => $"{_scopeProvider()}:";

    private string _ScopeKey(string key) => $"{_Prefix()}{key}";

    /// <inheritdoc />
    public ValueTask<CacheValue<T>> GetOrAddAsync(
        string key,
        Func<CancellationToken, ValueTask<T?>> factory,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        return _cache.GetOrAddAsync(_ScopeKey(key), factory, expiration, cancellationToken);
    }

    #region Update

    /// <inheritdoc />
    public ValueTask<bool> UpsertAsync(
        string cacheKey,
        T? cacheValue,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(cacheKey);
        return _cache.UpsertAsync(_ScopeKey(cacheKey), cacheValue, expiration, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<int> UpsertAllAsync(
        IDictionary<string, T> value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(value);
        var scoped = new Dictionary<string, T>(value.Count, StringComparer.Ordinal);

        foreach (var (key, val) in value)
        {
            scoped[_ScopeKey(key)] = val;
        }

        return _cache.UpsertAllAsync(scoped, expiration, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<bool> TryAddAsync(
        string cacheKey,
        T? cacheValue,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(cacheKey);
        return _cache.TryInsertAsync(_ScopeKey(cacheKey), cacheValue, expiration, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<bool> TryReplaceAsync(
        string key,
        T? value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        return _cache.TryReplaceAsync(_ScopeKey(key), value, expiration, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<bool> TryReplaceIfEqualAsync(
        string key,
        T? expected,
        T? value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        return _cache.TryReplaceIfEqualAsync(_ScopeKey(key), expected, value, expiration, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<long> SetAddAsync(
        string key,
        IEnumerable<T> value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        return _cache.SetAddAsync(_ScopeKey(key), value, expiration, cancellationToken);
    }

    #endregion

    #region Get

    /// <inheritdoc />
    public async ValueTask<IDictionary<string, CacheValue<T>>> GetAllAsync(
        IEnumerable<string> cacheKeys,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(cacheKeys);

        // Build a scoped->original mapping to reliably restore keys
        var keyMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var scopedKeys = new List<string>();

        foreach (var key in cacheKeys)
        {
            var scoped = _ScopeKey(key);
            keyMap[scoped] = key;
            scopedKeys.Add(scoped);
        }

        var result = await _cache.GetAllAsync<T>(scopedKeys, cancellationToken).ConfigureAwait(false);

        var unscoped = new Dictionary<string, CacheValue<T>>(result.Count, StringComparer.Ordinal);

        foreach (var (key, value) in result)
        {
            unscoped[keyMap[key]] = value;
        }

        return unscoped;
    }

    /// <inheritdoc />
    public async ValueTask<IDictionary<string, CacheValue<T>>> GetByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    )
    {
        var scopePrefix = _Prefix();
        var result = await _cache
            .GetByPrefixAsync<T>($"{scopePrefix}{prefix}", cancellationToken)
            .ConfigureAwait(false);

        var unscoped = new Dictionary<string, CacheValue<T>>(result.Count, StringComparer.Ordinal);

        foreach (var (key, value) in result)
        {
            unscoped[key[scopePrefix.Length..]] = value;
        }

        return unscoped;
    }

    /// <inheritdoc />
    public ValueTask<CacheValue<T>> GetAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(cacheKey);
        return _cache.GetAsync<T>(_ScopeKey(cacheKey), cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<CacheValue<ICollection<T>>> GetSetAsync(string key, int? pageIndex = null, int pageSize = 100)
    {
        Argument.IsNotNullOrEmpty(key);
        return _cache.GetSetAsync<T>(_ScopeKey(key), pageIndex, pageSize);
    }

    #endregion

    #region Remove

    /// <inheritdoc />
    public ValueTask<bool> RemoveAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(cacheKey);
        return _cache.RemoveAsync(_ScopeKey(cacheKey), cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<bool> RemoveIfEqualAsync(string cacheKey, T? expected)
    {
        Argument.IsNotNullOrEmpty(cacheKey);
        return _cache.RemoveIfEqualAsync(_ScopeKey(cacheKey), expected);
    }

    /// <inheritdoc />
    public ValueTask<int> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        return _cache.RemoveByPrefixAsync($"{_Prefix()}{prefix}", cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<long> SetRemoveAsync(
        string key,
        IEnumerable<T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        return _cache.SetRemoveAsync(_ScopeKey(key), value, expiration, cancellationToken);
    }

    #endregion
}
