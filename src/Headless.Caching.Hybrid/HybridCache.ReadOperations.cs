// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Logging;

namespace Headless.Caching;

public sealed partial class HybridCache
{
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
                    ct => l2Store.TryGetEntryAsync<T>(key, cancellationToken: ct),
                    _SelectDistributedReadTimeout(hasLocalFallback: false, softCanDegradeToMiss: true),
                    DistributedCacheTimeoutKind.Soft,
                    cancellationToken
                )
                .ConfigureAwait(false);

            if (!l2EntryRead.IsSuccess)
            {
                _LogL2ReadFailure(l2EntryRead.Exception, key);
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
            _LogL2ReadFailure(l2Read.Exception, key);
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
            // Degrade to the partial L1 result, mirroring the single-key GetAsync contract: an L2 read fault or
            // timeout is logged then swallowed so callers always get a best-effort response.
            _LogBulkL2ReadDegraded(distributedRead, missedKeys);
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
    /// Cold bulk-read tail when both tiers implement <see cref="IFactoryCacheStore"/>: reads the missed keys through
    /// the bulk framed <see cref="IFactoryCacheStore.TryGetAllEntriesAsync{T}"/> primitive (value + expiration + Tags
    /// + CreatedAt per key) in one circuit/timeout boundary, fills <paramref name="result"/>, and seeds each
    /// logically-fresh hit into L1 via <see cref="_SetLocalEntryAsync{T}"/> so the local copy carries the tag
    /// metadata Family-2 invalidation version-pins against — unlike the bulk <c>GetAllWithExpirationAsync</c>
    /// fallback, whose result carries no tags. The bulk primitive resolves the batch's invalidation markers with a
    /// single prefetch (O(1) marker round-trips) instead of the O(N) marker MGETs a per-key fan-out incurs (#554).
    /// On a tripped circuit or read fault the whole batch degrades to the partial L1 result.
    /// </summary>
    private async ValueTask<IDictionary<string, CacheValue<T>>> _GetAllColdReadFromFramedL2Async<T>(
        List<string> missedKeys,
        Dictionary<string, CacheValue<T>> result,
        IFactoryCacheStore l2Store,
        IFactoryCacheStore l1Store,
        CancellationToken cancellationToken
    )
    {
        // One bulk read under a single _ReadFromL2Async wrapper (matching the native bulk read's one circuit/timeout
        // boundary) so the whole batch degrades together to the partial L1 result on fault/timeout. The store
        // returns entries position-aligned with missedKeys so the freshness/seed loop below can index them directly,
        // and resolves the batch's clear/remove/tag markers in a single prefetch — O(1) marker round-trips.
        var distributedRead = await _ReadFromL2Async(
                // Diagnostic-only label: _ReadFromL2Async uses this key solely for timeout/circuit log fields.
                $"[bulk:{missedKeys.Count}]",
                ct => l2Store.TryGetAllEntriesAsync<T>(missedKeys, cancellationToken: ct),
                _SelectDistributedReadTimeout(hasLocalFallback: false, softCanDegradeToMiss: true),
                DistributedCacheTimeoutKind.Soft,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (!distributedRead.IsSuccess)
        {
            // Degrade to the partial L1 result, mirroring the bulk GetAllAsync contract.
            _LogBulkL2ReadDegraded(distributedRead, missedKeys);
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
            _LogL2ReadFailure(l2Read.Exception, prefix);
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
            _LogL2ReadFailure(l2Read.Exception, prefix);
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
            _LogL2ReadFailure(l2Read.Exception, prefix);
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
            _LogL2ReadFailure(l2Read.Exception, key);
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
            _LogL2ReadFailure(l2Read.Exception, key);
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

        // Serve straight from L2 without seeding L1. InMemory stores sets as per-member dictionaries
        // (SetAddAsync), so upserting the bare collection returned here would poison the key for the next local
        // GetSetAsync (InvalidCastException on read-back) — and a paged read returns one page, never the whole
        // set, so a seed could also clobber the full set with a single page. Locally-written sets (Hybrid
        // SetAddAsync writes both tiers) still hit L1 above; cold set reads stay L2-authoritative, which also
        // makes this a single L2 round-trip (no companion GetExpirationAsync needed for a seed TTL).
        var l2Read = await _ReadFromL2Async(
                key,
                ct => l2Cache.GetSetAsync<T>(key, pageIndex, pageSize, ct),
                _SelectDistributedReadTimeout(hasLocalFallback: false, softCanDegradeToMiss: true),
                DistributedCacheTimeoutKind.Soft,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (!l2Read.IsSuccess)
        {
            _LogL2ReadFailure(l2Read.Exception, key);
            return CacheValue<ICollection<T>>.NoValue;
        }

        return l2Read.Value;
    }

    /// <summary>
    /// Shared degrade logging for the single-key/prefix L2 read paths: a read fault is logged once and then
    /// swallowed (the read degrades to a miss); timeout and circuit-open outcomes carry no exception and log at the
    /// resilience-wrapper level instead. One helper keeps the degrade log contract identical across the read ops.
    /// </summary>
    private void _LogL2ReadFailure(Exception? exception, string keyOrPrefix)
    {
        if (exception is not null)
        {
            _logger.LogFailedToReadFromL2Cache(exception, keyOrPrefix);
        }
    }

    /// <summary>
    /// Shared degrade tail for the two bulk cold-read paths (native bulk and framed bulk), which must keep an
    /// identical degrade contract: log with a small key sample — so operators can identify the affected keys
    /// without a single-key flood — then the call site falls back to the partial L1 result. Faults carry the
    /// exception; timeout/circuit-open degrades log the status reason instead.
    /// </summary>
    private void _LogBulkL2ReadDegraded<T>(in DistributedCacheReadResult<T> distributedRead, List<string> missedKeys)
    {
        var keySample = string.Join(", ", missedKeys.Take(5));

        if (distributedRead.Exception is { } exception)
        {
            _logger.LogFailedBulkL2CacheOperationWithSample(exception, missedKeys.Count, keySample);
        }
        else
        {
            _logger.LogBulkDistributedCacheReadDegradedWithSample(
                missedKeys.Count,
                distributedRead.Status.ToString(),
                keySample
            );
        }
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
