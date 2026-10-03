// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

/// <summary>
/// Concrete strongly-typed cache wrapper that adapts an <see cref="ICache"/> instance to
/// <see cref="ICache{T}"/> by forwarding every call to the underlying cache with the type parameter
/// fixed to <typeparamref name="T"/>. Registered by the setup infrastructure as the default
/// <c>ICache&lt;T&gt;</c> singleton so consumers can inject a typed cache without declaring the
/// type at the call site.
/// </summary>
/// <typeparam name="T">The type of every value stored and retrieved through this wrapper.</typeparam>
/// <param name="cache">The underlying untyped cache to delegate all operations to.</param>
[PublicAPI]
public sealed class Cache<T>(ICache cache) : ICache<T>
{
    /// <inheritdoc />
    public CacheEntryOptions? DefaultEntryOptions => cache.DefaultEntryOptions;

    public ICacheEvents Events => cache.Events;

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
        TimeSpan? expiration,
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
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.UpsertAllAsync(value, expiration, cancellationToken);
    }

    public ValueTask<bool> TryInsertAsync(
        string cacheKey,
        T? cacheValue,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.TryInsertAsync(cacheKey, cacheValue, expiration, cancellationToken);
    }

    public ValueTask<bool> TryReplaceAsync(
        string key,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.TryReplaceAsync(key, value, expiration, cancellationToken);
    }

    public ValueTask<bool> TryReplaceIfEqualAsync(
        string key,
        T? expected,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.TryReplaceIfEqualAsync(key, expected, value, expiration, cancellationToken);
    }

    public ValueTask<long> SetAddAsync(
        string key,
        IEnumerable<T> value,
        TimeSpan? expiration,
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

    public ValueTask<CacheValue<ICollection<T>>> GetSetAsync(
        string key,
        int? pageIndex = null,
        int pageSize = 100,
        CancellationToken cancellationToken = default
    )
    {
        return cache.GetSetAsync<T>(key, pageIndex, pageSize, cancellationToken);
    }

    public ValueTask RefreshAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        return cache.RefreshAsync(cacheKey, cancellationToken);
    }

    public ValueTask<bool> RemoveAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        return cache.RemoveAsync(cacheKey, cancellationToken);
    }

    public ValueTask<bool> ExpireAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        return cache.ExpireAsync(cacheKey, cancellationToken);
    }

    public ValueTask<bool> RemoveIfEqualAsync(
        string cacheKey,
        T? expected,
        CancellationToken cancellationToken = default
    )
    {
        return cache.RemoveIfEqualAsync(cacheKey, expected, cancellationToken);
    }

    public ValueTask<int> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        return cache.RemoveByPrefixAsync(prefix, cancellationToken);
    }

    public ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        return cache.RemoveByTagAsync(tag, cancellationToken);
    }

    public ValueTask ClearAsync(CancellationToken cancellationToken = default)
    {
        return cache.ClearAsync(cancellationToken);
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

    public ValueTask<int> RemoveAllAsync(IEnumerable<string> cacheKeys, CancellationToken cancellationToken = default)
    {
        return cache.RemoveAllAsync(cacheKeys, cancellationToken);
    }

    public ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        return cache.FlushAsync(cancellationToken);
    }

    public ValueTask<double> IncrementAsync(
        string key,
        double amount,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.IncrementAsync(key, amount, expiration, cancellationToken);
    }

    public ValueTask<long> IncrementAsync(
        string key,
        long amount,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.IncrementAsync(key, amount, expiration, cancellationToken);
    }

    public ValueTask<double> SetIfHigherAsync(
        string key,
        double value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.SetIfHigherAsync(key, value, expiration, cancellationToken);
    }

    public ValueTask<long> SetIfHigherAsync(
        string key,
        long value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.SetIfHigherAsync(key, value, expiration, cancellationToken);
    }

    public ValueTask<double> SetIfLowerAsync(
        string key,
        double value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.SetIfLowerAsync(key, value, expiration, cancellationToken);
    }

    public ValueTask<long> SetIfLowerAsync(
        string key,
        long value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.SetIfLowerAsync(key, value, expiration, cancellationToken);
    }

    public ValueTask<IReadOnlyList<string>> GetAllKeysByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    )
    {
        return cache.GetAllKeysByPrefixAsync(prefix, cancellationToken);
    }

    public ValueTask<long> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default)
    {
        return cache.GetCountAsync(prefix, cancellationToken);
    }

    public ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        return cache.ExistsAsync(key, cancellationToken);
    }

    public ValueTask<TimeSpan?> GetExpirationAsync(string key, CancellationToken cancellationToken = default)
    {
        return cache.GetExpirationAsync(key, cancellationToken);
    }
}
