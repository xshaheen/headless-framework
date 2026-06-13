// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Logging;

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

        var l2Entry = l2Read.Value!;

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

        if (RecoveryQueue is not null && !skipL2)
        {
            if (l2WriteSucceeded)
            {
                RecoveryQueue.OnSuccessfulL2Operation(key);
            }
            else
            {
                // Degraded mode: the caller already succeeded against L1; queue the L2 write for replay.
                _QueueSetEntryRecovery(key, entry, localWriteSync.PhysicalStamp);
            }
        }

        // Factory value-writes (cold-miss fresh write, soft-timeout background completion, eager refresh,
        // conditional Modified) invalidate peers' L1 exactly like the explicit-upsert path. Metadata-only
        // restamps (NotModified extension, fail-safe throttle, eager-refresh gate) are skipped: peers' cached
        // bytes are still identical, so invalidating them would only force pointless L2 re-reads. The publish is
        // kept as-is for the tier-skip flags (it does not depend on skipL2): a fresh value here means peers'
        // cached copies are stale, so they must still drop their L1 even when this node wrote only one tier. The
        // publish runs after the recovery bookkeeping so a queued publish-recovery item cannot be cleared by this
        // write's own L2 success.
        if (!entry.IsRestamp)
        {
            await _PublishInvalidationAsync(
                    new CacheInvalidationMessage { InstanceId = _instanceId, Key = key },
                    cancellationToken,
                    queueOnFailure: true
                )
                .ConfigureAwait(false);
        }

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
            return;
        }

        if (RecoveryQueue is not null && !skipL2)
        {
            if (l2WriteSucceeded)
            {
                RecoveryQueue.OnSuccessfulL2Operation(key);
            }
            else
            {
                _QueueSetEntryRecovery(key, entry, l1PhysicalStamp);
            }
        }

        if (!entry.IsRestamp)
        {
            await _PublishInvalidationAsync(
                    new CacheInvalidationMessage { InstanceId = _instanceId, Key = key },
                    CancellationToken.None,
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
            Tags = entry.Tags,
        };

        await l1Store.SetEntryAsync(key, in localWrite, cancellationToken).ConfigureAwait(false);

        return localWrite.PhysicalExpiresAt;
    }
}
