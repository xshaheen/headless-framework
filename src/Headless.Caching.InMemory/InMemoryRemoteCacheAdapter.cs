// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

/// <summary>
/// Adapts <see cref="IInMemoryCache"/> to <see cref="IRemoteCache"/> so that single-node hosts can use the
/// in-memory store as the remote tier without wiring up a real network cache. Every method simply delegates
/// to the underlying in-memory cache.
/// </summary>
internal sealed class InMemoryRemoteCacheAdapter(IInMemoryCache inMemoryCache) : IRemoteCache
{
    public CacheEntryOptions? DefaultEntryOptions => inMemoryCache.DefaultEntryOptions;

    public ValueTask<CacheValue<T>> GetOrAddAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T?>> factory,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    ) => inMemoryCache.GetOrAddAsync(key, factory, options, cancellationToken);

    public ValueTask<CacheValue<T>> GetOrAddAsync<T>(
        string key,
        Func<CacheFactoryContext<T>, CancellationToken, ValueTask<CacheFactoryResult<T>>> factory,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    ) => inMemoryCache.GetOrAddAsync(key, factory, options, cancellationToken);

    public ValueTask<bool> UpsertAsync<T>(
        string key,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => inMemoryCache.UpsertAsync(key, value, expiration, cancellationToken);

    public ValueTask<bool> UpsertEntryAsync<T>(
        string key,
        T? value,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    ) => inMemoryCache.UpsertEntryAsync(key, value, options, cancellationToken);

    public ValueTask<int> UpsertAllAsync<T>(
        IDictionary<string, T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => inMemoryCache.UpsertAllAsync(value, expiration, cancellationToken);

    public ValueTask<bool> TryInsertAsync<T>(
        string key,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => inMemoryCache.TryInsertAsync(key, value, expiration, cancellationToken);

    public ValueTask<bool> TryReplaceAsync<T>(
        string key,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => inMemoryCache.TryReplaceAsync(key, value, expiration, cancellationToken);

    public ValueTask<bool> TryReplaceIfEqualAsync<T>(
        string key,
        T? expected,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => inMemoryCache.TryReplaceIfEqualAsync(key, expected, value, expiration, cancellationToken);

    public ValueTask<double> IncrementAsync(
        string key,
        double amount,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => inMemoryCache.IncrementAsync(key, amount, expiration, cancellationToken);

    public ValueTask<long> IncrementAsync(
        string key,
        long amount,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => inMemoryCache.IncrementAsync(key, amount, expiration, cancellationToken);

    public ValueTask<double> SetIfHigherAsync(
        string key,
        double value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => inMemoryCache.SetIfHigherAsync(key, value, expiration, cancellationToken);

    public ValueTask<long> SetIfHigherAsync(
        string key,
        long value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => inMemoryCache.SetIfHigherAsync(key, value, expiration, cancellationToken);

    public ValueTask<double> SetIfLowerAsync(
        string key,
        double value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => inMemoryCache.SetIfLowerAsync(key, value, expiration, cancellationToken);

    public ValueTask<long> SetIfLowerAsync(
        string key,
        long value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => inMemoryCache.SetIfLowerAsync(key, value, expiration, cancellationToken);

    public ValueTask<long> SetAddAsync<T>(
        string key,
        IEnumerable<T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => inMemoryCache.SetAddAsync(key, value, expiration, cancellationToken);

    public ValueTask<IDictionary<string, CacheValue<T>>> GetAllAsync<T>(
        IEnumerable<string> cacheKeys,
        CancellationToken cancellationToken = default
    ) => inMemoryCache.GetAllAsync<T>(cacheKeys, cancellationToken);

    public ValueTask<IDictionary<string, CacheValue<T>>> GetByPrefixAsync<T>(
        string prefix,
        CancellationToken cancellationToken = default
    ) => inMemoryCache.GetByPrefixAsync<T>(prefix, cancellationToken);

    public ValueTask<IReadOnlyList<string>> GetAllKeysByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    ) => inMemoryCache.GetAllKeysByPrefixAsync(prefix, cancellationToken);

    public ValueTask<CacheValue<T>> GetAsync<T>(string key, CancellationToken cancellationToken = default) =>
        inMemoryCache.GetAsync<T>(key, cancellationToken);

    public ValueTask<long> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default) =>
        inMemoryCache.GetCountAsync(prefix, cancellationToken);

    public ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) =>
        inMemoryCache.ExistsAsync(key, cancellationToken);

    public ValueTask<TimeSpan?> GetExpirationAsync(string key, CancellationToken cancellationToken = default) =>
        inMemoryCache.GetExpirationAsync(key, cancellationToken);

    public ValueTask<CacheValue<ICollection<T>>> GetSetAsync<T>(
        string key,
        int? pageIndex = null,
        int pageSize = 100,
        CancellationToken cancellationToken = default
    ) => inMemoryCache.GetSetAsync<T>(key, pageIndex, pageSize, cancellationToken);

    public ValueTask<bool> RemoveAsync(string key, CancellationToken cancellationToken = default) =>
        inMemoryCache.RemoveAsync(key, cancellationToken);

    public ValueTask<bool> RemoveIfEqualAsync<T>(
        string key,
        T? expected,
        CancellationToken cancellationToken = default
    ) => inMemoryCache.RemoveIfEqualAsync(key, expected, cancellationToken);

    public ValueTask<int> RemoveAllAsync(
        IEnumerable<string> cacheKeys,
        CancellationToken cancellationToken = default
    ) => inMemoryCache.RemoveAllAsync(cacheKeys, cancellationToken);

    public ValueTask<int> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default) =>
        inMemoryCache.RemoveByPrefixAsync(prefix, cancellationToken);

    public ValueTask<int> RemoveByTagAsync(string tag, CancellationToken cancellationToken = default) =>
        inMemoryCache.RemoveByTagAsync(tag, cancellationToken);

    public ValueTask<long> SetRemoveAsync<T>(
        string key,
        IEnumerable<T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => inMemoryCache.SetRemoveAsync(key, value, expiration, cancellationToken);

    public ValueTask FlushAsync(CancellationToken cancellationToken = default) =>
        inMemoryCache.FlushAsync(cancellationToken);
}
