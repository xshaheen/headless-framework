// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;

namespace Tests;

/// <summary>
/// An L2 remote cache whose framed read (TryGetEntryAsync) and prefix/count reads (GetByPrefixAsync,
/// GetAllKeysByPrefixAsync, GetCountAsync) always throw to simulate a down store. Write operations are no-ops so
/// the factory-success path still works if needed.
/// </summary>
internal sealed class ThrowingReadRemoteCache(TimeProvider timeProvider) : IRemoteCache, IFactoryCacheStore, IDisposable
{
    public CacheEntryOptions? DefaultEntryOptions => null;

    private readonly InMemoryCache _inner = new(timeProvider, new InMemoryCacheOptions());

    public ValueTask<CacheStoreEntry<T>> TryGetEntryAsync<T>(
        string key,
        CancellationToken cancellationToken,
        FactoryCacheReadOptions readOptions = default
    ) => throw new InvalidOperationException("L2 store is unavailable");

    public ValueTask<CacheStoreEntry<T>[]> TryGetAllEntriesAsync<T>(
        IReadOnlyList<string> keys,
        CancellationToken cancellationToken,
        FactoryCacheReadOptions readOptions = default
    ) => throw new InvalidOperationException("L2 store is unavailable");

    public ValueTask<bool> SetEntryAsync<T>(
        string key,
        in CacheStoreEntryWrite<T> entry,
        CancellationToken cancellationToken
    ) =>
        // No-op: writes are silently dropped (non-fatal in HybridCache.SetEntryAsync)
        new(true);

    public ValueTask TryRearmSlidingAsync(
        string key,
        TimeSpan slidingExpiration,
        DateTime physicalExpiresAt,
        DateTime now,
        CancellationToken cancellationToken
    ) =>
        // No-op: best-effort re-arm on a down store is silently dropped.
        ValueTask.CompletedTask;

    public ValueTask<CacheValue<T>> GetOrAddAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T?>> factory,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    ) => throw new InvalidOperationException("L2 store is unavailable");

    public ValueTask<CacheValue<T>> GetOrAddAsync<T>(
        string key,
        Func<CacheFactoryContext<T>, CancellationToken, ValueTask<CacheFactoryResult<T>>> factory,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    ) => throw new InvalidOperationException("L2 store is unavailable");

    public ValueTask<bool> UpsertAsync<T>(
        string key,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => new(false);

    public ValueTask<bool> UpsertEntryAsync<T>(
        string key,
        T? value,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    ) => new(false);

    public ValueTask<int> UpsertAllAsync<T>(
        IDictionary<string, T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => new(0);

    public ValueTask<bool> TryInsertAsync<T>(
        string key,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => new(false);

    public ValueTask<bool> TryReplaceAsync<T>(
        string key,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => new(false);

    public ValueTask<bool> TryReplaceIfEqualAsync<T>(
        string key,
        T? expected,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => new(false);

    public ValueTask<double> IncrementAsync(
        string key,
        double amount,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => new(0d);

    public ValueTask<long> IncrementAsync(
        string key,
        long amount,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => new(0L);

    public ValueTask<double> SetIfHigherAsync(
        string key,
        double value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => new(0d);

    public ValueTask<long> SetIfHigherAsync(
        string key,
        long value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => new(0L);

    public ValueTask<double> SetIfLowerAsync(
        string key,
        double value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => new(0d);

    public ValueTask<long> SetIfLowerAsync(
        string key,
        long value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => new(0L);

    public ValueTask<long> SetAddAsync<T>(
        string key,
        IEnumerable<T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => new(0L);

    public ValueTask<CacheValue<T>> GetAsync<T>(string key, CancellationToken cancellationToken = default) =>
        new(CacheValue<T>.NoValue);

    public ValueTask<IDictionary<string, CacheValue<T>>> GetAllAsync<T>(
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default
    ) => new(new Dictionary<string, CacheValue<T>>(StringComparer.Ordinal));

    public ValueTask<CacheValueWithExpiration<T>> GetWithExpirationAsync<T>(
        string key,
        CancellationToken cancellationToken = default
    ) => new(new CacheValueWithExpiration<T>(CacheValue<T>.NoValue, null));

    public ValueTask<IDictionary<string, CacheValueWithExpiration<T>>> GetAllWithExpirationAsync<T>(
        IEnumerable<string> cacheKeys,
        CancellationToken cancellationToken = default
    ) => new(new Dictionary<string, CacheValueWithExpiration<T>>(StringComparer.Ordinal));

    public ValueTask<IDictionary<string, CacheValue<T>>> GetByPrefixAsync<T>(
        string prefix,
        CancellationToken cancellationToken = default
    ) => throw new InvalidOperationException("L2 store is unavailable");

    public ValueTask<IReadOnlyList<string>> GetAllKeysByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    ) => throw new InvalidOperationException("L2 store is unavailable");

    public ValueTask<long> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("L2 store is unavailable");

    public ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) => new(false);

    public ValueTask<TimeSpan?> GetExpirationAsync(string key, CancellationToken cancellationToken = default) =>
        new((TimeSpan?)null);

    public ValueTask<CacheValue<ICollection<T>>> GetSetAsync<T>(
        string key,
        int? pageIndex = null,
        int pageSize = 100,
        CancellationToken cancellationToken = default
    ) => new(CacheValue<ICollection<T>>.NoValue);

    public ValueTask RefreshAsync(string key, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public ValueTask<bool> RemoveAsync(string key, CancellationToken cancellationToken = default) => new(false);

    public ValueTask<bool> ExpireAsync(string key, CancellationToken cancellationToken = default) => new(false);

    public ValueTask<bool> RemoveIfEqualAsync<T>(
        string key,
        T? expected,
        CancellationToken cancellationToken = default
    ) => new(false);

    public ValueTask<int> RemoveAllAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default) =>
        new(0);

    public ValueTask<int> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default) => new(0);

    public ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    public ValueTask ClearAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public ValueTask<long> SetRemoveAsync<T>(
        string key,
        IEnumerable<T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => new(0L);

    public ValueTask FlushAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public void Dispose() => _inner.Dispose();
}
