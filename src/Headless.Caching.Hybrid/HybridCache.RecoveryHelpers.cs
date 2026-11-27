// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

public sealed partial class HybridCache
{
    private readonly record struct ScalarUpsertSnapshot(
        DateTimeOffset WrittenAt,
        (long Global, long Key) Generation,
        string? ConcurrencyStamp,
        DateTime? PhysicalExpiresAt
    );

    private async ValueTask<ScalarUpsertSnapshot?> _WriteLocalScalarAsync<T>(
        string key,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken
    )
    {
        if (RecoveryQueue is null)
        {
            await _L1For(key).UpsertAsync(key, value, expiration, cancellationToken).ConfigureAwait(false);
            return null;
        }

        var writtenAt = _timeProvider.GetUtcNow();
        var committed = await _L1For(key, out var generation)
            .UpsertAsync(key, value, expiration, cancellationToken)
            .ConfigureAwait(false);
        if (!committed || !_L1GenerationHolds(key, generation))
        {
            return null;
        }

        if (LocalCache is not IFactoryCacheStore l1Store)
        {
            return new ScalarUpsertSnapshot(writtenAt, generation, ConcurrencyStamp: null, PhysicalExpiresAt: null);
        }

        // Capture the store-owned stamp while the generation still belongs to this write. A detached retry
        // could otherwise read the stamp of a different value under the same key.
        CacheStoreEntry<object> current;
        try
        {
            current = await l1Store
                .TryGetEntryAsync<object>(key, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (!FactoryCacheCoordinator.IsCallerCancellation(exception, cancellationToken))
        {
            _logger.LogCacheStoreReadFailed(exception, key);
            return null;
        }

        if (!current.Found || !_L1GenerationHolds(key, generation))
        {
            return null;
        }

        if (current.CreatedAt is { } createdAt)
        {
            writtenAt = new DateTimeOffset(createdAt, TimeSpan.Zero);
        }

        return new ScalarUpsertSnapshot(writtenAt, generation, current.ConcurrencyStamp, current.PhysicalExpiresAt);
    }

    /// <summary>
    /// Checks that the L1 entry still corresponds to the operation queued for recovery. When L1 exposes no stamp,
    /// presence is the only fallback available here.
    /// </summary>
    /// <param name="key">The key the queued write targets.</param>
    /// <param name="l1PhysicalStamp">The physical expiry the write left on the L1 entry.</param>
    /// <param name="requireLogicallyLive">
    /// Also require that the L1 entry has not been logically invalidated since (a tag or clear marker, local or from a
    /// peer). A scalar upsert replays with its original birth time when L2 exposes <see cref="IFactoryCacheStore"/>,
    /// so the L2 markers outrank it there too; against any other L2 it falls back to a plain write born at replay
    /// time, where only this guard keeps an invalidation this node has seen from losing to the replay.
    /// </param>
    /// <param name="concurrencyStamp">The exact store-owned stamp, when the original write captured one.</param>
    private Func<CancellationToken, ValueTask<bool>> _BuildL1StampGuard<T>(
        string key,
        DateTime? l1PhysicalStamp,
        bool requireLogicallyLive = false,
        string? concurrencyStamp = null
    )
    {
        return async ct =>
        {
            if (LocalCache is IFactoryCacheStore l1Store && (concurrencyStamp is not null || l1PhysicalStamp.HasValue))
            {
                var current = await l1Store.TryGetEntryAsync<T>(key, cancellationToken: ct).ConfigureAwait(false);

                var stampMatches = concurrencyStamp is not null
                    ? string.Equals(current.ConcurrencyStamp, concurrencyStamp, StringComparison.Ordinal)
                    : current.PhysicalExpiresAt == l1PhysicalStamp;
                if (!current.Found || !stampMatches)
                {
                    return false;
                }

                // A tag/clear-invalidated entry reads back with its logical expiry clamped to the read instant.
                return !requireLogicallyLive || current.LogicalExpiresAt is not { } logical || logical > _GetUtcNow();
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
                var current = await l1Store.TryGetEntryAsync<object>(key, cancellationToken: ct).ConfigureAwait(false);

                return !(current.Found && current.PhysicalExpiresAt > baseline);
            }

            var fallback = await LocalCache.GetAsync<object>(key, ct).ConfigureAwait(false);

            return !fallback.HasValue;
        };
    }

    private async ValueTask _QueueScalarUpsertRecoveryAsync<T>(
        string key,
        T? value,
        TimeSpan? expiration,
        ScalarUpsertSnapshot? snapshot,
        CancellationToken cancellationToken
    )
    {
        if (RecoveryQueue is not { } queue || snapshot is not { } write)
        {
            return;
        }

        var deadline = expiration.HasValue
            ? write.WrittenAt.SafeAdd(expiration.Value)
            : _timeProvider.GetUtcNow().SafeAdd(queue.DefaultRetention);
        var l1Guard = _BuildL1StampGuard<object>(
            key,
            write.PhysicalExpiresAt,
            requireLogicallyLive: true,
            concurrencyStamp: write.ConcurrencyStamp
        );

        async ValueTask<bool> isCurrent(CancellationToken ct) =>
            (write.ConcurrencyStamp is not null || _L1GenerationHolds(key, write.Generation))
            && await l1Guard(ct).ConfigureAwait(false);

        if (!_L1GenerationHolds(key, write.Generation) && !await isCurrent(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        queue.Enqueue(
            key,
            HybridCacheRecoveryKind.SetEntry,
            deadline,
            async ct =>
            {
                if (!await isCurrent(ct).ConfigureAwait(false))
                {
                    return HybridCacheRecoveryReplayOutcome.Obsolete;
                }

                var now = _timeProvider.GetUtcNow();
                if (expiration.HasValue && deadline <= now)
                {
                    return HybridCacheRecoveryReplayOutcome.Obsolete;
                }

                if (l2Cache is IFactoryCacheStore l2Store)
                {
                    // Replay the write as it was made: its original birth time and absolute expiry. A plain upsert
                    // would stamp the entry with the replay time, so a tag or clear invalidation issued on another
                    // node between the write and this replay would read as older than the entry and lose to it.
                    var expiresAt = expiration.HasValue ? deadline.UtcDateTime : DateTime.MaxValue;
                    var entry = new CacheStoreEntryWrite<T>
                    {
                        Value = value,
                        IsNull = value is null,
                        LogicalExpiresAt = expiresAt,
                        PhysicalExpiresAt = expiresAt,
                        CreatedAt = write.WrittenAt.UtcDateTime,
                    };

                    await l2Store.SetEntryAsync(key, entry, ct).ConfigureAwait(false);
                }
                else
                {
                    TimeSpan? remaining = expiration.HasValue ? deadline - now : null;
                    await l2Cache.UpsertAsync(key, value, remaining, ct).ConfigureAwait(false);
                }

                await _PublishReplayedInvalidationAsync(key, write.WrittenAt, ct).ConfigureAwait(false);
                return HybridCacheRecoveryReplayOutcome.Replayed;
            },
            enqueuedAt: write.WrittenAt,
            scalarWriteGeneration: write.Generation.Key
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
                await backplane.PublishAsync(message, ct).ConfigureAwait(false);
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
