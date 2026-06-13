// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;

namespace Headless.Caching;

public sealed partial class HybridCache
{
    private void _QueueScalarUpsertRecovery<T>(string key, T? value, TimeSpan? expiration)
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
                // L1 is the source of truth: if the entry is gone, the queued write is obsolete. A surviving
                // queued item implies no newer single-key write went through this instance (newer ops replace
                // or clear it), and foreign writes drop it via the invalidation conflict check.
                var current = await LocalCache.GetAsync<T>(key, ct).ConfigureAwait(false);

                if (!current.HasValue)
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
