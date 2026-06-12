// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;

namespace Tests;

/// <summary>Simple adapter to use InMemoryCache as IRemoteCache for testing.</summary>
internal sealed class InMemoryRemoteCacheAdapter(InMemoryCache cache) : IRemoteCache, IFactoryCacheStore
{
    public CacheEntryOptions? DefaultEntryOptions => cache.DefaultEntryOptions;

    public ValueTask<CacheValue<T>> GetOrAddAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T?>> factory,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    ) => cache.GetOrAddAsync(key, factory, options, cancellationToken);

    public ValueTask<CacheValue<T>> GetOrAddAsync<T>(
        string key,
        Func<CacheFactoryContext<T>, CancellationToken, ValueTask<CacheFactoryResult<T>>> factory,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    ) => cache.GetOrAddAsync(key, factory, options, cancellationToken);

    public ValueTask<CacheStoreEntry<T>> TryGetEntryAsync<T>(string key, CancellationToken cancellationToken) =>
        ((IFactoryCacheStore)cache).TryGetEntryAsync<T>(key, cancellationToken);

    public ValueTask SetEntryAsync<T>(
        string key,
        in CacheStoreEntryWrite<T> entry,
        CancellationToken cancellationToken
    ) => ((IFactoryCacheStore)cache).SetEntryAsync(key, in entry, cancellationToken);

    public ValueTask TryRearmSlidingAsync(
        string key,
        TimeSpan slidingExpiration,
        DateTime physicalExpiresAt,
        DateTime now,
        CancellationToken cancellationToken
    ) =>
        ((IFactoryCacheStore)cache).TryRearmSlidingAsync(
            key,
            slidingExpiration,
            physicalExpiresAt,
            now,
            cancellationToken
        );

    public ValueTask<bool> UpsertAsync<T>(
        string key,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => cache.UpsertAsync(key, value, expiration, cancellationToken);

    public ValueTask<bool> UpsertEntryAsync<T>(
        string key,
        T? value,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    ) => cache.UpsertEntryAsync(key, value, options, cancellationToken);

    public ValueTask<int> UpsertAllAsync<T>(
        IDictionary<string, T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => cache.UpsertAllAsync(value, expiration, cancellationToken);

    public ValueTask<bool> TryInsertAsync<T>(
        string key,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => cache.TryInsertAsync(key, value, expiration, cancellationToken);

    public ValueTask<bool> TryReplaceAsync<T>(
        string key,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => cache.TryReplaceAsync(key, value, expiration, cancellationToken);

    public ValueTask<bool> TryReplaceIfEqualAsync<T>(
        string key,
        T? expected,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => cache.TryReplaceIfEqualAsync(key, expected, value, expiration, cancellationToken);

    public ValueTask<double> IncrementAsync(
        string key,
        double amount,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => cache.IncrementAsync(key, amount, expiration, cancellationToken);

    public ValueTask<long> IncrementAsync(
        string key,
        long amount,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => cache.IncrementAsync(key, amount, expiration, cancellationToken);

    public ValueTask<double> SetIfHigherAsync(
        string key,
        double value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => cache.SetIfHigherAsync(key, value, expiration, cancellationToken);

    public ValueTask<long> SetIfHigherAsync(
        string key,
        long value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => cache.SetIfHigherAsync(key, value, expiration, cancellationToken);

    public ValueTask<double> SetIfLowerAsync(
        string key,
        double value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => cache.SetIfLowerAsync(key, value, expiration, cancellationToken);

    public ValueTask<long> SetIfLowerAsync(
        string key,
        long value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => cache.SetIfLowerAsync(key, value, expiration, cancellationToken);

    public ValueTask<long> SetAddAsync<T>(
        string key,
        IEnumerable<T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => cache.SetAddAsync(key, value, expiration, cancellationToken);

    public ValueTask<IDictionary<string, CacheValue<T>>> GetAllAsync<T>(
        IEnumerable<string> cacheKeys,
        CancellationToken cancellationToken = default
    ) => cache.GetAllAsync<T>(cacheKeys, cancellationToken);

    public ValueTask<IDictionary<string, CacheValue<T>>> GetByPrefixAsync<T>(
        string prefix,
        CancellationToken cancellationToken = default
    ) => cache.GetByPrefixAsync<T>(prefix, cancellationToken);

    public ValueTask<IReadOnlyList<string>> GetAllKeysByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    ) => cache.GetAllKeysByPrefixAsync(prefix, cancellationToken);

    public ValueTask<CacheValue<T>> GetAsync<T>(string key, CancellationToken cancellationToken = default) =>
        cache.GetAsync<T>(key, cancellationToken);

    public ValueTask<long> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default) =>
        cache.GetCountAsync(prefix, cancellationToken);

    public ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) =>
        cache.ExistsAsync(key, cancellationToken);

    public ValueTask<TimeSpan?> GetExpirationAsync(string key, CancellationToken cancellationToken = default) =>
        cache.GetExpirationAsync(key, cancellationToken);

    public ValueTask<CacheValue<ICollection<T>>> GetSetAsync<T>(
        string key,
        int? pageIndex = null,
        int pageSize = 100,
        CancellationToken cancellationToken = default
    ) => cache.GetSetAsync<T>(key, pageIndex, pageSize, cancellationToken);

    public ValueTask<bool> RemoveAsync(string key, CancellationToken cancellationToken = default) =>
        cache.RemoveAsync(key, cancellationToken);

    public ValueTask<bool> RemoveIfEqualAsync<T>(
        string key,
        T? expected,
        CancellationToken cancellationToken = default
    ) => cache.RemoveIfEqualAsync(key, expected, cancellationToken);

    public ValueTask<int> RemoveAllAsync(
        IEnumerable<string> cacheKeys,
        CancellationToken cancellationToken = default
    ) => cache.RemoveAllAsync(cacheKeys, cancellationToken);

    public ValueTask<int> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default) =>
        cache.RemoveByPrefixAsync(prefix, cancellationToken);

    public ValueTask<int> RemoveByTagAsync(string tag, CancellationToken cancellationToken = default) =>
        cache.RemoveByTagAsync(tag, cancellationToken);

    public ValueTask<long> SetRemoveAsync<T>(
        string key,
        IEnumerable<T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => cache.SetRemoveAsync(key, value, expiration, cancellationToken);

    public ValueTask FlushAsync(CancellationToken cancellationToken = default) => cache.FlushAsync(cancellationToken);
}

/// <summary>
/// An L2 remote cache whose read (TryGetEntryAsync) always throws to simulate a down store.
/// Write operations are no-ops so the factory-success path still works if needed.
/// </summary>
internal sealed class ThrowingReadRemoteCache(TimeProvider timeProvider) : IRemoteCache, IFactoryCacheStore, IDisposable
{
    public CacheEntryOptions? DefaultEntryOptions => null;

    private readonly InMemoryCache _inner = new(timeProvider, new InMemoryCacheOptions());

    public ValueTask<CacheStoreEntry<T>> TryGetEntryAsync<T>(string key, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("L2 store is unavailable");

    public ValueTask SetEntryAsync<T>(
        string key,
        in CacheStoreEntryWrite<T> entry,
        CancellationToken cancellationToken
    ) =>
        // No-op: writes are silently dropped (non-fatal in HybridCache.SetEntryAsync)
        ValueTask.CompletedTask;

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
    ) => new((IDictionary<string, CacheValue<T>>)new Dictionary<string, CacheValue<T>>(StringComparer.Ordinal));

    public ValueTask<IDictionary<string, CacheValue<T>>> GetByPrefixAsync<T>(
        string prefix,
        CancellationToken cancellationToken = default
    ) => new((IDictionary<string, CacheValue<T>>)new Dictionary<string, CacheValue<T>>(StringComparer.Ordinal));

    public ValueTask<IReadOnlyList<string>> GetAllKeysByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    ) => new((IReadOnlyList<string>)Array.Empty<string>());

    public ValueTask<long> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default) => new(0L);

    public ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) => new(false);

    public ValueTask<TimeSpan?> GetExpirationAsync(string key, CancellationToken cancellationToken = default) =>
        new((TimeSpan?)null);

    public ValueTask<CacheValue<ICollection<T>>> GetSetAsync<T>(
        string key,
        int? pageIndex = null,
        int pageSize = 100,
        CancellationToken cancellationToken = default
    ) => new(CacheValue<ICollection<T>>.NoValue);

    public ValueTask<bool> RemoveAsync(string key, CancellationToken cancellationToken = default) => new(false);

    public ValueTask<bool> RemoveIfEqualAsync<T>(
        string key,
        T? expected,
        CancellationToken cancellationToken = default
    ) => new(false);

    public ValueTask<int> RemoveAllAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default) =>
        new(0);

    public ValueTask<int> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default) => new(0);

    public ValueTask<int> RemoveByTagAsync(string tag, CancellationToken cancellationToken = default) => new(0);

    public ValueTask<long> SetRemoveAsync<T>(
        string key,
        IEnumerable<T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => new(0L);

    public ValueTask FlushAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public void Dispose() => _inner.Dispose();
}

/// <summary>
/// An L2 remote cache whose TryGetEntryAsync always returns a found entry with null
/// LogicalExpiresAt and PhysicalExpiresAt, simulating a legacy/unframed L2 value that
/// carries no expiration metadata (e.g., written by an older version of the cache layer).
/// </summary>
internal sealed class NullTimestampL2Adapter<TValue>(TValue value) : IRemoteCache, IFactoryCacheStore, IDisposable
{
    public CacheEntryOptions? DefaultEntryOptions => null;

    public ValueTask<CacheStoreEntry<T>> TryGetEntryAsync<T>(string key, CancellationToken cancellationToken)
    {
        // Return a found entry with null timestamps regardless of type requested.
        // The test uses int, so cast works; for other types this returns default.
        var typedValue = value is T typed ? typed : default;
        var entry = new CacheStoreEntry<T>(
            Found: true,
            IsNull: false,
            Value: typedValue,
            LogicalExpiresAt: null,
            PhysicalExpiresAt: null,
            SlidingExpiration: null
        );
        return new ValueTask<CacheStoreEntry<T>>(entry);
    }

    public ValueTask SetEntryAsync<T>(
        string key,
        in CacheStoreEntryWrite<T> entry,
        CancellationToken cancellationToken
    ) => ValueTask.CompletedTask;

    public ValueTask TryRearmSlidingAsync(
        string key,
        TimeSpan slidingExpiration,
        DateTime physicalExpiresAt,
        DateTime now,
        CancellationToken cancellationToken
    ) => ValueTask.CompletedTask;

    public ValueTask<CacheValue<T>> GetOrAddAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T?>> factory,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    ) => new(CacheValue<T>.NoValue);

    public ValueTask<CacheValue<T>> GetOrAddAsync<T>(
        string key,
        Func<CacheFactoryContext<T>, CancellationToken, ValueTask<CacheFactoryResult<T>>> factory,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    ) => new(CacheValue<T>.NoValue);

    public ValueTask<bool> UpsertAsync<T>(
        string key,
        T? val,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => new(true);

    public ValueTask<bool> UpsertEntryAsync<T>(
        string key,
        T? val,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    ) => new(true);

    public ValueTask<int> UpsertAllAsync<T>(
        IDictionary<string, T> val,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => new(val.Count);

    public ValueTask<bool> TryInsertAsync<T>(
        string key,
        T? val,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => new(true);

    public ValueTask<bool> TryReplaceAsync<T>(
        string key,
        T? val,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => new(true);

    public ValueTask<bool> TryReplaceIfEqualAsync<T>(
        string key,
        T? expected,
        T? val,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => new(true);

    public ValueTask<double> IncrementAsync(
        string key,
        double amount,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => new(amount);

    public ValueTask<long> IncrementAsync(
        string key,
        long amount,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => new(amount);

    public ValueTask<double> SetIfHigherAsync(
        string key,
        double val,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => new(val);

    public ValueTask<long> SetIfHigherAsync(
        string key,
        long val,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => new(val);

    public ValueTask<double> SetIfLowerAsync(
        string key,
        double val,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => new(val);

    public ValueTask<long> SetIfLowerAsync(
        string key,
        long val,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => new(val);

    public ValueTask<long> SetAddAsync<T>(
        string key,
        IEnumerable<T> val,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => new(0L);

    public ValueTask<CacheValue<T>> GetAsync<T>(string key, CancellationToken cancellationToken = default) =>
        new(CacheValue<T>.NoValue);

    public ValueTask<IDictionary<string, CacheValue<T>>> GetAllAsync<T>(
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default
    ) => new((IDictionary<string, CacheValue<T>>)new Dictionary<string, CacheValue<T>>(StringComparer.Ordinal));

    public ValueTask<IDictionary<string, CacheValue<T>>> GetByPrefixAsync<T>(
        string prefix,
        CancellationToken cancellationToken = default
    ) => new((IDictionary<string, CacheValue<T>>)new Dictionary<string, CacheValue<T>>(StringComparer.Ordinal));

    public ValueTask<IReadOnlyList<string>> GetAllKeysByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    ) => new((IReadOnlyList<string>)Array.Empty<string>());

    public ValueTask<long> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default) => new(0L);

    public ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) => new(true);

    public ValueTask<TimeSpan?> GetExpirationAsync(string key, CancellationToken cancellationToken = default) =>
        new((TimeSpan?)null);

    public ValueTask<CacheValue<ICollection<T>>> GetSetAsync<T>(
        string key,
        int? pageIndex = null,
        int pageSize = 100,
        CancellationToken cancellationToken = default
    ) => new(CacheValue<ICollection<T>>.NoValue);

    public ValueTask<bool> RemoveAsync(string key, CancellationToken cancellationToken = default) => new(true);

    public ValueTask<bool> RemoveIfEqualAsync<T>(
        string key,
        T? expected,
        CancellationToken cancellationToken = default
    ) => new(true);

    public ValueTask<int> RemoveAllAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default) =>
        new(0);

    public ValueTask<int> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default) => new(0);

    public ValueTask<int> RemoveByTagAsync(string tag, CancellationToken cancellationToken = default) => new(0);

    public ValueTask<long> SetRemoveAsync<T>(
        string key,
        IEnumerable<T> val,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => new(0L);

    public ValueTask FlushAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public void Dispose() { }
}

/// <summary>
/// An L2 remote cache whose write operations (entry sets, scalar upserts, removes) can be toggled to fail,
/// simulating a transient outage for auto-recovery tests. Reads always work. Counts write attempts so tests
/// can assert barrier/retry behavior.
/// </summary>
internal sealed class TogglableRemoteCache(TimeProvider timeProvider) : IRemoteCache, IFactoryCacheStore, IDisposable
{
    private readonly InMemoryCache _cache = new(timeProvider, new InMemoryCacheOptions { CloneValues = true });

    public CacheEntryOptions? DefaultEntryOptions => null;

    /// <summary>When true, entry sets, scalar upserts, and removes throw.</summary>
    public bool FailWrites { get; set; }

    public int SetEntryAttempts { get; private set; }

    public int UpsertAttempts { get; private set; }

    public int RemoveAttempts { get; private set; }

    public ValueTask<CacheStoreEntry<T>> TryGetEntryAsync<T>(string key, CancellationToken cancellationToken) =>
        ((IFactoryCacheStore)_cache).TryGetEntryAsync<T>(key, cancellationToken);

    public ValueTask SetEntryAsync<T>(string key, in CacheStoreEntryWrite<T> entry, CancellationToken cancellationToken)
    {
        SetEntryAttempts++;

        return FailWrites
            ? throw new InvalidOperationException("L2 write failed")
            : ((IFactoryCacheStore)_cache).SetEntryAsync(key, in entry, cancellationToken);
    }

    public ValueTask TryRearmSlidingAsync(
        string key,
        TimeSpan slidingExpiration,
        DateTime physicalExpiresAt,
        DateTime now,
        CancellationToken cancellationToken
    ) =>
        ((IFactoryCacheStore)_cache).TryRearmSlidingAsync(
            key,
            slidingExpiration,
            physicalExpiresAt,
            now,
            cancellationToken
        );

    public ValueTask<CacheValue<T>> GetOrAddAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T?>> factory,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    ) => _cache.GetOrAddAsync(key, factory, options, cancellationToken);

    public ValueTask<CacheValue<T>> GetOrAddAsync<T>(
        string key,
        Func<CacheFactoryContext<T>, CancellationToken, ValueTask<CacheFactoryResult<T>>> factory,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    ) => _cache.GetOrAddAsync(key, factory, options, cancellationToken);

    public ValueTask<bool> UpsertAsync<T>(
        string key,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        UpsertAttempts++;

        return FailWrites
            ? throw new InvalidOperationException("L2 write failed")
            : _cache.UpsertAsync(key, value, expiration, cancellationToken);
    }

    public ValueTask<bool> UpsertEntryAsync<T>(
        string key,
        T? value,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    ) => _cache.UpsertEntryAsync(key, value, options, cancellationToken);

    public ValueTask<int> UpsertAllAsync<T>(
        IDictionary<string, T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) =>
        FailWrites
            ? throw new InvalidOperationException("L2 write failed")
            : _cache.UpsertAllAsync(value, expiration, cancellationToken);

    public ValueTask<bool> TryInsertAsync<T>(
        string key,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => _cache.TryInsertAsync(key, value, expiration, cancellationToken);

    public ValueTask<bool> TryReplaceAsync<T>(
        string key,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => _cache.TryReplaceAsync(key, value, expiration, cancellationToken);

    public ValueTask<bool> TryReplaceIfEqualAsync<T>(
        string key,
        T? expected,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => _cache.TryReplaceIfEqualAsync(key, expected, value, expiration, cancellationToken);

    public ValueTask<double> IncrementAsync(
        string key,
        double amount,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => _cache.IncrementAsync(key, amount, expiration, cancellationToken);

    public ValueTask<long> IncrementAsync(
        string key,
        long amount,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => _cache.IncrementAsync(key, amount, expiration, cancellationToken);

    public ValueTask<double> SetIfHigherAsync(
        string key,
        double value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => _cache.SetIfHigherAsync(key, value, expiration, cancellationToken);

    public ValueTask<long> SetIfHigherAsync(
        string key,
        long value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => _cache.SetIfHigherAsync(key, value, expiration, cancellationToken);

    public ValueTask<double> SetIfLowerAsync(
        string key,
        double value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => _cache.SetIfLowerAsync(key, value, expiration, cancellationToken);

    public ValueTask<long> SetIfLowerAsync(
        string key,
        long value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => _cache.SetIfLowerAsync(key, value, expiration, cancellationToken);

    public ValueTask<long> SetAddAsync<T>(
        string key,
        IEnumerable<T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => _cache.SetAddAsync(key, value, expiration, cancellationToken);

    public ValueTask<IDictionary<string, CacheValue<T>>> GetAllAsync<T>(
        IEnumerable<string> cacheKeys,
        CancellationToken cancellationToken = default
    ) => _cache.GetAllAsync<T>(cacheKeys, cancellationToken);

    public ValueTask<IDictionary<string, CacheValue<T>>> GetByPrefixAsync<T>(
        string prefix,
        CancellationToken cancellationToken = default
    ) => _cache.GetByPrefixAsync<T>(prefix, cancellationToken);

    public ValueTask<IReadOnlyList<string>> GetAllKeysByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    ) => _cache.GetAllKeysByPrefixAsync(prefix, cancellationToken);

    public ValueTask<CacheValue<T>> GetAsync<T>(string key, CancellationToken cancellationToken = default) =>
        _cache.GetAsync<T>(key, cancellationToken);

    public ValueTask<long> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default) =>
        _cache.GetCountAsync(prefix, cancellationToken);

    public ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) =>
        _cache.ExistsAsync(key, cancellationToken);

    public ValueTask<TimeSpan?> GetExpirationAsync(string key, CancellationToken cancellationToken = default) =>
        _cache.GetExpirationAsync(key, cancellationToken);

    public ValueTask<CacheValue<ICollection<T>>> GetSetAsync<T>(
        string key,
        int? pageIndex = null,
        int pageSize = 100,
        CancellationToken cancellationToken = default
    ) => _cache.GetSetAsync<T>(key, pageIndex, pageSize, cancellationToken);

    public ValueTask<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        RemoveAttempts++;

        return FailWrites
            ? throw new InvalidOperationException("L2 write failed")
            : _cache.RemoveAsync(key, cancellationToken);
    }

    public ValueTask<bool> RemoveIfEqualAsync<T>(
        string key,
        T? expected,
        CancellationToken cancellationToken = default
    ) => _cache.RemoveIfEqualAsync(key, expected, cancellationToken);

    public ValueTask<int> RemoveAllAsync(
        IEnumerable<string> cacheKeys,
        CancellationToken cancellationToken = default
    ) =>
        FailWrites
            ? throw new InvalidOperationException("L2 write failed")
            : _cache.RemoveAllAsync(cacheKeys, cancellationToken);

    public ValueTask<int> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default) =>
        _cache.RemoveByPrefixAsync(prefix, cancellationToken);

    public ValueTask<int> RemoveByTagAsync(string tag, CancellationToken cancellationToken = default) =>
        _cache.RemoveByTagAsync(tag, cancellationToken);

    public ValueTask<long> SetRemoveAsync<T>(
        string key,
        IEnumerable<T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => _cache.SetRemoveAsync(key, value, expiration, cancellationToken);

    public ValueTask FlushAsync(CancellationToken cancellationToken = default) => _cache.FlushAsync(cancellationToken);

    public void Dispose() => _cache.Dispose();
}

/// <summary>
/// An L2 remote cache whose factory-store read (TryGetEntryAsync) and write (SetEntryAsync) can each be held
/// open behind an optional gate, so tests can cancel the caller while an L2 phase is in flight. The gate waits
/// honor the operation's cancellation token (a cancelled wait throws an OperationCanceledException carrying the
/// caller's token, like a real remote client). All other operations delegate to a real in-memory cache.
/// </summary>
internal sealed class GatedRemoteCache(TimeProvider timeProvider) : IRemoteCache, IFactoryCacheStore, IDisposable
{
    private readonly InMemoryCache _cache = new(timeProvider, new InMemoryCacheOptions { CloneValues = true });

    /// <summary>When set, TryGetEntryAsync blocks on this gate (honoring the token) before reading.</summary>
    public TaskCompletionSource? ReadGate { get; set; }

    /// <summary>When set, SetEntryAsync blocks on this gate (honoring the token) before writing.</summary>
    public TaskCompletionSource? WriteGate { get; set; }

    /// <summary>
    /// When set, the scalar UpsertAsync and bulk UpsertAllAsync block on this gate (honoring the token) before
    /// writing, so tests can assert a caller returns before a backgrounded scalar/bulk L2 write completes.
    /// </summary>
    public TaskCompletionSource? UpsertGate { get; set; }

    /// <summary>Completed when a gated TryGetEntryAsync has started and is parked on the gate.</summary>
    public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completed when a gated SetEntryAsync has started and is parked on the gate.</summary>
    public TaskCompletionSource WriteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completed when a gated UpsertAsync/UpsertAllAsync has started and is parked on the gate.</summary>
    public TaskCompletionSource UpsertStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public CacheEntryOptions? DefaultEntryOptions => null;

    public async ValueTask<CacheStoreEntry<T>> TryGetEntryAsync<T>(string key, CancellationToken cancellationToken)
    {
        if (ReadGate is { } gate)
        {
            ReadStarted.TrySetResult();
            await gate.Task.WaitAsync(cancellationToken);
        }

        return await ((IFactoryCacheStore)_cache).TryGetEntryAsync<T>(key, cancellationToken);
    }

    // Non-async forwarder: `in` parameters are not allowed on async methods, so copy the descriptor by value.
    public ValueTask SetEntryAsync<T>(
        string key,
        in CacheStoreEntryWrite<T> entry,
        CancellationToken cancellationToken
    ) => _SetEntryCoreAsync(key, entry, cancellationToken);

    private async ValueTask _SetEntryCoreAsync<T>(
        string key,
        CacheStoreEntryWrite<T> entry,
        CancellationToken cancellationToken
    )
    {
        if (WriteGate is { } gate)
        {
            WriteStarted.TrySetResult();
            await gate.Task.WaitAsync(cancellationToken);
        }

        await ((IFactoryCacheStore)_cache).SetEntryAsync(key, in entry, cancellationToken);
    }

    public ValueTask TryRearmSlidingAsync(
        string key,
        TimeSpan slidingExpiration,
        DateTime physicalExpiresAt,
        DateTime now,
        CancellationToken cancellationToken
    ) =>
        ((IFactoryCacheStore)_cache).TryRearmSlidingAsync(
            key,
            slidingExpiration,
            physicalExpiresAt,
            now,
            cancellationToken
        );

    public ValueTask<CacheValue<T>> GetOrAddAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T?>> factory,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    ) => _cache.GetOrAddAsync(key, factory, options, cancellationToken);

    public ValueTask<CacheValue<T>> GetOrAddAsync<T>(
        string key,
        Func<CacheFactoryContext<T>, CancellationToken, ValueTask<CacheFactoryResult<T>>> factory,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    ) => _cache.GetOrAddAsync(key, factory, options, cancellationToken);

    public async ValueTask<bool> UpsertAsync<T>(
        string key,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        if (UpsertGate is { } gate)
        {
            UpsertStarted.TrySetResult();
            await gate.Task.WaitAsync(cancellationToken);
        }

        return await _cache.UpsertAsync(key, value, expiration, cancellationToken);
    }

    public ValueTask<bool> UpsertEntryAsync<T>(
        string key,
        T? value,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    ) => _cache.UpsertEntryAsync(key, value, options, cancellationToken);

    public async ValueTask<int> UpsertAllAsync<T>(
        IDictionary<string, T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        if (UpsertGate is { } gate)
        {
            UpsertStarted.TrySetResult();
            await gate.Task.WaitAsync(cancellationToken);
        }

        return await _cache.UpsertAllAsync(value, expiration, cancellationToken);
    }

    public ValueTask<bool> TryInsertAsync<T>(
        string key,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => _cache.TryInsertAsync(key, value, expiration, cancellationToken);

    public ValueTask<bool> TryReplaceAsync<T>(
        string key,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => _cache.TryReplaceAsync(key, value, expiration, cancellationToken);

    public ValueTask<bool> TryReplaceIfEqualAsync<T>(
        string key,
        T? expected,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => _cache.TryReplaceIfEqualAsync(key, expected, value, expiration, cancellationToken);

    public ValueTask<double> IncrementAsync(
        string key,
        double amount,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => _cache.IncrementAsync(key, amount, expiration, cancellationToken);

    public ValueTask<long> IncrementAsync(
        string key,
        long amount,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => _cache.IncrementAsync(key, amount, expiration, cancellationToken);

    public ValueTask<double> SetIfHigherAsync(
        string key,
        double value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => _cache.SetIfHigherAsync(key, value, expiration, cancellationToken);

    public ValueTask<long> SetIfHigherAsync(
        string key,
        long value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => _cache.SetIfHigherAsync(key, value, expiration, cancellationToken);

    public ValueTask<double> SetIfLowerAsync(
        string key,
        double value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => _cache.SetIfLowerAsync(key, value, expiration, cancellationToken);

    public ValueTask<long> SetIfLowerAsync(
        string key,
        long value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => _cache.SetIfLowerAsync(key, value, expiration, cancellationToken);

    public ValueTask<long> SetAddAsync<T>(
        string key,
        IEnumerable<T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => _cache.SetAddAsync(key, value, expiration, cancellationToken);

    public ValueTask<IDictionary<string, CacheValue<T>>> GetAllAsync<T>(
        IEnumerable<string> cacheKeys,
        CancellationToken cancellationToken = default
    ) => _cache.GetAllAsync<T>(cacheKeys, cancellationToken);

    public ValueTask<IDictionary<string, CacheValue<T>>> GetByPrefixAsync<T>(
        string prefix,
        CancellationToken cancellationToken = default
    ) => _cache.GetByPrefixAsync<T>(prefix, cancellationToken);

    public ValueTask<IReadOnlyList<string>> GetAllKeysByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    ) => _cache.GetAllKeysByPrefixAsync(prefix, cancellationToken);

    public ValueTask<CacheValue<T>> GetAsync<T>(string key, CancellationToken cancellationToken = default) =>
        _cache.GetAsync<T>(key, cancellationToken);

    public ValueTask<long> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default) =>
        _cache.GetCountAsync(prefix, cancellationToken);

    public ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) =>
        _cache.ExistsAsync(key, cancellationToken);

    public ValueTask<TimeSpan?> GetExpirationAsync(string key, CancellationToken cancellationToken = default) =>
        _cache.GetExpirationAsync(key, cancellationToken);

    public ValueTask<CacheValue<ICollection<T>>> GetSetAsync<T>(
        string key,
        int? pageIndex = null,
        int pageSize = 100,
        CancellationToken cancellationToken = default
    ) => _cache.GetSetAsync<T>(key, pageIndex, pageSize, cancellationToken);

    public ValueTask<bool> RemoveAsync(string key, CancellationToken cancellationToken = default) =>
        _cache.RemoveAsync(key, cancellationToken);

    public ValueTask<bool> RemoveIfEqualAsync<T>(
        string key,
        T? expected,
        CancellationToken cancellationToken = default
    ) => _cache.RemoveIfEqualAsync(key, expected, cancellationToken);

    public ValueTask<int> RemoveAllAsync(
        IEnumerable<string> cacheKeys,
        CancellationToken cancellationToken = default
    ) => _cache.RemoveAllAsync(cacheKeys, cancellationToken);

    public ValueTask<int> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default) =>
        _cache.RemoveByPrefixAsync(prefix, cancellationToken);

    public ValueTask<int> RemoveByTagAsync(string tag, CancellationToken cancellationToken = default) =>
        _cache.RemoveByTagAsync(tag, cancellationToken);

    public ValueTask<long> SetRemoveAsync<T>(
        string key,
        IEnumerable<T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => _cache.SetRemoveAsync(key, value, expiration, cancellationToken);

    public ValueTask FlushAsync(CancellationToken cancellationToken = default) => _cache.FlushAsync(cancellationToken);

    public void Dispose() => _cache.Dispose();
}
