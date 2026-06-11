// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

public interface ICache<T>
{
    /// <summary>
    /// Gets the default <see cref="CacheEntryOptions"/> configured for this cache instance at registration.
    /// Used by the option-less <c>GetOrAddAsync</c> extension overloads; when <see langword="null"/>,
    /// those overloads throw <see cref="InvalidOperationException"/> — defaults are explicit-at-registration,
    /// never magic.
    /// </summary>
    CacheEntryOptions? DefaultEntryOptions { get; }

    /// <summary>
    /// Gets a value from cache, or creates it using the factory if not found.
    /// Uses keyed locking to prevent cache stampedes (multiple concurrent factory executions for the same key).
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="factory">The factory function to create the value if not found in cache. Receives the cancellation token.</param>
    /// <param name="options">Cache entry options for the cached value.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cached or newly created value wrapped in <see cref="CacheValue{T}"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <see cref="CacheEntryOptions.Duration"/> is not positive, or when fail-safe is enabled and
    /// <see cref="CacheEntryOptions.FailSafeMaxDuration"/> or
    /// <see cref="CacheEntryOptions.FailSafeThrottleDuration"/> is not positive.
    /// </exception>
    ValueTask<CacheValue<T>> GetOrAddAsync(
        string key,
        Func<CancellationToken, ValueTask<T?>> factory,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Gets a value from cache, or refreshes it using a conditional factory (the HTTP-304 pattern).
    /// The factory receives a <see cref="CacheFactoryContext{T}"/> carrying the last-known cached value and its
    /// validators and returns <see cref="CacheFactoryContext{T}.NotModified"/> to extend the existing entry as
    /// fresh, or <see cref="CacheFactoryContext{T}.Modified(T, string?, DateTime?)"/> to replace it. The factory
    /// may also replace <see cref="CacheFactoryContext{T}.Options"/> before returning (adaptive caching).
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="factory">The conditional factory invoked on a miss or refresh. Receives the per-execution context and the cancellation token.</param>
    /// <param name="options">Cache entry options for the cached value; the factory may replace them via the context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cached, extended, or newly created value wrapped in <see cref="CacheValue{T}"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the entry options (including an adaptive replacement set by the factory) are invalid, for
    /// example a non-positive <see cref="CacheEntryOptions.Duration"/>.
    /// </exception>
    ValueTask<CacheValue<T>> GetOrAddAsync(
        string key,
        Func<CacheFactoryContext<T>, CancellationToken, ValueTask<CacheFactoryResult<T>>> factory,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    );

    #region Update

    ValueTask<bool> UpsertAsync(
        string cacheKey,
        T? cacheValue,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Sets a value as a direct write honoring the full <see cref="CacheEntryOptions"/> semantics, including
    /// <see cref="CacheEntryOptions.Tags"/> for later <see cref="RemoveByTagAsync"/> invalidation.
    /// </summary>
    ValueTask<bool> UpsertEntryAsync(
        string cacheKey,
        T? cacheValue,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    );

    ValueTask<int> UpsertAllAsync(
        IDictionary<string, T> value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    );

    ValueTask<bool> TryAddAsync(
        string cacheKey,
        T? cacheValue,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    );

    ValueTask<bool> TryReplaceAsync(
        string key,
        T? value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    );

    ValueTask<bool> TryReplaceIfEqualAsync(
        string key,
        T? expected,
        T? value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    );

    ValueTask<long> SetAddAsync(
        string key,
        IEnumerable<T> value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    );

    #endregion

    #region Get

    ValueTask<IDictionary<string, CacheValue<T>>> GetAllAsync(
        IEnumerable<string> cacheKeys,
        CancellationToken cancellationToken = default
    );

    ValueTask<IDictionary<string, CacheValue<T>>> GetByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    );

    ValueTask<CacheValue<T>> GetAsync(string cacheKey, CancellationToken cancellationToken = default);

    ValueTask<CacheValue<ICollection<T>>> GetSetAsync(string key, int? pageIndex = null, int pageSize = 100);

    #endregion

    #region Remove

    ValueTask<bool> RemoveAsync(string cacheKey, CancellationToken cancellationToken = default);

    ValueTask<bool> RemoveIfEqualAsync(string cacheKey, T? expected);

    ValueTask<int> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default);

