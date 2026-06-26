// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

public sealed partial class HybridCache
{
    /// <summary>
    /// Reads back the L1 physical-expiry stamp for a key just written by a scalar upsert, so a queued recovery
    /// item can verify (at replay time) that the L1 entry it captured is still the one in the store. Returns
    /// <see langword="null"/> when L1 does not expose the entry store (the replay then falls back to a
    /// presence-only check) — symmetric with how <see cref="_QueueSetEntryRecovery{T}"/> consumes its stamp.
    /// </summary>
    private async ValueTask<DateTime?> _CaptureLocalPhysicalStampAsync<T>(string key, CancellationToken ct)
    {
        if (LocalCache is not IFactoryCacheStore l1Store)
        {
            return null;
        }

        var current = await l1Store.TryGetEntryAsync<T>(key, ct).ConfigureAwait(false);
        return current.Found ? current.PhysicalExpiresAt : null;
    }

    /// <summary>
    /// Builds the stamp-aware L1-staleness guard shared by <see cref="_QueueScalarUpsertRecovery{T}"/> and
    /// <see cref="_QueueSetEntryRecovery{T}"/>: a queued write may replay only while L1 still holds an entry
    /// carrying the exact physical stamp the write produced — a different stamp means a newer local write landed
    /// and the queued write is obsolete. When L1 does not expose <see cref="IFactoryCacheStore"/> (or no stamp was
    /// captured) it falls back to a presence-only check. Returns <see langword="true"/> when the write should still
    /// replay.
    /// </summary>
    private Func<CancellationToken, ValueTask<bool>> _BuildL1StampGuard<T>(string key, DateTime? l1PhysicalStamp)
    {
        return async ct =>
        {
            if (LocalCache is IFactoryCacheStore l1Store && l1PhysicalStamp.HasValue)
            {
                var current = await l1Store.TryGetEntryAsync<T>(key, ct).ConfigureAwait(false);

                return current.Found && current.PhysicalExpiresAt == l1PhysicalStamp;
            }

            var fallback = await LocalCache.GetAsync<T>(key, ct).ConfigureAwait(false);

            return fallback.HasValue;
        };
    }

    /// <summary>
    /// Builds the presence-only L1-staleness guard shared by <see cref="_QueueRemoveRecovery"/> and
    /// <see cref="_QueueExpireRecovery"/>: a queued remove/expire becomes obsolete once L1 holds an entry newer than
    /// <paramref name="baseline"/> (a write landed during the outage), because replaying it would clobber that newer
    /// write. Removes/expires produce no physical stamp to compare, so the guard is presence-only; when L1 does not
    /// expose <see cref="IFactoryCacheStore"/> it falls back to a plain presence check. Returns
    /// <see langword="true"/> when the queued operation should still replay.
    /// </summary>
    private Func<CancellationToken, ValueTask<bool>> _BuildL1PresenceGuard(string key, DateTimeOffset baseline)
    {
        return async ct =>
        {
            if (LocalCache is IFactoryCacheStore l1Store)
            {
                var current = await l1Store.TryGetEntryAsync<object>(key, ct).ConfigureAwait(false);

                return !(current.Found && current.PhysicalExpiresAt > baseline);
            }

            var fallback = await LocalCache.GetAsync<object>(key, ct).ConfigureAwait(false);

            return !fallback.HasValue;
        };
    }

    private void _QueueScalarUpsertRecovery<T>(string key, T? value, TimeSpan? expiration, DateTime? l1PhysicalStamp)
    {
        var queue = RecoveryQueue!;
        var now = _timeProvider.GetUtcNow();

        // Values without a TTL have no natural item expiry; bound them by the generous fixed window.
        var deadline = expiration.HasValue ? now + expiration.Value : now + queue.DefaultRetention;
        var l1Guard = _BuildL1StampGuard<T>(key, l1PhysicalStamp);

        queue.Enqueue(
            key,
            HybridCacheRecoveryKind.SetEntry,
            deadline,
            async ct =>
            {
                // L1 is the source of truth: only replay while it still holds the exact physical stamp this write
                // produced (a different stamp means a newer local write landed and the queued write is obsolete).
                if (!await l1Guard(ct).ConfigureAwait(false))
                {
                    return HybridCacheRecoveryReplayOutcome.Obsolete;
                }

                TimeSpan? remaining = expiration.HasValue ? deadline - _timeProvider.GetUtcNow() : null;

                if (remaining is { Ticks: <= 0 })
                {
                    return HybridCacheRecoveryReplayOutcome.Obsolete;
                }

                await l2Cache.UpsertAsync(key, value, remaining, ct).ConfigureAwait(false);
                await _PublishReplayedInvalidationAsync(key, now, ct).ConfigureAwait(false);
                return HybridCacheRecoveryReplayOutcome.Replayed;
            }
        );
    }

