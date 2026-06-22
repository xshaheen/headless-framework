// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using Headless.Checks;

namespace Headless.Caching;

// Explicit IFactoryCacheStore implementation: the per-entry store primitives the FactoryCacheCoordinator drives.
// These fan the framed entry descriptor across both tiers (L2 then L1, with the local ceiling applied to L1) and
// broadcast peer invalidations for value-bearing writes, keeping the hybrid in sync with the coordinator's stamps.
public sealed partial class HybridCache
{
    async ValueTask<CacheStoreEntry<T>> IFactoryCacheStore.TryGetEntryAsync<T>(
        string key,
        CancellationToken cancellationToken
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        var now = _GetUtcNow();
        CacheStoreEntry<T>? l1StaleCandidate = null;
        var l1SlidingHit = false;

        if (LocalCache is IFactoryCacheStore l1Store)
        {
            var l1Entry = await l1Store.TryGetEntryAsync<T>(key, cancellationToken).ConfigureAwait(false);

            if (l1Entry.IsFresh(now))
            {
                _logger.LogLocalCacheHit(key);
                Interlocked.Increment(ref _localCacheHits);

                if (!l1Entry.SlidingExpiration.HasValue)
                {
                    return l1Entry;
                }

                // Sliding entries need the L2 physical cap for safe re-arm. A local entry may be physically
                // capped by DefaultLocalExpiration, so use it only as a no-rearm fallback if L2 is unavailable.
                l1SlidingHit = true;
                l1StaleCandidate = l1Entry with { SlidingExpiration = null };
            }
            else if (l1Entry.IsPhysicallyPresent(now))
            {
                l1StaleCandidate = l1Entry;
            }
        }
        else
        {
            var l1Value = await LocalCache.GetAsync<T>(key, cancellationToken).ConfigureAwait(false);

            if (l1Value.HasValue)
            {
                _logger.LogLocalCacheHit(key);
                Interlocked.Increment(ref _localCacheHits);
                return new CacheStoreEntry<T>(
                    Found: true,
                    IsNull: l1Value.IsNull,
                    Value: l1Value.Value,
                    LogicalExpiresAt: null,
                    PhysicalExpiresAt: null,
                    SlidingExpiration: null
                );
            }
        }

        if (!l1SlidingHit)
        {
            _logger.LogLocalCacheMiss(key);
        }

        if (l2Cache is not IFactoryCacheStore l2Store)
        {
            return l1StaleCandidate ?? CacheStoreEntry<T>.NotFound;
        }

        var timeoutKind = l1StaleCandidate is not null
            ? DistributedCacheTimeoutKind.Soft
            : DistributedCacheTimeoutKind.Hard;
        var l2Read = await _ReadFromL2Async(
                key,
                ct => l2Store.TryGetEntryAsync<T>(key, ct),
                _SelectDistributedReadTimeout(
                    hasLocalFallback: l1StaleCandidate is not null,
                    softCanDegradeToMiss: false
                ),
                timeoutKind,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (!l2Read.IsSuccess)
        {
            if (l2Read.Exception is { } exception)
            {
                _logger.LogFailedToReadFromL2Cache(exception, key);
            }

            if (
                l1StaleCandidate is { } fallback
                && l2Read.Status is DistributedCacheReadStatus.TimedOut or DistributedCacheReadStatus.CircuitOpen
            )
            {
                return fallback with { ServeStaleImmediately = true };
            }

            return l1StaleCandidate ?? CacheStoreEntry<T>.NotFound;
        }

        var l2Entry = l2Read.Value;

        // Only promote a logically-fresh L2 entry into L1. Promoting a stale (logically-expired) reserve on
        // every fail-safe read amplifies L1 writes under stampede and can overwrite a newer L1 stale reserve.
        // The coordinator still receives the returned l2Entry as its stale candidate, so fail-safe serving of
        // an L2 reserve is unaffected — it simply is not re-cached into L1.
        if (l2Entry.IsFresh(now) && LocalCache is IFactoryCacheStore l1StoreForPromotion)
        {
            await _SetLocalEntryAsync(l1StoreForPromotion, key, l2Entry, cancellationToken).ConfigureAwait(false);
        }

        return l2Entry.Found ? l2Entry : l1StaleCandidate ?? CacheStoreEntry<T>.NotFound;
    }

    // Non-async forwarder: `in` parameters are not allowed on async methods, so copy the descriptor by value.
    ValueTask<bool> IFactoryCacheStore.SetEntryAsync<T>(
        string key,
        in CacheStoreEntryWrite<T> entry,
        CancellationToken cancellationToken
    )
        where T : default
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        return _SetEntryCoreAsync(key, entry, cancellationToken);
    }