    /// <summary>Removes exactly the entries that currently carry <paramref name="tag"/>. See <see cref="ICache.RemoveByTagAsync"/>.</summary>
    ValueTask<int> RemoveByTagAsync(string tag, CancellationToken cancellationToken = default);

    ValueTask<long> SetRemoveAsync(
        string key,
        IEnumerable<T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    #endregion
}

public class Cache<T>(ICache cache) : ICache<T>
{
    /// <inheritdoc />
    public CacheEntryOptions? DefaultEntryOptions => cache.DefaultEntryOptions;

    public ValueTask<CacheValue<T>> GetOrAddAsync(
        string key,
        Func<CancellationToken, ValueTask<T?>> factory,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    )
    {
        return cache.GetOrAddAsync(key, factory, options, cancellationToken);
    }

    public ValueTask<CacheValue<T>> GetOrAddAsync(
        string key,
        Func<CacheFactoryContext<T>, CancellationToken, ValueTask<CacheFactoryResult<T>>> factory,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    )
    {
        return cache.GetOrAddAsync(key, factory, options, cancellationToken);
    }

    public ValueTask<bool> UpsertAsync(
        string cacheKey,
        T? cacheValue,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.UpsertAsync(cacheKey, cacheValue, expiration, cancellationToken);
    }

    public ValueTask<bool> UpsertEntryAsync(
        string cacheKey,
        T? cacheValue,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    )
    {
        return cache.UpsertEntryAsync(cacheKey, cacheValue, options, cancellationToken);
    }

    public ValueTask<int> UpsertAllAsync(
        IDictionary<string, T> value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.UpsertAllAsync(value, expiration, cancellationToken);
    }

    public ValueTask<bool> TryAddAsync(
        string cacheKey,
        T? cacheValue,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.TryInsertAsync(cacheKey, cacheValue, expiration, cancellationToken);
    }

    public ValueTask<bool> TryReplaceAsync(
        string key,
        T? value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.TryReplaceAsync(key, value, expiration, cancellationToken);
    }

    public ValueTask<bool> TryReplaceIfEqualAsync(
        string key,
        T? expected,
        T? value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.TryReplaceIfEqualAsync(key, expected, value, expiration, cancellationToken);
    }

    public ValueTask<long> SetAddAsync(
        string key,
        IEnumerable<T> value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.SetAddAsync(key, value, expiration, cancellationToken);
    }

    public ValueTask<IDictionary<string, CacheValue<T>>> GetAllAsync(
        IEnumerable<string> cacheKeys,
        CancellationToken cancellationToken = default
    )
    {
        return cache.GetAllAsync<T>(cacheKeys, cancellationToken);
    }

    public ValueTask<IDictionary<string, CacheValue<T>>> GetByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    )
    {
        return cache.GetByPrefixAsync<T>(prefix, cancellationToken);
    }

    public ValueTask<CacheValue<T>> GetAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        return cache.GetAsync<T>(cacheKey, cancellationToken);
    }

    public ValueTask<CacheValue<ICollection<T>>> GetSetAsync(string key, int? pageIndex = null, int pageSize = 100)
    {
        return cache.GetSetAsync<T>(key, pageIndex, pageSize);
    }

    public ValueTask<bool> RemoveAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        return cache.RemoveAsync(cacheKey, cancellationToken);
    }

    public ValueTask<bool> RemoveIfEqualAsync(string cacheKey, T? expected)
    {
        return cache.RemoveIfEqualAsync(cacheKey, expected);
    }

    public ValueTask<int> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        return cache.RemoveByPrefixAsync(prefix, cancellationToken);
    }

    public ValueTask<int> RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        return cache.RemoveByTagAsync(tag, cancellationToken);
    }

    public ValueTask<long> SetRemoveAsync(
        string key,
        IEnumerable<T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.SetRemoveAsync(key, value, expiration, cancellationToken);
    }
}

public interface IRemoteCache<T> : ICache<T>;

public interface IInMemoryCache<T> : ICache<T>;

public sealed class RemoteCache<T>(IRemoteCache cache) : Cache<T>(cache), IRemoteCache<T>;

public sealed class InMemoryCache<T>(IInMemoryCache cache) : Cache<T>(cache), IInMemoryCache<T>;