    private void _QueueSetEntryRecovery<T>(string key, CacheStoreEntryWrite<T> entry, DateTime? l1PhysicalStamp)
    {
        var queue = RecoveryQueue!;
        var writeTimestamp = _timeProvider.GetUtcNow();
        var l1Guard = _BuildL1StampGuard<T>(key, l1PhysicalStamp);

        queue.Enqueue(
            key,
            HybridCacheRecoveryKind.SetEntry,
            new DateTimeOffset(DateTime.SpecifyKind(entry.PhysicalExpiresAt, DateTimeKind.Utc)),
            async ct =>
            {
                // L1 is the source of truth: only replay while it still holds the exact physical stamp this write
                // produced (a different stamp means the entry changed and the queued write is obsolete).
                if (!await l1Guard(ct).ConfigureAwait(false))
                {
                    return HybridCacheRecoveryReplayOutcome.Obsolete;
                }

                if (l2Cache is IFactoryCacheStore l2Store)
                {
                    // The descriptor carries absolute UTC stamps, so it replays unchanged.
                    await l2Store.SetEntryAsync(key, entry, ct).ConfigureAwait(false);
                }
                else
                {
                    var expiresIn = (
                        entry.SlidingExpiration is null ? entry.PhysicalExpiresAt : entry.LogicalExpiresAt
                    ).Subtract(_GetUtcNow());

                    if (expiresIn <= TimeSpan.Zero)
                    {
                        return HybridCacheRecoveryReplayOutcome.Obsolete;
                    }

                    await l2Cache
                        .UpsertAsync(key, entry.IsNull ? default : entry.Value, expiresIn, ct)
                        .ConfigureAwait(false);
                }

                // Mirror the live SetEntry path, restamp gating included: peers' cached bytes are identical
                // for a metadata-only restamp, so only value-bearing replays broadcast.
                if (!entry.IsRestamp)
                {
                    await _PublishReplayedInvalidationAsync(key, writeTimestamp, ct).ConfigureAwait(false);
                }

                return HybridCacheRecoveryReplayOutcome.Replayed;
            }
        );
    }

    private void _QueueRemoveRecovery(string key)
    {
        var queue = RecoveryQueue!;
        var removeTimestamp = _timeProvider.GetUtcNow();
        var l1Guard = _BuildL1PresenceGuard(key, removeTimestamp);

        queue.Enqueue(
            key,
            HybridCacheRecoveryKind.Remove,
            removeTimestamp + queue.DefaultRetention,
            async ct =>
            {
                // Guard against replaying a remove that would delete a value written during the outage: if L1 now
                // holds an entry newer than this remove, a newer write landed and replaying would clobber it.
                if (!await l1Guard(ct).ConfigureAwait(false))
                {
                    return HybridCacheRecoveryReplayOutcome.Obsolete;
                }

                await l2Cache.RemoveAsync(key, ct).ConfigureAwait(false);
                await _PublishReplayedInvalidationAsync(key, removeTimestamp, ct).ConfigureAwait(false);
                return HybridCacheRecoveryReplayOutcome.Replayed;
            }
        );
    }

    private void _QueueExpireRecovery(string key)
    {
        var queue = RecoveryQueue!;
        var expireTimestamp = _timeProvider.GetUtcNow();
        var l1Guard = _BuildL1PresenceGuard(key, expireTimestamp);

        queue.Enqueue(
            key,
            HybridCacheRecoveryKind.Expire,
            expireTimestamp + queue.DefaultRetention,
            async ct =>
            {
                // Guard: if L1 now holds an entry newer than this expire, a write landed during the outage and
                // replaying the expire would evict it on L2 — preserve the newer write.
                if (!await l1Guard(ct).ConfigureAwait(false))
                {
                    return HybridCacheRecoveryReplayOutcome.Obsolete;
                }

                await l2Cache.ExpireAsync(key, ct).ConfigureAwait(false);
                await _PublishReplayedInvalidationAsync(key, expireTimestamp, ct, expire: true).ConfigureAwait(false);
                return HybridCacheRecoveryReplayOutcome.Replayed;
            }
        );
    }