    private async ValueTask<bool> _SetEntryCoreAsync<T>(
        string key,
        CacheStoreEntryWrite<T> entry,
        CancellationToken cancellationToken
    )
    {
        // Background path: the GetOrAdd factory write-through is the highest-value detach target. The factory
        // already returned its value to the caller against L1, so the L2 mirror + peer publish add nothing to
        // the caller's result. Write L1 synchronously (capturing the physical stamp the recovery replay verifies
        // against), then detach the L2 write + recovery bookkeeping + publish under CancellationToken.None so a
        // caller token going away cannot abandon the L2 mirror. The descriptor is already captured by value
        // (the SetEntryAsync forwarder copies the `in` parameter), so the lambda owns immutable state.
        if (options.AllowBackgroundDistributedCacheOperations)
        {
            var localWrite = await _WriteSetEntryToLocalAsync(key, entry, cancellationToken).ConfigureAwait(false);

            if (!localWrite.Committed)
            {
                return false;
            }

            _RunDetached(() => _SetEntryL2TailAsync(key, entry, localWrite.PhysicalStamp), key);

            return true;
        }

        // Per-call tier control: skip the L2 (distributed) write entirely. No recovery replay is queued (the skip
        // is intentional, not a failure), and the peer-invalidation publish below is skipped together with it —
        // a value that never reached L2 has no shared copy for peers to invalidate against.
        var (skipL2, l2WriteSucceeded, l2WriteConditionFailed) = await _WriteL2EntryAsync(entry, key, cancellationToken)
            .ConfigureAwait(false);

        if (!skipL2 && l2WriteConditionFailed)
        {
            return false;
        }

        var localWriteSync = await _WriteSetEntryToLocalAsync(key, entry, cancellationToken).ConfigureAwait(false);

        if (!localWriteSync.Committed)
        {
            return false;
        }

        await _ApplyL2WriteRecoveryAndPublishAsync(
                key,
                entry,
                skipL2,
                l2WriteSucceeded,
                localWriteSync.PhysicalStamp,
                cancellationToken
            )
            .ConfigureAwait(false);

        return true;
    }

    /// <summary>
    /// Writes the framed entry into L1 (bounded by the local ceiling), returning the physical stamp actually
    /// written so the auto-recovery replay can detect that the L1 entry was later replaced. Shared by the
    /// synchronous and background <see cref="_SetEntryCoreAsync{T}"/> flows so both write L1 identically.
    /// </summary>
    private async ValueTask<(bool Committed, DateTime? PhysicalStamp)> _WriteSetEntryToLocalAsync<T>(
        string key,
        CacheStoreEntryWrite<T> entry,
        CancellationToken cancellationToken
    )
    {
        // Per-call tier control: skip the L1 (memory) write entirely. The L2 mirror and the peer invalidation
        // publish still run, so peers drop their stale L1 copy even though this node wrote nothing to L1.
        if (entry.SkipMemoryCacheWrite)
        {
            return (true, null);
        }

        if (LocalCache is IFactoryCacheStore l1Store)
        {
            var l1Entry = new CacheStoreEntry<T>(
                Found: true,
                IsNull: entry.IsNull,
                Value: entry.IsNull ? default : entry.Value,
                LogicalExpiresAt: entry.LogicalExpiresAt,
                PhysicalExpiresAt: entry.PhysicalExpiresAt,
                SlidingExpiration: entry.SlidingExpiration
            )
            {
                EagerRefreshAt = entry.EagerRefreshAt,
                ETag = entry.ETag,
                LastModifiedAt = entry.LastModifiedAt,
                CreatedAt = entry.CreatedAt,
                Tags = entry.Tags,
            };

            var physicalStamp = await _SetLocalEntryAsync(l1Store, key, l1Entry, cancellationToken)
                .ConfigureAwait(false);

            return (true, physicalStamp);
        }

        await LocalCache
            .UpsertAsync(
                key,
                entry.IsNull ? default : entry.Value,
                _GetLocalExpiration(entry.PhysicalExpiresAt.Subtract(_GetUtcNow())),
                cancellationToken
            )
            .ConfigureAwait(false);

        return (true, null);
    }

