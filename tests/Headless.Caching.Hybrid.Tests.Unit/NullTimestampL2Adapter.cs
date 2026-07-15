// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;

namespace Tests;

/// <summary>
/// An L2 remote cache whose TryGetEntryAsync always returns a found entry with null
/// LogicalExpiresAt and PhysicalExpiresAt, simulating a legacy/unframed L2 value that
/// carries no expiration metadata (e.g., written by an older version of the cache layer).
/// </summary>
internal sealed class NullTimestampL2Adapter<TValue>(TValue value) : IRemoteCache, IFactoryCacheStore, IDisposable
{
    public CacheEntryOptions? DefaultEntryOptions => null;

    public ValueTask<CacheStoreEntry<T>> TryGetEntryAsync<T>(
        string key,
        CancellationToken cancellationToken,
        FactoryCacheReadOptions readOptions = default
    )
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

    public ValueTask<CacheStoreEntry<T>[]> TryGetAllEntriesAsync<T>(
        IReadOnlyList<string> keys,
        CancellationToken cancellationToken,
        FactoryCacheReadOptions readOptions = default
    )
    {
        // Position-aligned: every key resolves to the same found-with-null-timestamps entry the single-key path
        // returns, modelling a legacy/unframed L2 that carries no expiration metadata.
        var typedValue = value is T typed ? typed : default;
        var result = new CacheStoreEntry<T>[keys.Count];

        for (var i = 0; i < keys.Count; i++)
        {
            result[i] = new CacheStoreEntry<T>(
                Found: true,
                IsNull: false,
                Value: typedValue,
                LogicalExpiresAt: null,
                PhysicalExpiresAt: null,
                SlidingExpiration: null
            );
        }

        return new ValueTask<CacheStoreEntry<T>[]>(result);
    }

    public ValueTask<bool> SetEntryAsync<T>(
        string key,
        in CacheStoreEntryWrite<T> entry,
        CancellationToken cancellationToken
    )
    {
        return new(true);
    }

    public ValueTask TryRearmSlidingAsync(
        string key,
        TimeSpan slidingExpiration,
        DateTime physicalExpiresAt,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask<CacheValue<T>> GetOrAddAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T?>> factory,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    )
    {
        return new(CacheValue<T>.NoValue);
    }

    public ValueTask<CacheValue<T>> GetOrAddAsync<T>(
        string key,
        Func<CacheFactoryContext<T>, CancellationToken, ValueTask<CacheFactoryResult<T>>> factory,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    )
    {
        return new(CacheValue<T>.NoValue);
    }

    public ValueTask<bool> UpsertAsync<T>(
        string key,
        T? val,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return new(true);
    }

    public ValueTask<bool> UpsertEntryAsync<T>(
        string key,
        T? val,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    )
    {
        return new(true);
    }

    public ValueTask<int> UpsertAllAsync<T>(
        IDictionary<string, T> val,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return new(val.Count);
    }

    public ValueTask<bool> TryInsertAsync<T>(
        string key,
        T? val,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return new(true);
    }

    public ValueTask<bool> TryReplaceAsync<T>(
        string key,
        T? val,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return new(true);
    }

    public ValueTask<bool> TryReplaceIfEqualAsync<T>(
        string key,
        T? expected,
        T? val,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return new(true);
    }

    public ValueTask<double> IncrementAsync(
        string key,
        double amount,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return new(amount);
    }

    public ValueTask<long> IncrementAsync(
        string key,
        long amount,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return new(amount);
    }

    public ValueTask<double> SetIfHigherAsync(
        string key,
        double val,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return new(val);
    }

    public ValueTask<long> SetIfHigherAsync(
        string key,
        long val,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return new(val);
    }

    public ValueTask<double> SetIfLowerAsync(
        string key,
        double val,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return new(val);
    }

    public ValueTask<long> SetIfLowerAsync(
        string key,
        long val,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return new(val);
    }

    public ValueTask<long> SetAddAsync<T>(
        string key,
        IEnumerable<T> val,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return new(0L);
    }

    public ValueTask<CacheValue<T>> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        return new(CacheValue<T>.NoValue);
    }

    public ValueTask<IDictionary<string, CacheValue<T>>> GetAllAsync<T>(
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default
    )
    {
        return new(new Dictionary<string, CacheValue<T>>(StringComparer.Ordinal));
    }

    public ValueTask<CacheValueWithExpiration<T>> GetWithExpirationAsync<T>(
        string key,
        CancellationToken cancellationToken = default
    )
    {
        return new(new CacheValueWithExpiration<T>(CacheValue<T>.NoValue, null));
    }

    public ValueTask<IDictionary<string, CacheValueWithExpiration<T>>> GetAllWithExpirationAsync<T>(
        IEnumerable<string> cacheKeys,
        CancellationToken cancellationToken = default
    )
    {
        return new(new Dictionary<string, CacheValueWithExpiration<T>>(StringComparer.Ordinal));
    }

    public ValueTask<IDictionary<string, CacheValue<T>>> GetByPrefixAsync<T>(
        string prefix,
        CancellationToken cancellationToken = default
    )
    {
        return new(new Dictionary<string, CacheValue<T>>(StringComparer.Ordinal));
    }

    public ValueTask<IReadOnlyList<string>> GetAllKeysByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    )
    {
        return new(Array.Empty<string>());
    }

    public ValueTask<long> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default)
    {
        return new(0L);
    }

    public ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        return new(true);
    }

    public ValueTask<TimeSpan?> GetExpirationAsync(string key, CancellationToken cancellationToken = default)
    {
        return new((TimeSpan?)null);
    }

    public ValueTask<CacheValue<ICollection<T>>> GetSetAsync<T>(
        string key,
        int? pageIndex = null,
        int pageSize = 100,
        CancellationToken cancellationToken = default
    )
    {
        return new(CacheValue<ICollection<T>>.NoValue);
    }

    public ValueTask RefreshAsync(string key, CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        return new(true);
    }

    public ValueTask<bool> ExpireAsync(string key, CancellationToken cancellationToken = default)
    {
        return new(true);
    }

    public ValueTask<bool> RemoveIfEqualAsync<T>(string key, T? expected, CancellationToken cancellationToken = default)
    {
        return new(true);
    }

    public ValueTask<int> RemoveAllAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        return new(0);
    }

    public ValueTask<int> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        return new(0);
    }

    public ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask ClearAsync(CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask<long> SetRemoveAsync<T>(
        string key,
        IEnumerable<T> val,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return new(0L);
    }

    public ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }

    public void Dispose() { }
}
