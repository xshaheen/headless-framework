// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;

namespace Tests;

/// <summary>Simple adapter to use InMemoryCache as IRemoteCache for testing.</summary>
public sealed class InMemoryRemoteCacheAdapter(InMemoryCache cache)
    : IRemoteCache,
        IFactoryCacheStore,
        ISeedableTagMarkerCache
{
    /// <summary>Records backplane marker pushes so tests can assert the hybrid receiver seeds the L2 marker cache.</summary>
    public List<(string Tag, DateTimeOffset At)> SeededTagMarkers { get; } = [];

    /// <summary>Records clear-generation pushes received from the backplane receiver.</summary>
    public List<DateTimeOffset> SeededClearMarkers { get; } = [];

    /// <summary>Records remove-generation (logical flush) pushes received from the backplane receiver.</summary>
    public List<DateTimeOffset> SeededRemoveMarkers { get; } = [];

    public void SeedTagMarker(string tag, DateTimeOffset invalidatedAt) => SeededTagMarkers.Add((tag, invalidatedAt));

    public void SeedClearMarker(DateTimeOffset invalidatedAt) => SeededClearMarkers.Add(invalidatedAt);

    public void SeedRemoveMarker(DateTimeOffset invalidatedAt) => SeededRemoveMarkers.Add(invalidatedAt);

    // Durable marker writes delegate to the inner cache (its marker dictionaries are this adapter's durable store).
    public ValueTask WriteTagMarkerAsync(
        string tag,
        DateTimeOffset invalidatedAt,
        CancellationToken cancellationToken = default
    ) => cache.WriteTagMarkerAsync(tag, invalidatedAt, cancellationToken);

    public ValueTask WriteClearMarkerAsync(
        DateTimeOffset invalidatedAt,
        CancellationToken cancellationToken = default
    ) => cache.WriteClearMarkerAsync(invalidatedAt, cancellationToken);

    // The inner InMemoryCache has no logical remove-generation marker (its FlushAsync wipes physically), so model
    // a durable remove on this InMemory-backed L2 stand-in as a physical flush of the inner cache.
    public ValueTask WriteRemoveMarkerAsync(
        DateTimeOffset invalidatedAt,
        CancellationToken cancellationToken = default
    ) => cache.FlushAsync(cancellationToken);

    public CacheEntryOptions? DefaultEntryOptions => cache.DefaultEntryOptions;

    /// <summary>Counts per-key framed reads issued against this L2 stand-in (proves the O(1) bulk-read fix).</summary>
    public int TryGetEntryCalls { get; private set; }

    /// <summary>Counts bulk framed reads issued against this L2 stand-in (proves the O(1) bulk-read fix).</summary>
    public int TryGetAllEntriesCalls { get; private set; }

    public ValueTask<CacheStoreEntry<T>> TryGetEntryAsync<T>(string key, CancellationToken cancellationToken)
    {
        TryGetEntryCalls++;
        return ((IFactoryCacheStore)cache).TryGetEntryAsync<T>(key, cancellationToken);
    }

    // Delegates to the inner cache's bulk primitive (not this adapter's single-key path), so a bulk cold read
    // registers exactly one TryGetAllEntriesCalls and zero TryGetEntryCalls at the L2 boundary.
    public ValueTask<CacheStoreEntry<T>[]> TryGetAllEntriesAsync<T>(
        IReadOnlyList<string> keys,
        CancellationToken cancellationToken
    )
    {
        TryGetAllEntriesCalls++;
        return ((IFactoryCacheStore)cache).TryGetAllEntriesAsync<T>(keys, cancellationToken);
    }

    public ValueTask<bool> SetEntryAsync<T>(
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

    public async ValueTask<CacheValueWithExpiration<T>> GetWithExpirationAsync<T>(
        string key,
        CancellationToken cancellationToken = default
    )
    {
        var value = await cache.GetAsync<T>(key, cancellationToken).ConfigureAwait(false);

        if (!value.HasValue)
        {
            return new CacheValueWithExpiration<T>(CacheValue<T>.NoValue, null);
        }

        var expiration = await cache.GetExpirationAsync(key, cancellationToken).ConfigureAwait(false);
        return new CacheValueWithExpiration<T>(value, expiration);
    }

    public async ValueTask<IDictionary<string, CacheValueWithExpiration<T>>> GetAllWithExpirationAsync<T>(
        IEnumerable<string> cacheKeys,
        CancellationToken cancellationToken = default
    )
    {
        var values = await cache.GetAllAsync<T>(cacheKeys, cancellationToken).ConfigureAwait(false);
        var result = new Dictionary<string, CacheValueWithExpiration<T>>(values.Count, StringComparer.Ordinal);

        foreach (var (key, value) in values)
        {
            if (!value.HasValue)
            {
                continue;
            }

            var expiration = await cache.GetExpirationAsync(key, cancellationToken).ConfigureAwait(false);
            result[key] = new CacheValueWithExpiration<T>(value, expiration);
        }

        return result;
    }

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

    public ValueTask RefreshAsync(string key, CancellationToken cancellationToken = default) =>
        cache.RefreshAsync(key, cancellationToken);

    public ValueTask<bool> RemoveAsync(string key, CancellationToken cancellationToken = default) =>
        cache.RemoveAsync(key, cancellationToken);

    public ValueTask<bool> ExpireAsync(string key, CancellationToken cancellationToken = default) =>
        cache.ExpireAsync(key, cancellationToken);

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

    public ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default) =>
        cache.RemoveByTagAsync(tag, cancellationToken);

    public ValueTask ClearAsync(CancellationToken cancellationToken = default) => cache.ClearAsync(cancellationToken);

    public ValueTask<long> SetRemoveAsync<T>(
        string key,
        IEnumerable<T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    ) => cache.SetRemoveAsync(key, value, expiration, cancellationToken);

    public ValueTask FlushAsync(CancellationToken cancellationToken = default) => cache.FlushAsync(cancellationToken);
}
