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

    private void _QueueScalarUpsertRecovery<T>(string key, T? value, TimeSpan? expiration, DateTime? l1PhysicalStamp)
    {
        var queue = RecoveryQueue!;
        var now = _timeProvider.GetUtcNow();

        // Values without a TTL have no natural item expiry; bound them by the generous fixed window.
        var deadline = expiration.HasValue ? now + expiration.Value : now + queue.DefaultRetention;

        queue.Enqueue(
            key,
            HybridCacheRecoveryKind.SetEntry,
            deadline,
            async ct =>
            {
                // L1 is the source of truth: only replay if the L1 entry still exists and carries the exact
                // physical stamp this write produced — a different stamp means a newer local write landed and
                // the queued write is obsolete. Mirrors _QueueSetEntryRecovery's stamp-aware guard; when L1 does
                // not expose the entry store the stamp is null and we fall back to the presence-only check.
                if (LocalCache is IFactoryCacheStore l1Store && l1PhysicalStamp.HasValue)
                {
                    var current = await l1Store.TryGetEntryAsync<T>(key, ct).ConfigureAwait(false);

                    if (!current.Found || current.PhysicalExpiresAt != l1PhysicalStamp)
                    {
                        return HybridCacheRecoveryReplayOutcome.Obsolete;
                    }
                }
                else
                {
                    var current = await LocalCache.GetAsync<T>(key, ct).ConfigureAwait(false);

                    if (!current.HasValue)
                    {
                        return HybridCacheRecoveryReplayOutcome.Obsolete;
                    }
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

        queue.Enqueue(
            key,
            HybridCacheRecoveryKind.SetEntry,
            new DateTimeOffset(DateTime.SpecifyKind(entry.PhysicalExpiresAt, DateTimeKind.Utc)),
            async ct =>
            {
                // L1 is the source of truth: only replay if the L1 entry still exists and carries the exact
                // physical stamp this write produced — a different stamp means the entry changed and the
                // queued write is obsolete.
                if (LocalCache is IFactoryCacheStore l1Store && l1PhysicalStamp.HasValue)
                {
                    var current = await l1Store.TryGetEntryAsync<T>(key, ct).ConfigureAwait(false);

                    if (!current.Found || current.PhysicalExpiresAt != l1PhysicalStamp)
                    {
                        return HybridCacheRecoveryReplayOutcome.Obsolete;
                    }
                }
                else
                {
                    var current = await LocalCache.GetAsync<T>(key, ct).ConfigureAwait(false);

                    if (!current.HasValue)
                    {
                        return HybridCacheRecoveryReplayOutcome.Obsolete;
                    }
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

        queue.Enqueue(
            key,
            HybridCacheRecoveryKind.Remove,
            removeTimestamp + queue.DefaultRetention,
            async ct =>
            {
                // Guard against replaying a remove that would delete a value written during the outage. If L1
                // still has an entry for this key then a newer write landed after the remove was queued; replaying
                // the remove would silently clobber that write. Mirrors the stamp-aware guard in
                // _QueueScalarUpsertRecovery / _QueueSetEntryRecovery (but uses presence-only because removes do
                // not produce a physical stamp to compare against).
                if (LocalCache is IFactoryCacheStore l1Store)
                {
                    var current = await l1Store.TryGetEntryAsync<object>(key, ct).ConfigureAwait(false);

                    if (current.Found && current.PhysicalExpiresAt > removeTimestamp)
                    {
                        return HybridCacheRecoveryReplayOutcome.Obsolete;
                    }
                }
                else
                {
                    var current = await LocalCache.GetAsync<object>(key, ct).ConfigureAwait(false);

                    if (current.HasValue)
                    {
                        return HybridCacheRecoveryReplayOutcome.Obsolete;
                    }
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

        queue.Enqueue(
            key,
            HybridCacheRecoveryKind.Expire,
            expireTimestamp + queue.DefaultRetention,
            async ct =>
            {
                // Guard: if L1 has a newer entry (physical stamp after the expire timestamp), a write landed
                // during the outage and replaying the expire would evict it on L2. Return Obsolete to preserve
                // the newer write — mirrors the remove guard above.
                if (LocalCache is IFactoryCacheStore l1Store)
                {
                    var current = await l1Store.TryGetEntryAsync<object>(key, ct).ConfigureAwait(false);

                    if (current.Found && current.PhysicalExpiresAt > expireTimestamp)
                    {
                        return HybridCacheRecoveryReplayOutcome.Obsolete;
                    }
                }
                else
                {
                    var current = await LocalCache.GetAsync<object>(key, ct).ConfigureAwait(false);

                    if (current.HasValue)
                    {
                        return HybridCacheRecoveryReplayOutcome.Obsolete;
                    }
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
