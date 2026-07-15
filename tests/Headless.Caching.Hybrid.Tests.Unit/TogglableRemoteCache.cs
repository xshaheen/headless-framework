// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;

namespace Tests;

/// <summary>
/// An L2 remote cache whose write operations (entry sets, scalar upserts, removes) can be toggled to fail,
/// simulating a transient outage for auto-recovery tests. Reads always work. Counts write attempts so tests
/// can assert barrier/retry behavior.
/// </summary>
internal sealed class TogglableRemoteCache(TimeProvider timeProvider)
    : IRemoteCache,
        IFactoryCacheStore,
        ISeedableTagMarkerCache,
        IDisposable
{
    private readonly InMemoryCache _cache = new(timeProvider, new InMemoryCacheOptions { CloneValues = true });

    public CacheEntryOptions? DefaultEntryOptions => null;

    /// <summary>When true, entry sets, scalar upserts, and removes throw.</summary>
    public bool FailWrites { get; set; }

    /// <summary>When true, read operations throw.</summary>
    public bool FailReads { get; set; }

    /// <summary>When true, the Family-2 marker bumps (RemoveByTagAsync / ClearAsync / FlushAsync) throw.</summary>
    public bool FailMarkerBumps { get; set; }

    /// <summary>When true, sliding-refresh re-arms throw, simulating an L2 outage during a value-free Refresh.</summary>
    public bool FailRefresh { get; set; }

    public int ReadAttempts { get; private set; }

    public int SetEntryAttempts { get; private set; }

    public int UpsertAttempts { get; private set; }

    public int RemoveAttempts { get; private set; }

    public async ValueTask<CacheStoreEntry<T>> TryGetEntryAsync<T>(
        string key,
        CancellationToken cancellationToken,
        FactoryCacheReadOptions readOptions = default
    )
    {
        await _WaitReadGateAsync(cancellationToken);

        return await ((IFactoryCacheStore)_cache).TryGetEntryAsync<T>(key, cancellationToken, readOptions);
    }

    public async ValueTask<CacheStoreEntry<T>[]> TryGetAllEntriesAsync<T>(
        IReadOnlyList<string> keys,
        CancellationToken cancellationToken,
        FactoryCacheReadOptions readOptions = default
    )
    {
        await _WaitReadGateAsync(cancellationToken);

        return await ((IFactoryCacheStore)_cache).TryGetAllEntriesAsync<T>(keys, cancellationToken, readOptions);
    }

    private ValueTask _WaitReadGateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReadAttempts++;

        if (FailReads)
        {
            throw new InvalidOperationException("L2 read failed");
        }

        return ValueTask.CompletedTask;
    }

    // Shared throw-or-delegate gates for the toggleable write / marker-bump failure families. The flag is
    // evaluated synchronously before the delegate runs, matching the per-method ternaries these replace.
    private ValueTask<TResult> _GuardWrite<TResult>(Func<ValueTask<TResult>> operation)
    {
        return FailWrites ? throw new InvalidOperationException("L2 write failed") : operation();
    }

    private ValueTask _GuardMarkerBump(Func<ValueTask> operation)
    {
        return FailMarkerBumps ? throw new InvalidOperationException("L2 marker bump failed") : operation();
    }

    public ValueTask<bool> SetEntryAsync<T>(
        string key,
        in CacheStoreEntryWrite<T> entry,
        CancellationToken cancellationToken
    )
    {
        SetEntryAttempts++;

        // Stays a ternary: the `in` entry parameter cannot be captured by the _GuardWrite lambda.
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
    )
    {
        return ((IFactoryCacheStore)_cache).TryRearmSlidingAsync(
            key,
            slidingExpiration,
            physicalExpiresAt,
            now,
            cancellationToken
        );
    }

    public ValueTask<CacheValue<T>> GetOrAddAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T?>> factory,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    )
    {
        return _cache.GetOrAddAsync(key, factory, options, cancellationToken);
    }

    public ValueTask<CacheValue<T>> GetOrAddAsync<T>(
        string key,
        Func<CacheFactoryContext<T>, CancellationToken, ValueTask<CacheFactoryResult<T>>> factory,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    )
    {
        return _cache.GetOrAddAsync(key, factory, options, cancellationToken);
    }

    public ValueTask<bool> UpsertAsync<T>(
        string key,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        UpsertAttempts++;

        return _GuardWrite(() => _cache.UpsertAsync(key, value, expiration, cancellationToken));
    }

    public ValueTask<bool> UpsertEntryAsync<T>(
        string key,
        T? value,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    )
    {
        return _cache.UpsertEntryAsync(key, value, options, cancellationToken);
    }

    public ValueTask<int> UpsertAllAsync<T>(
        IDictionary<string, T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return _GuardWrite(() => _cache.UpsertAllAsync(value, expiration, cancellationToken));
    }

    public ValueTask<bool> TryInsertAsync<T>(
        string key,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return _cache.TryInsertAsync(key, value, expiration, cancellationToken);
    }

    public ValueTask<bool> TryReplaceAsync<T>(
        string key,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return _cache.TryReplaceAsync(key, value, expiration, cancellationToken);
    }

    public ValueTask<bool> TryReplaceIfEqualAsync<T>(
        string key,
        T? expected,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return _GuardWrite(() => _cache.TryReplaceIfEqualAsync(key, expected, value, expiration, cancellationToken));
    }

    public ValueTask<double> IncrementAsync(
        string key,
        double amount,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return _GuardWrite(() => _cache.IncrementAsync(key, amount, expiration, cancellationToken));
    }

    public ValueTask<long> IncrementAsync(
        string key,
        long amount,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return _GuardWrite(() => _cache.IncrementAsync(key, amount, expiration, cancellationToken));
    }

    public ValueTask<double> SetIfHigherAsync(
        string key,
        double value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return _GuardWrite(() => _cache.SetIfHigherAsync(key, value, expiration, cancellationToken));
    }

    public ValueTask<long> SetIfHigherAsync(
        string key,
        long value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return _GuardWrite(() => _cache.SetIfHigherAsync(key, value, expiration, cancellationToken));
    }

    public ValueTask<double> SetIfLowerAsync(
        string key,
        double value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return _GuardWrite(() => _cache.SetIfLowerAsync(key, value, expiration, cancellationToken));
    }

    public ValueTask<long> SetIfLowerAsync(
        string key,
        long value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return _GuardWrite(() => _cache.SetIfLowerAsync(key, value, expiration, cancellationToken));
    }

    public ValueTask<long> SetAddAsync<T>(
        string key,
        IEnumerable<T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return _GuardWrite(() => _cache.SetAddAsync(key, value, expiration, cancellationToken));
    }

    public async ValueTask<IDictionary<string, CacheValue<T>>> GetAllAsync<T>(
        IEnumerable<string> cacheKeys,
        CancellationToken cancellationToken = default
    )
    {
        await _WaitReadGateAsync(cancellationToken);

        return await _cache.GetAllAsync<T>(cacheKeys, cancellationToken);
    }

    public async ValueTask<CacheValueWithExpiration<T>> GetWithExpirationAsync<T>(
        string key,
        CancellationToken cancellationToken = default
    )
    {
        await _WaitReadGateAsync(cancellationToken);

        var value = await _cache.GetAsync<T>(key, cancellationToken).ConfigureAwait(false);

        if (!value.HasValue)
        {
            return new CacheValueWithExpiration<T>(CacheValue<T>.NoValue, null);
        }

        var expiration = await _cache.GetExpirationAsync(key, cancellationToken).ConfigureAwait(false);
        return new CacheValueWithExpiration<T>(value, expiration);
    }

    public async ValueTask<IDictionary<string, CacheValueWithExpiration<T>>> GetAllWithExpirationAsync<T>(
        IEnumerable<string> cacheKeys,
        CancellationToken cancellationToken = default
    )
    {
        await _WaitReadGateAsync(cancellationToken);

        var values = await _cache.GetAllAsync<T>(cacheKeys, cancellationToken).ConfigureAwait(false);
        var result = new Dictionary<string, CacheValueWithExpiration<T>>(values.Count, StringComparer.Ordinal);

        foreach (var (key, value) in values)
        {
            if (!value.HasValue)
            {
                continue;
            }

            var expiration = await _cache.GetExpirationAsync(key, cancellationToken).ConfigureAwait(false);
            result[key] = new CacheValueWithExpiration<T>(value, expiration);
        }

        return result;
    }

    public ValueTask<IDictionary<string, CacheValue<T>>> GetByPrefixAsync<T>(
        string prefix,
        CancellationToken cancellationToken = default
    )
    {
        return _cache.GetByPrefixAsync<T>(prefix, cancellationToken);
    }

    public ValueTask<IReadOnlyList<string>> GetAllKeysByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    )
    {
        return _cache.GetAllKeysByPrefixAsync(prefix, cancellationToken);
    }

    public async ValueTask<CacheValue<T>> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        await _WaitReadGateAsync(cancellationToken);

        return await _cache.GetAsync<T>(key, cancellationToken);
    }

    public ValueTask<long> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default)
    {
        return _cache.GetCountAsync(prefix, cancellationToken);
    }

    public async ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        await _WaitReadGateAsync(cancellationToken);

        return await _cache.ExistsAsync(key, cancellationToken);
    }

    public async ValueTask<TimeSpan?> GetExpirationAsync(string key, CancellationToken cancellationToken = default)
    {
        await _WaitReadGateAsync(cancellationToken);

        return await _cache.GetExpirationAsync(key, cancellationToken);
    }

    public async ValueTask<CacheValue<ICollection<T>>> GetSetAsync<T>(
        string key,
        int? pageIndex = null,
        int pageSize = 100,
        CancellationToken cancellationToken = default
    )
    {
        await _WaitReadGateAsync(cancellationToken);

        return await _cache.GetSetAsync<T>(key, pageIndex, pageSize, cancellationToken);
    }

    public async ValueTask RefreshAsync(string key, CancellationToken cancellationToken = default)
    {
        await _WaitReadGateAsync(cancellationToken);

        if (FailRefresh)
        {
            throw new InvalidOperationException("L2 refresh failed");
        }

        await _cache.RefreshAsync(key, cancellationToken);
    }

    public ValueTask<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        RemoveAttempts++;

        return _GuardWrite(() => _cache.RemoveAsync(key, cancellationToken));
    }

    public ValueTask<bool> ExpireAsync(string key, CancellationToken cancellationToken = default)
    {
        RemoveAttempts++;

        return _GuardWrite(() => _cache.ExpireAsync(key, cancellationToken));
    }

    public ValueTask<bool> RemoveIfEqualAsync<T>(string key, T? expected, CancellationToken cancellationToken = default)
    {
        return _cache.RemoveIfEqualAsync(key, expected, cancellationToken);
    }

    public ValueTask<int> RemoveAllAsync(IEnumerable<string> cacheKeys, CancellationToken cancellationToken = default)
    {
        return _GuardWrite(() => _cache.RemoveAllAsync(cacheKeys, cancellationToken));
    }

    public ValueTask<int> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        return _GuardWrite(() => _cache.RemoveByPrefixAsync(prefix, cancellationToken));
    }

    public ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        return _GuardMarkerBump(() => _cache.RemoveByTagAsync(tag, cancellationToken));
    }

    public ValueTask ClearAsync(CancellationToken cancellationToken = default)
    {
        return _GuardMarkerBump(() => _cache.ClearAsync(cancellationToken));
    }

    public ValueTask<long> SetRemoveAsync<T>(
        string key,
        IEnumerable<T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return _GuardWrite(() => _cache.SetRemoveAsync(key, value, expiration, cancellationToken));
    }

    public ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        return _GuardMarkerBump(() => _cache.FlushAsync(cancellationToken));
    }

    // ISeedableTagMarkerCache: a seedable L2 so the Hybrid uses the timestamped marker-write path. Seed* delegate
    // to the inner cache's local marker state; Write* are the durable writes and honor FailMarkerBumps.
    public void SeedTagMarker(string tag, DateTimeOffset invalidatedAt)
    {
        _cache.SeedTagMarker(tag, invalidatedAt);
    }

    public void SeedClearMarker(DateTimeOffset invalidatedAt)
    {
        _cache.SeedClearMarker(invalidatedAt);
    }

    public void SeedRemoveMarker(DateTimeOffset invalidatedAt)
    {
        _cache.SeedRemoveMarker(invalidatedAt);
    }

    public ValueTask WriteTagMarkerAsync(
        string tag,
        DateTimeOffset invalidatedAt,
        CancellationToken cancellationToken = default
    )
    {
        return _GuardMarkerBump(() => _cache.WriteTagMarkerAsync(tag, invalidatedAt, cancellationToken));
    }

    public ValueTask WriteClearMarkerAsync(DateTimeOffset invalidatedAt, CancellationToken cancellationToken = default)
    {
        return _GuardMarkerBump(() => _cache.WriteClearMarkerAsync(invalidatedAt, cancellationToken));
    }

    // Inner InMemoryCache has no logical remove marker (FlushAsync wipes physically), so model a durable remove on
    // this InMemory-backed L2 stand-in as a physical flush of the inner cache.
    public ValueTask WriteRemoveMarkerAsync(DateTimeOffset invalidatedAt, CancellationToken cancellationToken = default)
    {
        return _GuardMarkerBump(() => _cache.FlushAsync(cancellationToken));
    }

    public void Dispose()
    {
        _cache.Dispose();
    }
}