    private void _QueuePublishRecovery(CacheInvalidationMessage message)
    {
        var queue = RecoveryQueue!;

        queue.Enqueue(
            message.Key!,
            HybridCacheRecoveryKind.PublishInvalidation,
            _timeProvider.GetUtcNow() + queue.DefaultRetention,
            async ct =>
            {
                // Re-publish the captured message unchanged (original timestamp) so receivers can still order
                // it correctly against operations that happened after the original publish attempt.
                await publisher.PublishAsync(message, cancellationToken: ct).ConfigureAwait(false);
                return HybridCacheRecoveryReplayOutcome.Replayed;
            },
            // The item's intent timestamp is the original publish time, not now: a foreign write that raced in
            // between must still win the incoming-invalidation conflict check against this pending publish.
            enqueuedAt: message.Timestamp
        );
    }

    /// <summary>
    /// Queues a Family-2 marker bump (tag/clear/remove) for auto-recovery after a failed/circuit-skipped L2 write.
    /// Replay re-asserts the marker at its ORIGINAL timestamp via the raise-only durable write (so a write that
    /// landed during the outage is not resurrected) and re-broadcasts the original message (closing the failed-
    /// publish half of the gap). Stored under a synthetic key so per-tag bumps coalesce and clear/remove are
    /// singletons; <see cref="HybridCacheRecoveryKind.MarkerBump"/> is exempt from the incoming-invalidation
    /// conflict drop because raise-only markers are idempotent. <paramref name="writeMarker"/> captures the original
    /// <c>invalidatedAt</c>, so replay writes that instant — not the recovery time.
    /// </summary>
    private void _QueueMarkerRecovery(
        ISeedableTagMarkerCache writer,
        Func<ISeedableTagMarkerCache, CancellationToken, ValueTask> writeMarker,
        string recoveryKey,
        CacheInvalidationMessage message
    )
    {
        var queue = RecoveryQueue!;

        queue.Enqueue(
            recoveryKey,
            HybridCacheRecoveryKind.MarkerBump,
            _timeProvider.GetUtcNow() + queue.DefaultRetention,
            async ct =>
            {
                await writeMarker(writer, ct).ConfigureAwait(false);
                // Re-broadcast best-effort, exactly like the live path: the durable marker has already landed
                // (raise-only), so a publish failure must NOT fail the replay (which would re-run the idempotent
                // durable write every retry and ultimately drop the item). _PublishInvalidationAsync logs and
                // honours ReThrowBackplaneExceptions; peers also converge via their L2 marker refresh window.
                await _PublishInvalidationAsync(message, ct).ConfigureAwait(false);
                return HybridCacheRecoveryReplayOutcome.Replayed;
            },
            // Intent timestamp = the original invalidation instant, so a value op written later (newer EnqueuedAt)
            // is replayed after this marker and survives it.
            enqueuedAt: message.Timestamp
        );
    }

    /// <summary>
    /// Publishes the key invalidation for a successfully replayed value op, mirroring the live path (a landed
    /// set/remove subsumes its invalidation). Stamped with the ORIGINAL write time so receivers order it
    /// correctly: a peer whose own pending write for the key is newer ignores it instead of wiping its L1. A
    /// publish failure queues a residual PublishInvalidation — the value already landed in L2, so a pending
    /// publish is the correct remaining intent; it replaces the value op being replayed and inherits the normal
    /// retry cap, so the failure path cannot loop unboundedly.
    /// </summary>
    private async ValueTask _PublishReplayedInvalidationAsync(
        string key,
        DateTimeOffset writeTimestamp,
        CancellationToken ct,
        bool expire = false
    )
    {
        await _PublishInvalidationAsync(
                new CacheInvalidationMessage
                {
                    InstanceId = _instanceId,
                    Key = key,
                    Expire = expire,
                    Timestamp = writeTimestamp,
                },
                ct,
                queueOnFailure: true
            )
            .ConfigureAwait(false);
    }
}
