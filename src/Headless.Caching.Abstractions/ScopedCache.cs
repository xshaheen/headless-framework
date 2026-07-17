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

    /// <inheritdoc />
    public CacheEntryOptions? DefaultEntryOptions => _cache.DefaultEntryOptions;

    private string _Prefix()
    {
        return $"{_scopeProvider()}:";
    }

    private string _ScopeKey(string key)
    {
        return $"{_Prefix()}{key}";
    }

    /// <inheritdoc />
    public ValueTask<CacheValue<T>> GetOrAddAsync(
        string key,
        Func<CancellationToken, ValueTask<T?>> factory,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);

        return _cache.GetOrAddAsync(_ScopeKey(key), factory, options, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<CacheValue<T>> GetOrAddAsync(
        string key,
        Func<CacheFactoryContext<T>, CancellationToken, ValueTask<CacheFactoryResult<T>>> factory,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);

        return _cache.GetOrAddAsync(_ScopeKey(key), factory, options, cancellationToken);
    }

    #region Update

    /// <inheritdoc />
    public ValueTask<bool> UpsertAsync(
        string cacheKey,
        T? cacheValue,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(cacheKey);
        return _cache.UpsertAsync(_ScopeKey(cacheKey), cacheValue, expiration, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<bool> UpsertEntryAsync(
        string cacheKey,
        T? cacheValue,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(cacheKey);
        return _cache.UpsertEntryAsync(_ScopeKey(cacheKey), cacheValue, options, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<int> UpsertAllAsync(
        IDictionary<string, T> value,
        TimeSpan? expiration,
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
    public ValueTask<bool> TryInsertAsync(
        string cacheKey,
        T? cacheValue,
        TimeSpan? expiration,
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
        TimeSpan? expiration,
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
        TimeSpan? expiration,
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
        TimeSpan? expiration,
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
    public ValueTask<CacheValue<ICollection<T>>> GetSetAsync(
        string key,
        int? pageIndex = null,
        int pageSize = 100,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        return _cache.GetSetAsync<T>(_ScopeKey(key), pageIndex, pageSize, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask RefreshAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(cacheKey);
        return _cache.RefreshAsync(_ScopeKey(cacheKey), cancellationToken);
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
    public ValueTask<bool> ExpireAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(cacheKey);
        return _cache.ExpireAsync(_ScopeKey(cacheKey), cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<bool> RemoveIfEqualAsync(
        string cacheKey,
        T? expected,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(cacheKey);
        return _cache.RemoveIfEqualAsync(_ScopeKey(cacheKey), expected, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<int> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        return _cache.RemoveByPrefixAsync($"{_Prefix()}{prefix}", cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Tags are NOT scope-isolated: only keys are scoped, so a tag invalidation removes every tagged entry in
    /// the underlying cache regardless of scope. Scope-isolate tags by embedding the scope in the tag value
    /// when per-scope invalidation is required.
    /// </remarks>
    public ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        return _cache.RemoveByTagAsync(tag, cancellationToken);
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

    /// <inheritdoc />
    public ValueTask<int> RemoveAllAsync(IEnumerable<string> cacheKeys, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(cacheKeys);
        var scoped = new List<string>();

        foreach (var key in cacheKeys)
        {
            scoped.Add(_ScopeKey(key));
        }

        return _cache.RemoveAllAsync(scoped, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// This is NOT scope-isolated: <c>FlushAsync</c> clears the entire underlying cache, not just the
    /// entries belonging to this scope. To remove only scoped entries, use <see cref="RemoveByPrefixAsync"/>
    /// with an empty prefix instead.
    /// </remarks>
    public ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        return _cache.FlushAsync(cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// This is NOT scope-isolated: <c>ClearAsync</c> logically clears the entire underlying cache, not just the
    /// entries belonging to this scope. To remove only scoped entries, use <see cref="RemoveByPrefixAsync"/>
    /// with an empty prefix instead.
    /// </remarks>
    public ValueTask ClearAsync(CancellationToken cancellationToken = default)
    {
        return _cache.ClearAsync(cancellationToken);
    }

    #endregion

    #region Management

    /// <inheritdoc />
    public ValueTask<double> IncrementAsync(
        string key,
        double amount,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        return _cache.IncrementAsync(_ScopeKey(key), amount, expiration, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<long> IncrementAsync(
        string key,
        long amount,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        return _cache.IncrementAsync(_ScopeKey(key), amount, expiration, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<double> SetIfHigherAsync(
        string key,
        double value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        return _cache.SetIfHigherAsync(_ScopeKey(key), value, expiration, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<long> SetIfHigherAsync(
        string key,
        long value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        return _cache.SetIfHigherAsync(_ScopeKey(key), value, expiration, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<double> SetIfLowerAsync(
        string key,
        double value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        return _cache.SetIfLowerAsync(_ScopeKey(key), value, expiration, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<long> SetIfLowerAsync(
        string key,
        long value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        return _cache.SetIfLowerAsync(_ScopeKey(key), value, expiration, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<string>> GetAllKeysByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    )
    {
        var scopePrefix = _Prefix();
        var results = await _cache
            .GetAllKeysByPrefixAsync($"{scopePrefix}{prefix}", cancellationToken)
            .ConfigureAwait(false);

        var unscoped = new List<string>(results.Count);

        foreach (var key in results)
        {
            unscoped.Add(key[scopePrefix.Length..]);
        }

        return unscoped;
    }

    /// <inheritdoc />
    public ValueTask<long> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default)
    {
        return _cache.GetCountAsync($"{_Prefix()}{prefix}", cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(key);
        return _cache.ExistsAsync(_ScopeKey(key), cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<TimeSpan?> GetExpirationAsync(string key, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(key);
        return _cache.GetExpirationAsync(_ScopeKey(key), cancellationToken);
    }

    #endregion
}
