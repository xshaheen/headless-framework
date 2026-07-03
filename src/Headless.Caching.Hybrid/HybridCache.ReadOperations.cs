// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Logging;

namespace Headless.Caching;

public sealed partial class HybridCache
{
    // Upper bound on concurrent per-key framed cold reads fanned out for a single bulk GetAllAsync miss set. Caps the
    // burst of concurrent L2 GETs (and, for tagged entries, the per-key marker MGETs) pushed through the shared L2
    // multiplexer regardless of caller batch size — mirroring the Redis provider's own multi-key fan-out cap so a large
    // cold batch cannot reintroduce the unbounded-command storm the native bulk path avoids.
    private const int _ColdReadFanOutBatchSize = 250;

    #region ICache - Get Operations

    /// <inheritdoc />
    public async ValueTask<CacheValue<T>> GetOrAddAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T?>> factory,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        Argument.IsNotNull(factory);

        cancellationToken.ThrowIfCancellationRequested();

        return await _coordinator.GetOrAddAsync(this, key, factory, options, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<CacheValue<T>> GetOrAddAsync<T>(
        string key,
        Func<CacheFactoryContext<T>, CancellationToken, ValueTask<CacheFactoryResult<T>>> factory,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        Argument.IsNotNull(factory);

        cancellationToken.ThrowIfCancellationRequested();

        return await _coordinator.GetOrAddAsync(this, key, factory, options, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask RefreshAsync(string key, CancellationToken cancellationToken = default)
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        if (_IsDistributedCacheCircuitClosed())
        {
            try
            {
                await l2Cache.RefreshAsync(key, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
                when (!FactoryCacheCoordinator.IsCallerCancellation(exception, cancellationToken))
            {
                _OpenDistributedCacheCircuit(exception, key);
                _logger.LogFailedToRefreshL2Cache(exception, key);
            }
        }

        await LocalCache.RefreshAsync(key, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<CacheValue<T>> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        var cacheValue = await LocalCache.GetAsync<T>(key, cancellationToken).ConfigureAwait(false);
        if (cacheValue.HasValue)
        {
            _logger.LogLocalCacheHit(key);
            Interlocked.Increment(ref _localCacheHits);
            return cacheValue;
        }

        _logger.LogLocalCacheMiss(key);

        // L1 miss -> L2. When both tiers speak the framed-entry contract, route the cold read through the same
        // entry-returning primitive the generic factory cold path uses (IFactoryCacheStore.TryGetEntryAsync): it
        // yields the value plus the Tags + CreatedAt the L1 seed needs in one round-trip, so the seeded L1 copy
        // stays version-pinned for Family-2 tag/clear invalidation. A plain GetWithExpirationAsync read carries no
        // tag metadata, so it would seed a tagless L1 entry that RemoveByTagAsync could never invalidate (stale).
        if (l2Cache is IFactoryCacheStore l2Store && LocalCache is IFactoryCacheStore l1Store)
        {
            var l2EntryRead = await _ReadFromL2Async(
                    key,
                    ct => l2Store.TryGetEntryAsync<T>(key, ct),
                    _SelectDistributedReadTimeout(hasLocalFallback: false, softCanDegradeToMiss: true),
                    DistributedCacheTimeoutKind.Soft,
                    cancellationToken
                )
                .ConfigureAwait(false);

            if (!l2EntryRead.IsSuccess)
            {
                if (l2EntryRead.Exception is { } framedException)
                {
                    _logger.LogFailedToReadFromL2Cache(framedException, key);
                }

                return CacheValue<T>.NoValue;
            }

            var l2Entry = l2EntryRead.Value;

            // A direct read serves only a logically-fresh entry; a stale/tag-invalidated reserve reads as a miss
            // here (serving a stale reserve is the factory coordinator's job, not a plain GetAsync).
            if (!l2Entry.IsFresh(_GetUtcNow()))
            {
                return CacheValue<T>.NoValue;
            }

            // Promote into L1 preserving Tags + CreatedAt via _SetLocalEntryAsync (mirrors the buffer cold path and
            // the generic TryGetEntryAsync promotion gate), so the local copy is version-pinned for invalidation.
            await _SetLocalEntryAsync(l1Store, key, l2Entry, cancellationToken).ConfigureAwait(false);

            return l2Entry.IsNull ? CacheValue<T>.Null : new CacheValue<T>(l2Entry.Value, hasValue: true);
        }

        var l2Read = await _ReadFromL2Async(
                key,
                ct => l2Cache.GetWithExpirationAsync<T>(key, ct),
                _SelectDistributedReadTimeout(hasLocalFallback: false, softCanDegradeToMiss: true),
                DistributedCacheTimeoutKind.Soft,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (!l2Read.IsSuccess)
        {
            if (l2Read.Exception is { } exception)
            {
                _logger.LogFailedToReadFromL2Cache(exception, key);
            }

            return CacheValue<T>.NoValue;
        }

        cacheValue = l2Read.Value.Value;

        if (cacheValue.HasValue)
        {
            var localExpiration = _GetLocalExpiration(l2Read.Value.Expiration);
            _logger.LogSettingLocalCacheKey(key, localExpiration);
            await LocalCache
                .UpsertAsync(key, cacheValue.Value, localExpiration, cancellationToken)
                .ConfigureAwait(false);
        }

        return cacheValue;
    }

    /// <inheritdoc />
    public async ValueTask<IDictionary<string, CacheValue<T>>> GetAllAsync<T>(
        IEnumerable<string> cacheKeys,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNull(cacheKeys);
        cancellationToken.ThrowIfCancellationRequested();

        var keysCollection = cacheKeys.AsICollection();
        if (keysCollection.Count == 0)
        {
            return new Dictionary<string, CacheValue<T>>(StringComparer.Ordinal);
        }

        var localValues = await LocalCache.GetAllAsync<T>(keysCollection, cancellationToken).ConfigureAwait(false);

        // Collect keys that weren't found in local cache
        var missedKeys = new List<string>(keysCollection.Count);
        foreach (var kvp in localValues)
        {
            if (kvp.Value.HasValue)
            {
                _logger.LogLocalCacheHit(kvp.Key);
            }
            else
            {
                _logger.LogLocalCacheMiss(kvp.Key);
                missedKeys.Add(kvp.Key);
            }
        }

        Interlocked.Add(ref _localCacheHits, keysCollection.Count - missedKeys.Count);

        // All keys found in local cache
        if (missedKeys.Count == 0)
        {
            return localValues;
        }

        var result = new Dictionary<string, CacheValue<T>>(localValues, StringComparer.Ordinal);

        // When both tiers speak the framed-entry contract, route the cold read through the per-key TryGetEntryAsync
        // primitive so each L1 seed carries Tags + CreatedAt (Family-2 version-pinning). A plain bulk
        // GetAllWithExpirationAsync read returns no tag metadata, so it would seed tagless L1 copies that
        // RemoveByTagAsync could never invalidate (serving stale).
        if (l2Cache is IFactoryCacheStore l2Store && LocalCache is IFactoryCacheStore l1Store)
        {
            return await _GetAllColdReadFromFramedL2Async(missedKeys, result, l2Store, l1Store, cancellationToken)
                .ConfigureAwait(false);
        }

        var distributedRead = await _ReadFromL2Async(
                // Diagnostic-only label: _ReadFromL2Async uses this key solely for timeout/circuit log fields, not
                // for the read itself (the read is the delegate below). A synthetic bulk marker keeps the logs from
                // looking single-key.
                $"[bulk:{missedKeys.Count}]",
                ct => l2Cache.GetAllWithExpirationAsync<T>(missedKeys, ct),
                _SelectDistributedReadTimeout(hasLocalFallback: false, softCanDegradeToMiss: true),
                DistributedCacheTimeoutKind.Soft,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (!distributedRead.IsSuccess)
        {
            // Include a small key sample so operators can identify the affected keys without a single-key flood.
            var keySample = string.Join(", ", missedKeys.Take(5));

            if (distributedRead.Exception is { } exception)
            {
                // Degrade to the partial L1 result, mirroring the single-key GetAsync contract:
                // an L2 read fault is logged then swallowed so callers always get a best-effort response.
                _logger.LogFailedBulkL2CacheOperationWithSample(exception, missedKeys.Count, keySample);
            }
            else
            {
                // Timeout or circuit-open: same degrade contract, but no exception to attach to the log entry.
                _logger.LogBulkDistributedCacheReadDegradedWithSample(
                    missedKeys.Count,
                    distributedRead.Status.ToString(),
                    keySample
                );
            }

            return result;
        }

        var distributedResults = distributedRead.Value!;

        // Mirror each L2 hit into L1 capped by its exact remaining L2 logical expiration (so the L1 copy never
        // outlives L2 freshness). The enriched bulk read returns value + expiration in one call — no separate
        // per-key expiration round-trips needed. Collect the hits first, then fan the L1 upserts out in parallel
        // (each carries its own capped expiration, so a single shared-expiration batch call cannot be used).
        List<Task>? localUpserts = null;

        foreach (var kvp in distributedResults)
        {
            result[kvp.Key] = kvp.Value.Value;

            if (kvp.Value.Value is { HasValue: true, Value: not null })
            {
                var localExpiration = _GetLocalExpiration(kvp.Value.Expiration);
                _logger.LogSettingLocalCacheKey(kvp.Key, localExpiration);
                (localUpserts ??= []).Add(
                    LocalCache.UpsertAsync(kvp.Key, kvp.Value.Value.Value, localExpiration, cancellationToken).AsTask()
                );
            }
        }

        if (localUpserts is not null)
        {
            await Task.WhenAll(localUpserts).ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>
    /// Cold bulk-read tail when both tiers implement <see cref="IFactoryCacheStore"/>: fans the missed keys out as
    /// per-key framed <see cref="IFactoryCacheStore.TryGetEntryAsync{T}"/> reads (value + expiration + Tags +
    /// CreatedAt) under one circuit/timeout boundary, fills <paramref name="result"/>, and seeds each
    /// logically-fresh hit into L1 via <see cref="_SetLocalEntryAsync{T}"/> so the local copy carries the tag
    /// metadata Family-2 invalidation version-pins against — unlike the bulk <c>GetAllWithExpirationAsync</c>
    /// fallback, whose result carries no tags. On a tripped circuit or read fault the whole batch degrades to the
    /// partial L1 result.
    /// </summary>
    private async ValueTask<IDictionary<string, CacheValue<T>>> _GetAllColdReadFromFramedL2Async<T>(
        List<string> missedKeys,
        Dictionary<string, CacheValue<T>> result,
        IFactoryCacheStore l2Store,
        IFactoryCacheStore l1Store,
        CancellationToken cancellationToken
    )
    {
        // Fan the per-key framed reads out under a single _ReadFromL2Async wrapper (matching the bulk read's one
        // circuit/timeout boundary) so the whole batch degrades together to the partial L1 result on fault/timeout.
        var distributedRead = await _ReadFromL2Async(
                // Diagnostic-only label: _ReadFromL2Async uses this key solely for timeout/circuit log fields.
                $"[bulk:{missedKeys.Count}]",
                async ct =>
                {
                    // Bound the fan-out: process the missed keys in fixed-size chunks (each chunk fully awaited before
                    // the next starts) so the concurrent L2 command backlog — and, for tagged entries, the per-key
                    // marker-MGET burst — is capped regardless of caller batch size. Results stay position-aligned with
                    // missedKeys so the freshness/seed loop below can index them directly.
                    var entries = new CacheStoreEntry<T>[missedKeys.Count];

                    for (var offset = 0; offset < missedKeys.Count; offset += _ColdReadFanOutBatchSize)
                    {
                        ct.ThrowIfCancellationRequested();

                        var count = Math.Min(_ColdReadFanOutBatchSize, missedKeys.Count - offset);
                        var reads = new Task<CacheStoreEntry<T>>[count];
                        for (var i = 0; i < count; i++)
                        {
                            reads[i] = l2Store.TryGetEntryAsync<T>(missedKeys[offset + i], ct).AsTask();
                        }

                        var chunk = await Task.WhenAll(reads).ConfigureAwait(false);
                        Array.Copy(chunk, 0, entries, offset, count);
                    }

                    return entries;
                },
                _SelectDistributedReadTimeout(hasLocalFallback: false, softCanDegradeToMiss: true),
                DistributedCacheTimeoutKind.Soft,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (!distributedRead.IsSuccess)
        {
            // Include a small key sample so operators can identify the affected keys without a single-key flood.
            var keySample = string.Join(", ", missedKeys.Take(5));

            if (distributedRead.Exception is { } exception)
            {
                // Degrade to the partial L1 result, mirroring the bulk GetAllAsync contract.
                _logger.LogFailedBulkL2CacheOperationWithSample(exception, missedKeys.Count, keySample);
            }
            else
            {
                // Timeout or circuit-open: same degrade contract, but no exception to attach to the log entry.
                _logger.LogBulkDistributedCacheReadDegradedWithSample(
                    missedKeys.Count,
                    distributedRead.Status.ToString(),
                    keySample
                );
            }

            return result;
        }

        var entries = distributedRead.Value!;
        var now = _GetUtcNow();
        List<Task>? localSeeds = null;

        for (var i = 0; i < missedKeys.Count; i++)
        {
            var key = missedKeys[i];
            var entry = entries[i];

            // A direct read serves only a logically-fresh entry; a stale/tag-invalidated reserve reads as a miss.
            if (!entry.IsFresh(now))
            {
                result[key] = CacheValue<T>.NoValue;
                continue;
            }

            result[key] = entry.IsNull ? CacheValue<T>.Null : new CacheValue<T>(entry.Value, hasValue: true);

            // Promote into L1 preserving Tags + CreatedAt (mirrors the generic TryGetEntryAsync promotion gate).
            (localSeeds ??= []).Add(_SetLocalEntryAsync(l1Store, key, entry, cancellationToken).AsTask());
        }

        if (localSeeds is not null)
        {
            await Task.WhenAll(localSeeds).ConfigureAwait(false);
        }

        return result;
    }

    /// <inheritdoc />
    public async ValueTask<IDictionary<string, CacheValue<T>>> GetByPrefixAsync<T>(
        string prefix,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNull(prefix);
        cancellationToken.ThrowIfCancellationRequested();

        // Prefix queries go straight to L2 (L1 may not hold every matching key), but still through the L2
        // resilience wrapper so a tripped circuit or a slow provider degrades to an empty result instead of
        // throwing or hanging past the configured timeout.
        var l2Read = await _ReadFromL2Async(
                prefix,
                ct => l2Cache.GetByPrefixAsync<T>(prefix, ct),
                _SelectDistributedReadTimeout(hasLocalFallback: false, softCanDegradeToMiss: true),
                DistributedCacheTimeoutKind.Soft,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (!l2Read.IsSuccess)
        {
            if (l2Read.Exception is { } exception)
            {
                _logger.LogFailedToReadFromL2Cache(exception, prefix);
            }

            return new Dictionary<string, CacheValue<T>>(StringComparer.Ordinal);
        }

        return l2Read.Value!;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<string>> GetAllKeysByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNull(prefix);
        cancellationToken.ThrowIfCancellationRequested();

        // Route through the L2 resilience wrapper (circuit breaker + soft timeout) and degrade to an empty list
        // on a tripped circuit or read fault rather than throwing.
        var l2Read = await _ReadFromL2Async(
                prefix,
                ct => l2Cache.GetAllKeysByPrefixAsync(prefix, ct),
                _SelectDistributedReadTimeout(hasLocalFallback: false, softCanDegradeToMiss: true),
                DistributedCacheTimeoutKind.Soft,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (!l2Read.IsSuccess)
        {
            if (l2Read.Exception is { } exception)
            {
                _logger.LogFailedToReadFromL2Cache(exception, prefix);
            }

            return [];
        }

        return l2Read.Value!;
    }

    /// <inheritdoc />
    public async ValueTask<long> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default)
    {
        _ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        // Route through the L2 resilience wrapper (circuit breaker + soft timeout) and degrade to 0 on a tripped
        // circuit or read fault rather than throwing.
        var l2Read = await _ReadFromL2Async(
                prefix,
                ct => l2Cache.GetCountAsync(prefix, ct),
                _SelectDistributedReadTimeout(hasLocalFallback: false, softCanDegradeToMiss: true),
                DistributedCacheTimeoutKind.Soft,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (!l2Read.IsSuccess)
        {
            if (l2Read.Exception is { } exception)
            {
                _logger.LogFailedToReadFromL2Cache(exception, prefix);
            }

            return 0;
        }

        return l2Read.Value;
    }

    /// <inheritdoc />
    public async ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        // Check local cache first
        var localExists = await LocalCache.ExistsAsync(key, cancellationToken).ConfigureAwait(false);
        if (localExists)
        {
            _logger.LogLocalCacheHit(key);
            Interlocked.Increment(ref _localCacheHits);
            return true;
        }

        _logger.LogLocalCacheMiss(key);
        var l2Read = await _ReadFromL2Async(
                key,
                ct => l2Cache.ExistsAsync(key, ct),
                _SelectDistributedReadTimeout(hasLocalFallback: false, softCanDegradeToMiss: true),
                DistributedCacheTimeoutKind.Soft,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (!l2Read.IsSuccess)
        {
            if (l2Read.Exception is { } exception)
            {
                _logger.LogFailedToReadFromL2Cache(exception, key);
            }

            return false;
        }

        return l2Read.Value;
    }

    /// <inheritdoc />
    public async ValueTask<TimeSpan?> GetExpirationAsync(string key, CancellationToken cancellationToken = default)
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        // Single L1 lookup: a null return means the key is absent, avoiding a separate ExistsAsync round-trip.
        var localExpiration = await LocalCache.GetExpirationAsync(key, cancellationToken).ConfigureAwait(false);
        if (localExpiration is not null)
        {
            _logger.LogLocalCacheHit(key);
            Interlocked.Increment(ref _localCacheHits);
            return localExpiration;
        }

        _logger.LogLocalCacheMiss(key);
        var l2Read = await _ReadFromL2Async(
                key,
                ct => l2Cache.GetExpirationAsync(key, ct),
                _SelectDistributedReadTimeout(hasLocalFallback: false, softCanDegradeToMiss: true),
                DistributedCacheTimeoutKind.Soft,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (!l2Read.IsSuccess)
        {
            if (l2Read.Exception is { } exception)
            {
                _logger.LogFailedToReadFromL2Cache(exception, key);
            }

            return null;
        }

        return l2Read.Value;
    }

    /// <inheritdoc />
    public async ValueTask<CacheValue<ICollection<T>>> GetSetAsync<T>(
        string key,
        int? pageIndex = null,
        int pageSize = 100,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        var cacheValue = await LocalCache
            .GetSetAsync<T>(key, pageIndex, pageSize, cancellationToken)
            .ConfigureAwait(false);
        if (cacheValue.HasValue)
        {
            _logger.LogLocalCacheHit(key);
            Interlocked.Increment(ref _localCacheHits);
            return cacheValue;
        }

        _logger.LogLocalCacheMiss(key);
        var l2Read = await _ReadFromL2Async(
                key,
                async ct =>
                {
                    var value = await l2Cache.GetSetAsync<T>(key, pageIndex, pageSize, ct).ConfigureAwait(false);

                    if (!value.HasValue)
                    {
                        return new CacheValueWithExpiration<ICollection<T>>(value, expiration: null);
                    }

                    var expiration = await l2Cache.GetExpirationAsync(key, ct).ConfigureAwait(false);
                    return new CacheValueWithExpiration<ICollection<T>>(value, expiration);
                },
                _SelectDistributedReadTimeout(hasLocalFallback: false, softCanDegradeToMiss: true),
                DistributedCacheTimeoutKind.Soft,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (!l2Read.IsSuccess)
        {
            if (l2Read.Exception is { } exception)
            {
                _logger.LogFailedToReadFromL2Cache(exception, key);
            }

            return CacheValue<ICollection<T>>.NoValue;
        }

        cacheValue = l2Read.Value.Value;

        if (cacheValue.HasValue)
        {
            // TOCTOU guard: the value and its expiration are read in two separate L2 calls (the lambda above). If
            // the key expires between them, the value is present but Expiration is null; seeding L1 with a null TTL
            // and no DefaultLocalExpiration ceiling would create a never-expiring local entry. Skip the L1 seed in
            // that case and return the value without caching it locally.
            if (l2Read.Value.Expiration is not null || cacheOptions.DefaultLocalExpiration.HasValue)
            {
                var localExpiration = _GetLocalExpiration(l2Read.Value.Expiration);
                _logger.LogSettingLocalCacheKey(key, localExpiration);
                // Use UpsertAsync to replace any existing L1 data (not SetAddAsync which would merge)
                await LocalCache
                    .UpsertAsync(key, cacheValue.Value, localExpiration, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return cacheValue;
    }

    #endregion
}

internal static partial class HybridCacheLoggerExtensions
{
    [LoggerMessage(
        EventId = 21,
        EventName = "FailedBulkL2CacheOperationWithSample",
        Level = LogLevel.Warning,
        Message = "Failed to perform a bulk L2 cache operation for {KeyCount} key(s); degrading to partial L1 result (sample: {KeySample})"
    )]
    public static partial void LogFailedBulkL2CacheOperationWithSample(
        this ILogger logger,
        Exception exception,
        int keyCount,
        string keySample
    );

    [LoggerMessage(
        EventId = 22,
        EventName = "BulkDistributedCacheReadDegradedWithSample",
        Level = LogLevel.Warning,
        Message = "Bulk L2 cache read for {KeyCount} key(s) did not complete ({Reason}); degrading to partial L1 result (sample: {KeySample})"
    )]
    public static partial void LogBulkDistributedCacheReadDegradedWithSample(
        this ILogger logger,
        int keyCount,
        string reason,
        string keySample
    );
}