    /// <summary>
    /// The detached L2 tail of <see cref="_SetEntryCoreAsync{T}"/> when background distributed operations are
    /// enabled: writes the framed entry to L2, runs the same recovery bookkeeping the synchronous path does
    /// (route a failed write to replay when auto-recovery is on; otherwise the failure was already logged and is
    /// swallowed), then publishes the peer invalidation. Runs under <see cref="CancellationToken.None"/> because
    /// the caller's token is gone. <paramref name="l1PhysicalStamp"/> was captured from the synchronous L1 write.
    /// </summary>
    private async Task _SetEntryL2TailAsync<T>(string key, CacheStoreEntryWrite<T> entry, DateTime? l1PhysicalStamp)
    {
        // Per-call tier control: skip the L2 write (and its recovery bookkeeping) when requested; the publish
        // below is kept as-is so peers still drop their stale L1.
        // Runs under CancellationToken.None: the caller's token is gone (detached background path).
        var (skipL2, l2WriteSucceeded, l2WriteConditionFailed) = await _WriteL2EntryAsync(
                entry,
                key,
                CancellationToken.None
            )
            .ConfigureAwait(false);

        if (!skipL2 && l2WriteConditionFailed)
        {
            // The conditional L2 write lost to a concurrent writer, but L1 on this node was already committed
            // with our value (synchronous path, before this detached tail ran). Publish so peers drop their
            // now-stale L1 — skip restamps, which do not change the value. The winning writer publishing too is
            // harmless (idempotent invalidation); a stale peer until TTL is not.
            if (!entry.IsRestamp)
            {
                await _PublishInvalidationAsync(
                        new CacheInvalidationMessage { InstanceId = _instanceId, Key = key },
                        CancellationToken.None,
                        queueOnFailure: true
                    )
                    .ConfigureAwait(false);
            }

            return;
        }

        await _ApplyL2WriteRecoveryAndPublishAsync(
                key,
                entry,
                skipL2,
                l2WriteSucceeded,
                l1PhysicalStamp,
                CancellationToken.None
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Shared tail of the synchronous (<see cref="_SetEntryCoreAsync{T}"/>) and detached-background
    /// (<see cref="_SetEntryL2TailAsync{T}"/>) write paths once the L2 write outcome is known and L1 is committed:
    /// routes a failed L2 write to the recovery queue (or records success), then publishes the peer invalidation.
    /// The two callers differ only in the physical stamp source (their own L1 write) and the token (the caller's
    /// token vs <see cref="CancellationToken.None"/> on the detached path); the divergent condition-failed handling
    /// stays in each caller. The publish runs <em>after</em> the recovery bookkeeping so a queued publish-recovery
    /// item cannot be cleared by this write's own L2 success.
    /// </summary>
    /// <remarks>
    /// Factory value-writes (cold-miss fresh write, soft-timeout background completion, eager refresh, conditional
    /// Modified) invalidate peers' L1 exactly like the explicit-upsert path. Metadata-only restamps (NotModified
    /// extension, fail-safe throttle, eager-refresh gate) are skipped: peers' cached bytes are identical, so
    /// invalidating them would only force pointless L2 re-reads. The publish does not depend on
    /// <paramref name="skipL2"/>: a fresh value means peers' copies are stale, so they must drop their L1 even when
    /// this node wrote only one tier.
    /// </remarks>
    private async ValueTask _ApplyL2WriteRecoveryAndPublishAsync<T>(
        string key,
        CacheStoreEntryWrite<T> entry,
        bool skipL2,
        bool l2WriteSucceeded,
        DateTime? l1PhysicalStamp,
        CancellationToken cancellationToken
    )
    {
        if (RecoveryQueue is not null && !skipL2)
        {
            if (l2WriteSucceeded)
            {
                RecoveryQueue.OnSuccessfulL2Operation(key);
            }
            else
            {
                // Degraded mode: the caller already succeeded against L1; queue the L2 write for replay.
                _QueueSetEntryRecovery(key, entry, l1PhysicalStamp);
            }
        }

        if (!entry.IsRestamp)
        {
            await _PublishInvalidationAsync(
                    new CacheInvalidationMessage { InstanceId = _instanceId, Key = key },
                    cancellationToken,
                    queueOnFailure: true
                )
                .ConfigureAwait(false);
        }
    }

    async ValueTask IFactoryCacheStore.TryRearmSlidingAsync(
        string key,
        TimeSpan slidingExpiration,
        DateTime physicalExpiresAt,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        // Re-arm both tiers (KTD-8). L2 carries the authoritative physical cap; L1's own re-arm bounds the new
        // logical deadline by its locally-capped entry metadata, so passing the L2 cap is safe. L2 is best-effort
        // (a remote hiccup must not fail the read); L1 is in-process and effectively infallible.
        if (l2Cache is IFactoryCacheStore l2Store && _IsDistributedCacheCircuitClosed())
        {
            try
            {
                await l2Store
                    .TryRearmSlidingAsync(key, slidingExpiration, physicalExpiresAt, now, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
                when (!FactoryCacheCoordinator.IsCallerCancellation(exception, cancellationToken))
            {
                _OpenDistributedCacheCircuit(exception, key);
                _logger.LogFailedToWriteToL2Cache(exception, key);
            }
        }

        if (LocalCache is IFactoryCacheStore l1Store)
        {
            await l1Store
                .TryRearmSlidingAsync(key, slidingExpiration, physicalExpiresAt, now, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Dispatches the L2 write for a framed entry: evaluates the circuit, routes to
    /// <see cref="IFactoryCacheStore"/> or <see cref="IRemoteCache"/>, trips the circuit on
    /// unhandled exceptions, and returns whether the write was skipped, succeeded, or failed
    /// with a condition mismatch.
    /// </summary>
    /// <remarks>
    /// Caller-specific concerns (recovery-queue routing, peer-invalidation publish, L1 write) are
    /// NOT handled here — each caller keeps those locally because they differ between
    /// <see cref="_SetEntryCoreAsync{T}"/> and <see cref="_SetEntryL2TailAsync{T}"/>.
    /// <para>
    /// The <paramref name="ct"/> must be <see cref="CancellationToken.None"/> when called from the
    /// detached background tail (<see cref="_SetEntryL2TailAsync{T}"/>); pass the caller's real token
    /// from the synchronous path.
    /// </para>
    /// </remarks>
    private async ValueTask<(bool SkipL2, bool Succeeded, bool ConditionFailed)> _WriteL2EntryAsync<T>(
        CacheStoreEntryWrite<T> entry,
        string key,
        CancellationToken ct
    )
    {
        var skipL2 = entry.SkipDistributedCacheWrite || !_IsDistributedCacheCircuitClosed();

        if (skipL2)
        {
            return (SkipL2: true, Succeeded: false, ConditionFailed: false);
        }

        // ExpectedConcurrencyStamp is store-local; an L1 stamp must not be applied to the L2 mirror.
        var l2Entry = entry with
        {
            ExpectedConcurrencyStamp = null,
        };

        if (l2Cache is IFactoryCacheStore l2Store)
        {
            try
            {
                var succeeded = await l2Store.SetEntryAsync(key, in l2Entry, ct).ConfigureAwait(false);
                return (SkipL2: false, Succeeded: succeeded, ConditionFailed: !succeeded);
            }
            catch (Exception exception) when (!FactoryCacheCoordinator.IsCallerCancellation(exception, ct))
            {
                _OpenDistributedCacheCircuit(exception, key);
                _logger.LogFailedToWriteToL2Cache(exception, key);

                if (options.ReThrowDistributedCacheExceptions)
                {
                    throw;
                }

                return (SkipL2: false, Succeeded: false, ConditionFailed: false);
            }
        }
        else
        {
            var expiresIn = (
                entry.SlidingExpiration is null ? entry.PhysicalExpiresAt : entry.LogicalExpiresAt
            ).Subtract(_GetUtcNow());

            try
            {
                await l2Cache
                    .UpsertAsync(key, entry.IsNull ? default : entry.Value, expiresIn, ct)
                    .ConfigureAwait(false);
                return (SkipL2: false, Succeeded: true, ConditionFailed: false);
            }
            catch (Exception exception) when (!FactoryCacheCoordinator.IsCallerCancellation(exception, ct))
            {
                _OpenDistributedCacheCircuit(exception, key);
                _logger.LogFailedToWriteToL2Cache(exception, key);

                if (options.ReThrowDistributedCacheExceptions)
                {
                    throw;
                }

                return (SkipL2: false, Succeeded: false, ConditionFailed: false);
            }
        }
    }

    /// <summary>
    /// Writes an entry into L1 bounded by the local ceiling. Returns the physical expiration stamp actually
    /// written (auto-recovery uses it to detect that the L1 entry was replaced), or <see langword="null"/>
    /// when the write was skipped.
    /// </summary>
    private async ValueTask<DateTime?> _SetLocalEntryAsync<T>(
        IFactoryCacheStore l1Store,
        string key,
        CacheStoreEntry<T> entry,
        CancellationToken cancellationToken
    )
    {
        if (!entry.Found)
        {
            return null;
        }

        var now = _GetUtcNow();
        var localCeiling = options.DefaultLocalExpiration.HasValue
            ? now.Add(options.DefaultLocalExpiration.Value)
            : (DateTime?)null;

        // Legacy/unframed L2 entries carry no expiration metadata. Promote them into L1 bounded by the local
        // ceiling so they cannot pin process memory indefinitely; without a configured ceiling there is no finite
        // bound to apply, so skip rather than cache a never-expiring entry locally.
        if (!entry.LogicalExpiresAt.HasValue || !entry.PhysicalExpiresAt.HasValue)
        {
            if (!localCeiling.HasValue)
            {
                return null;
            }

            var ceilingWrite = new CacheStoreEntryWrite<T>
            {
                Value = entry.Value,
                IsNull = entry.IsNull,
                LogicalExpiresAt = localCeiling.Value,
                PhysicalExpiresAt = localCeiling.Value,
                SlidingExpiration = null,
                EagerRefreshAt = entry.EagerRefreshAt,
                ETag = entry.ETag,
                LastModifiedAt = entry.LastModifiedAt,
                // Carry the birth time into L1 so Family-2 tag/clear markers version-pin the local copy correctly
                // (a null CreatedAt would bias the promoted entry to invalidated under any marker).
                CreatedAt = entry.CreatedAt,
                Tags = entry.Tags,
            };

            await l1Store.SetEntryAsync(key, in ceilingWrite, cancellationToken).ConfigureAwait(false);

            return ceilingWrite.PhysicalExpiresAt;
        }

        var logicalExpiresAt = localCeiling.HasValue
            ? _Min(entry.LogicalExpiresAt.Value, localCeiling.Value)
            : entry.LogicalExpiresAt.Value;
        var physicalExpiresAt = localCeiling.HasValue
            ? _Min(entry.PhysicalExpiresAt.Value, localCeiling.Value)
            : entry.PhysicalExpiresAt.Value;

        var localWrite = new CacheStoreEntryWrite<T>
        {
            Value = entry.Value,
            IsNull = entry.IsNull,
            LogicalExpiresAt = logicalExpiresAt,
            PhysicalExpiresAt = physicalExpiresAt,
            SlidingExpiration = entry.SlidingExpiration,
            EagerRefreshAt = entry.EagerRefreshAt,
            ETag = entry.ETag,
            LastModifiedAt = entry.LastModifiedAt,
            // Carry the birth time into L1 so Family-2 tag/clear markers version-pin the local copy correctly
            // (a null CreatedAt would bias the promoted entry to invalidated under any marker).
            CreatedAt = entry.CreatedAt,
            Tags = entry.Tags,
        };

        await l1Store.SetEntryAsync(key, in localWrite, cancellationToken).ConfigureAwait(false);

        return localWrite.PhysicalExpiresAt;
    }

    /// <summary>
    /// Zero-intermediate-copy buffer read. Mirrors <see cref="GetAsync{T}"/>'s tier composition exactly: an L1 hit
    /// returns straight from L1 (zero-intermediate-copy when L1 implements <see cref="IBufferCache"/> — the stored
    /// bytes flow into <paramref name="destination"/> without a typed deserialize), and an L1 miss falls through to
    /// the same <see cref="_ReadFromL2Async{T}"/> wrapper (circuit breaker, soft timeout, degrade-to-miss) before
    /// seeding L1. Expiry, sliding re-arm, and Family-2 tag/clear invalidation are applied identically because the
    /// per-tier reads are the same primitives the generic path uses.
    /// </summary>
    /// <remarks>
    /// The cold L1-miss -> L2 path materializes the L2 payload as a <c>byte[]</c> so it can both seed L1 — which
    /// retains an owned array — and write to <paramref name="destination"/>. Two copies on this cold path are
    /// inherent to populating two tiers; the L1-hit hot path stays single-copy. When both tiers implement
    /// <see cref="IFactoryCacheStore"/> the read goes through the framed <c>TryGetEntryAsync&lt;byte[]&gt;</c>
    /// primitive — the same one the generic cold path uses — so the one round-trip yields the bytes plus expiration
    /// AND the Tags + CreatedAt the L1 seed needs to stay version-pinned for Family-2 tag/clear invalidation;
    /// otherwise it falls back to the typed <c>GetWithExpirationAsync&lt;byte[]&gt;</c> read (bytes + expiration
    /// only, the most a non-framed L2 exposes). Routing through L2's own
    /// <see cref="IBufferCache.TryGetToAsync"/> is deliberately avoided: it would write the buffer but leave nothing
    /// to seed L1 with.
    /// </remarks>
    public async ValueTask<bool> TryGetToAsync(
        string key,
        IBufferWriter<byte> destination,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        Argument.IsNotNull(destination);
        cancellationToken.ThrowIfCancellationRequested();

        // L1 hit (hot path): when L1 supports the buffer contract, its TryGetToAsync applies the same expiry,
        // sliding re-arm, and tag/clear-invalidation checks the generic L1 read does, and writes the stored bytes
        // straight into destination — fully zero-intermediate-copy.
        if (LocalCache is IBufferCache l1Buffer)
        {
            if (await l1Buffer.TryGetToAsync(key, destination, cancellationToken).ConfigureAwait(false))
            {
                _logger.LogLocalCacheHit(key);
                Interlocked.Increment(ref _localCacheHits);
                return true;
            }
        }
        else
        {
            // L1 lacks the buffer contract: mirror the generic L1 read and write the materialized bytes out.
            var l1Value = await LocalCache.GetAsync<byte[]>(key, cancellationToken).ConfigureAwait(false);

            if (l1Value is { HasValue: true, Value: { } l1Bytes })
            {
                _logger.LogLocalCacheHit(key);
                Interlocked.Increment(ref _localCacheHits);
                destination.Write(l1Bytes);
                return true;
            }
        }

        _logger.LogLocalCacheMiss(key);

        // L1 miss -> L2. When both tiers speak the framed-entry contract, route the cold read through the same
        // entry-returning primitive the generic cold path (IFactoryCacheStore.TryGetEntryAsync) uses: it yields the
        // bytes plus expiration AND the Tags + CreatedAt the L1 seed needs in ONE round-trip, so the seeded L1 entry
        // is version-pinned for Family-2 tag/clear invalidation. The legacy GetWithExpirationAsync<byte[]> read
        // returns no tag metadata, so it would seed a tagless L1 entry that EvictByTagAsync can never invalidate.
        if (l2Cache is IFactoryCacheStore l2Store && LocalCache is IFactoryCacheStore l1StoreForSeed)
        {
            return await _BufferColdReadFromFramedL2Async(key, destination, l2Store, l1StoreForSeed, cancellationToken)
                .ConfigureAwait(false);
        }

        // Fallback: L2 (or L1) lacks the framed-entry contract, so no tag-carrying single-read primitive is
        // available. The typed byte[] read yields the bytes plus expiration in one round-trip; seed L1 tagless
        // (the only metadata L2 exposes here) exactly as GetAsync<T> does, then write the same bytes out.
        var l2Read = await _ReadFromL2Async(
                key,
                ct => l2Cache.GetWithExpirationAsync<byte[]>(key, ct),
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

        var cacheValue = l2Read.Value.Value;

        // Miss everywhere / null-sentinel / tag-invalidated (all surfaced as no value by the L2 read): false,
        // nothing written — parity with the byte[] fallback.
        if (!cacheValue.HasValue || cacheValue.Value is not { } l2Bytes)
        {
            return false;
        }

        // Seed L1 (bounded by the local ceiling) exactly as GetAsync<T> does, then write the same bytes out.
        var localExpiration = _GetLocalExpiration(l2Read.Value.Expiration);
        _logger.LogSettingLocalCacheKey(key, localExpiration);
        await LocalCache.UpsertAsync(key, l2Bytes, localExpiration, cancellationToken).ConfigureAwait(false);

        destination.Write(l2Bytes);
        return true;
    }

    /// <summary>
    /// Cold buffer-read tail when both tiers implement <see cref="IFactoryCacheStore"/>: reads the framed L2 entry
    /// (value + expiration + Tags + CreatedAt) in one round-trip, seeds L1 via <see cref="_SetLocalEntryAsync{T}"/>
    /// so the local copy carries the tag metadata Family-2 invalidation version-pins against (mirroring the generic
    /// <see cref="IFactoryCacheStore.TryGetEntryAsync{T}"/> cold path), then writes the bytes into the caller's
    /// destination. Seeds L1 only when the L2 entry is logically fresh, matching the generic path's promotion gate.
    /// </summary>
    private async ValueTask<bool> _BufferColdReadFromFramedL2Async(
        string key,
        IBufferWriter<byte> destination,
        IFactoryCacheStore l2Store,
        IFactoryCacheStore l1Store,
        CancellationToken cancellationToken
    )
    {
        var l2Read = await _ReadFromL2Async(
                key,
                ct => l2Store.TryGetEntryAsync<byte[]>(key, ct),
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

        var l2Entry = l2Read.Value;

        // A direct buffer read serves only a logically-fresh entry. The framed read returns a tag/clear-invalidated
        // (or otherwise logically-expired) reserve as Found-but-stale for the factory coordinator; for a direct read
        // that is a miss, matching the L1 buffer hot path (InMemoryCache.TryGetToAsync) and the GetWithExpirationAsync
        // fallback, which both surface a tag-invalidated entry as no value. Null-sentinel also reads as a miss.
        if (!l2Entry.IsFresh(_GetUtcNow()) || l2Entry.IsNull || l2Entry.Value is not { } l2Bytes)
        {
            return false;
        }

        // Promote the fresh L2 entry into L1, exactly like the generic IFactoryCacheStore.TryGetEntryAsync cold path:
        // _SetLocalEntryAsync preserves Tags + CreatedAt and applies the local-expiration ceiling, so the seeded L1
        // entry stays version-pinned for Family-2 tag/clear invalidation.
        await _SetLocalEntryAsync(l1Store, key, l2Entry, cancellationToken).ConfigureAwait(false);

        destination.Write(l2Bytes);
        return true;
    }

    /// <summary>
    /// Zero-intermediate-copy buffer write. Materializes the sequence into a stable owned <c>byte[]</c>
    /// synchronously before any await (the cache retains it, so a caller-pooled buffer must not be aliased),
    /// validates + stamps via <see cref="CacheEntryStamps"/> exactly as
    /// <c>FactoryCacheStoreExtensions.UpsertEntryAsync</c>, then routes the framed entry through the same
    /// <see cref="_SetEntryCoreAsync{T}"/> the generic upsert uses — so L2 mirror, L1 write (local ceiling),
    /// auto-recovery booking, circuit breaking, and the peer-invalidation publish (with InstanceId self-filtering)
    /// are all identical to <see cref="UpsertEntryAsync{T}"/> for a <c>byte[]</c> value.
    /// </summary>
    public async ValueTask UpsertRawAsync(
        string key,
        ReadOnlySequence<byte> value,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        // Materialize a stable owned copy synchronously, before any stamping/await: the cache retains this array,
        // so it must not alias a caller-pooled buffer that is recycled after the call returns.
        var bytes = value.ToArray();

        // Validate then stamp, matching the UpsertEntryAsync extension exactly: Compute does the stamp math but
        // does NOT validate, so ValidateOptions (which also validates Tags) runs first at this single choke point.
        CacheEntryStamps.ValidateOptions(options);

        var now = _GetUtcNow();
        var stamps = CacheEntryStamps.Compute(options, now);

        var entry = new CacheStoreEntryWrite<byte[]>
        {
            Value = bytes,
            IsNull = false,
            LogicalExpiresAt = stamps.LogicalExpiresAt,
            PhysicalExpiresAt = stamps.PhysicalExpiresAt,
            SlidingExpiration = options.SlidingExpiration,
            EagerRefreshAt = stamps.EagerRefreshAt,
            // Stamp the birth time so a prior tag/clear marker does not logically invalidate this fresh write
            // (Family-2 version-pinning compares CreatedAt against the newest applicable marker).
            CreatedAt = stamps.CreatedAt,
            Tags = options.Tags,
            SkipMemoryCacheWrite = options.SkipMemoryCacheWrite,
            SkipDistributedCacheWrite = options.SkipDistributedCacheWrite,
        };

        await _SetEntryCoreAsync(key, entry, cancellationToken).ConfigureAwait(false);
    }
}
