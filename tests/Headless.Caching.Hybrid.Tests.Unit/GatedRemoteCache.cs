// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;

namespace Tests;

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

    /// <summary>When set, every read (TryGetEntryAsync, GetWithExpirationAsync, …) throws this before the gate.</summary>
    public Exception? ReadFault { get; set; }

    /// <summary>When set, the factory-store SetEntryAsync throws this before the gate.</summary>
    public Exception? WriteFault { get; set; }

    /// <summary>Completed when a gated TryGetEntryAsync has started and is parked on the gate.</summary>
    public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completed when a gated SetEntryAsync has started and is parked on the gate.</summary>
    public TaskCompletionSource WriteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completed when a gated UpsertAsync/UpsertAllAsync has started and is parked on the gate.</summary>
    public TaskCompletionSource UpsertStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int ReadAttempts { get; private set; }

    public CacheEntryOptions? DefaultEntryOptions => null;

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
        // One gate wait for the whole bulk read (mirrors the single MGET boundary the real store issues), then
        // delegate to the inner cache's bulk primitive.
        await _WaitReadGateAsync(cancellationToken);

        return await ((IFactoryCacheStore)_cache).TryGetAllEntriesAsync<T>(keys, cancellationToken, readOptions);
    }

    private async ValueTask _WaitReadGateAsync(CancellationToken cancellationToken)
    {
        ReadAttempts++;

        if (ReadFault is { } fault)
        {
            throw fault;
        }

        if (ReadGate is { } gate)
        {
            ReadStarted.TrySetResult();
            await gate.Task.WaitAsync(cancellationToken);
        }
    }

    // Non-async forwarder: `in` parameters are not allowed on async methods, so copy the descriptor by value.
    public ValueTask<bool> SetEntryAsync<T>(
        string key,
        in CacheStoreEntryWrite<T> entry,
        CancellationToken cancellationToken
    )
    {
        return _SetEntryCoreAsync(key, entry, cancellationToken);
    }

    private async ValueTask<bool> _SetEntryCoreAsync<T>(
        string key,
        CacheStoreEntryWrite<T> entry,
        CancellationToken cancellationToken
    )
    {
        if (WriteFault is { } fault)
        {
            throw fault;
        }

        if (WriteGate is { } gate)
        {
            WriteStarted.TrySetResult();
            await gate.Task.WaitAsync(cancellationToken);
        }

        return await ((IFactoryCacheStore)_cache).SetEntryAsync(key, in entry, cancellationToken);
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
    )
    {
        return _cache.UpsertEntryAsync(key, value, options, cancellationToken);
    }

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
        return _cache.TryReplaceIfEqualAsync(key, expected, value, expiration, cancellationToken);
    }

    public ValueTask<double> IncrementAsync(
        string key,
        double amount,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return _cache.IncrementAsync(key, amount, expiration, cancellationToken);
    }

    public ValueTask<long> IncrementAsync(
        string key,
        long amount,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return _cache.IncrementAsync(key, amount, expiration, cancellationToken);
    }

    public ValueTask<double> SetIfHigherAsync(
        string key,
        double value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return _cache.SetIfHigherAsync(key, value, expiration, cancellationToken);
    }

    public ValueTask<long> SetIfHigherAsync(
        string key,
        long value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return _cache.SetIfHigherAsync(key, value, expiration, cancellationToken);
    }

    public ValueTask<double> SetIfLowerAsync(
        string key,
        double value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return _cache.SetIfLowerAsync(key, value, expiration, cancellationToken);
    }

    public ValueTask<long> SetIfLowerAsync(
        string key,
        long value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return _cache.SetIfLowerAsync(key, value, expiration, cancellationToken);
    }

    public ValueTask<long> SetAddAsync<T>(
        string key,
        IEnumerable<T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return _cache.SetAddAsync(key, value, expiration, cancellationToken);
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
        await _cache.RefreshAsync(key, cancellationToken);
    }

    public ValueTask<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        return _cache.RemoveAsync(key, cancellationToken);
    }

    public ValueTask<bool> ExpireAsync(string key, CancellationToken cancellationToken = default)
    {
        return _cache.ExpireAsync(key, cancellationToken);
    }

    public ValueTask<bool> RemoveIfEqualAsync<T>(string key, T? expected, CancellationToken cancellationToken = default)
    {
        return _cache.RemoveIfEqualAsync(key, expected, cancellationToken);
    }

    public ValueTask<int> RemoveAllAsync(IEnumerable<string> cacheKeys, CancellationToken cancellationToken = default)
    {
        return _cache.RemoveAllAsync(cacheKeys, cancellationToken);
    }

    public ValueTask<int> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        return _cache.RemoveByPrefixAsync(prefix, cancellationToken);
    }

    public ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        return _cache.RemoveByTagAsync(tag, cancellationToken);
    }

    public ValueTask ClearAsync(CancellationToken cancellationToken = default)
    {
        return _cache.ClearAsync(cancellationToken);
    }

    public ValueTask<long> SetRemoveAsync<T>(
        string key,
        IEnumerable<T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return _cache.SetRemoveAsync(key, value, expiration, cancellationToken);
    }

    public ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        return _cache.FlushAsync(cancellationToken);
    }

    public void Dispose()
    {
        _cache.Dispose();
    }
}
