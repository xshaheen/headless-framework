// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging;

namespace Headless.Caching;

public sealed partial class HybridCache
{
    #region ICache - Update Operations

    /// <inheritdoc />
    public async ValueTask<bool> UpsertAsync<T>(
        string key,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        if (expiration is { Ticks: <= 0 })
        {
            await RemoveAsync(key, cancellationToken).ConfigureAwait(false);
            return false;
        }

        _logger.LogSettingKey(key, expiration);

        // Background path: write L1 synchronously and detach the L2 write + publish. The caller's result no
        // longer reflects the L2 response, so we optimistically populate L1 (the additive write succeeded
        // locally) and return true. Capture every value the detached lambda needs before returning so it never
        // races disposal. A failed background write routes to recovery (when enabled) or is logged and swallowed.
        if (options.AllowBackgroundDistributedCacheOperations)
        {
            var localExpiration = _GetLocalExpiration(expiration);
            await LocalCache.UpsertAsync(key, value, localExpiration, cancellationToken).ConfigureAwait(false);

            _RunDetached(() => _BackgroundScalarUpsertAsync(key, value, expiration), key);

            return true;
        }

        bool updated;

        if (!_IsDistributedCacheCircuitClosed())
        {
            updated = true;
        }
        else
        {
            try
            {
                updated = await l2Cache.UpsertAsync(key, value, expiration, cancellationToken).ConfigureAwait(false);
                RecoveryQueue?.OnSuccessfulL2Operation(key);
            }
            catch (Exception exception)
                when (!FactoryCacheCoordinator.IsCallerCancellation(exception, cancellationToken)
                    && (RecoveryQueue is not null || options.DistributedCacheCircuitBreakerDuration > TimeSpan.Zero)
                )
            {
                // Degraded mode: with recovery on, queue the L2 write; with only the circuit breaker on, avoid
                // amplifying an unhealthy L2 and let the caller succeed against L1 for this additive write.
                _OpenDistributedCacheCircuit(exception, key);
                _logger.LogFailedToWriteToL2Cache(exception, key);
                if (RecoveryQueue is not null)
                {
                    _QueueScalarUpsertRecovery(key, value, expiration);
                }

                updated = true;
            }
        }

        if (updated)
        {
            var localExpiration = _GetLocalExpiration(expiration);
            await LocalCache.UpsertAsync(key, value, localExpiration, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // Remove from local cache when upsert fails
            await LocalCache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
        }

        await _PublishInvalidationAsync(
                new CacheInvalidationMessage { InstanceId = _instanceId, Key = key },
                cancellationToken,
                queueOnFailure: true
            )
            .ConfigureAwait(false);

        return updated;
    }

    /// <summary>
    /// The detached L2 tail of a scalar <see cref="UpsertAsync{T}"/> when background distributed operations are
    /// enabled. Runs with <see cref="CancellationToken.None"/>: the caller's token is gone once it returned, and
    /// cancelling a fire-and-forget L2 write would only abandon it. On L2 failure it routes to auto-recovery when
    /// enabled, otherwise logs and swallows (best-effort — the caller already succeeded against L1).
    /// </summary>
    private async Task _BackgroundScalarUpsertAsync<T>(string key, T? value, TimeSpan? expiration)
    {
        if (!_IsDistributedCacheCircuitClosed())
        {
            if (RecoveryQueue is not null)
            {
                _QueueScalarUpsertRecovery(key, value, expiration);
            }
        }
        else
        {
            try
            {
                await l2Cache.UpsertAsync(key, value, expiration, CancellationToken.None).ConfigureAwait(false);
                RecoveryQueue?.OnSuccessfulL2Operation(key);
            }
            catch (Exception exception)
                when (!FactoryCacheCoordinator.IsCallerCancellation(exception, CancellationToken.None))
            {
                _OpenDistributedCacheCircuit(exception, key);
                _logger.LogFailedToWriteToL2Cache(exception, key);

                if (RecoveryQueue is not null)
                {
                    // Same capture the synchronous degraded path uses: queue the failed L2 write for replay.
                    _QueueScalarUpsertRecovery(key, value, expiration);
                }

                // Auto-recovery off: best-effort, swallow. The caller already returned success (fire-and-forget).
            }
        }

        await _PublishInvalidationAsync(
                new CacheInvalidationMessage { InstanceId = _instanceId, Key = key },
                CancellationToken.None,
                queueOnFailure: true
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The detached L2 tail of a bulk <see cref="UpsertAllAsync{T}"/> when background distributed operations are
    /// enabled. Bulk ops are not captured by auto-recovery (issue #440), so an L2 failure here is best-effort:
    /// logged and swallowed. The publish runs regardless so peers still drop their stale L1 entries.
    /// </summary>
    private async Task _BackgroundBulkUpsertAsync<T>(
        Dictionary<string, T> snapshot,
        string[] keys,
        TimeSpan? expiration
    )
    {
        if (_IsDistributedCacheCircuitClosed())
        {
            try
            {
                await l2Cache.UpsertAllAsync(snapshot, expiration, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
                when (!FactoryCacheCoordinator.IsCallerCancellation(exception, CancellationToken.None))
            {
                if (keys.Length > 0)
                {
                    _OpenDistributedCacheCircuit(exception, keys[0]);
                }

                _logger.LogFailedBulkL2CacheOperation(exception, snapshot.Count);
            }
        }

        await _PublishInvalidationAsync(
                new CacheInvalidationMessage { InstanceId = _instanceId, Keys = keys },
                CancellationToken.None
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<bool> UpsertEntryAsync<T>(
        string key,
        T? value,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        // The hybrid store write fans out to L2 then L1 with the full per-entry metadata (tags included) and
        // publishes the key invalidation itself: every value-write through the composite store broadcasts.
        await ((IFactoryCacheStore)this)
            .UpsertEntryAsync(key, value, options, _timeProvider, cancellationToken)
            .ConfigureAwait(false);

        return true;
    }

    /// <inheritdoc />
    public async ValueTask<int> UpsertAllAsync<T>(
        IDictionary<string, T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNull(value);
        cancellationToken.ThrowIfCancellationRequested();

        if (value.Count == 0)
        {
            return 0;
        }

        if (expiration is { Ticks: <= 0 })
        {
            await RemoveAllAsync(value.Keys, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        _logger.LogSettingKeys(value.Keys, expiration);

        // Background path: write L1 synchronously and detach the L2 bulk write + publish. The caller no longer
        // depends on the L2 outcome, so we optimistically populate L1 with every entry and report the full count.
        // Bulk ops are not captured by auto-recovery (issue #440), so a failed background bulk write is purely
        // best-effort: logged and swallowed. Snapshot the dictionary and key array before detaching so the
        // background lambda owns immutable state and never observes a caller-side mutation.
        if (options.AllowBackgroundDistributedCacheOperations)
        {
            var snapshot = new Dictionary<string, T>(value, StringComparer.Ordinal);
            var keys = snapshot.Keys.ToArray();
            var localExpiration = _GetLocalExpiration(expiration);
            await LocalCache.UpsertAllAsync(snapshot, localExpiration, cancellationToken).ConfigureAwait(false);

            _RunDetached(() => _BackgroundBulkUpsertAsync(snapshot, keys, expiration), keys.Length > 0 ? keys[0] : "");

            return snapshot.Count;
        }

        int setCount;

        if (!_IsDistributedCacheCircuitClosed())
        {
            setCount = value.Count;
        }
        else
        {
            try
            {
                setCount = await l2Cache.UpsertAllAsync(value, expiration, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
                when (!FactoryCacheCoordinator.IsCallerCancellation(exception, cancellationToken)
                    && options.DistributedCacheCircuitBreakerDuration > TimeSpan.Zero
                )
            {
                _OpenDistributedCacheCircuit(exception, value.Keys.First());
                _logger.LogFailedBulkL2CacheOperation(exception, value.Count);
                setCount = value.Count;
            }
        }

        if (setCount == value.Count)
        {
            var localExpiration = _GetLocalExpiration(expiration);
            await LocalCache.UpsertAllAsync(value, localExpiration, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // Remove all keys from local cache when set fails or partially succeeds
            await LocalCache.RemoveAllAsync(value.Keys, cancellationToken).ConfigureAwait(false);
        }

        await _PublishInvalidationAsync(
                new CacheInvalidationMessage { InstanceId = _instanceId, Keys = value.Keys.ToArray() },
                cancellationToken
            )
            .ConfigureAwait(false);

        return setCount;
    }

    /// <inheritdoc />
    public async ValueTask<bool> TryInsertAsync<T>(
        string key,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        if (expiration is { Ticks: <= 0 })
        {
            await RemoveAsync(key, cancellationToken).ConfigureAwait(false);
            return false;
        }

        _logger.LogAddingKeyToLocalCache(key, expiration);

        var added = await l2Cache.TryInsertAsync(key, value, expiration, cancellationToken).ConfigureAwait(false);

        if (added)
        {
            var localExpiration = _GetLocalExpiration(expiration);
            await LocalCache.UpsertAsync(key, value, localExpiration, cancellationToken).ConfigureAwait(false);
        }

        return added;
    }

    /// <inheritdoc />
    public async ValueTask<bool> TryReplaceAsync<T>(
        string key,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        if (expiration is { Ticks: <= 0 })
        {
            await RemoveAsync(key, cancellationToken).ConfigureAwait(false);
            return false;
        }

        var replaced = await l2Cache.TryReplaceAsync(key, value, expiration, cancellationToken).ConfigureAwait(false);

        if (replaced)
        {
            var localExpiration = _GetLocalExpiration(expiration);
            await LocalCache.UpsertAsync(key, value, localExpiration, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // Remove from local cache when replace fails
            await LocalCache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
        }

        await _PublishInvalidationAsync(
                new CacheInvalidationMessage { InstanceId = _instanceId, Key = key },
                cancellationToken
            )
            .ConfigureAwait(false);

        return replaced;
    }

    /// <inheritdoc />
    public async ValueTask<bool> TryReplaceIfEqualAsync<T>(
        string key,
        T? expected,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        if (expiration is { Ticks: <= 0 })
        {
            await RemoveAsync(key, cancellationToken).ConfigureAwait(false);
            return false;
        }

        var replaced = await l2Cache
            .TryReplaceIfEqualAsync(key, expected, value, expiration, cancellationToken)
            .ConfigureAwait(false);

        if (replaced)
        {
            // Use UpsertAsync instead of TryReplaceIfEqualAsync for local cache because we know
            // the distributed cache now has this exact value, and we need local cache to be in sync
            var localExpiration = _GetLocalExpiration(expiration);
            await LocalCache.UpsertAsync(key, value, localExpiration, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // Remove from local cache when replace fails
            await LocalCache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
        }

        await _PublishInvalidationAsync(
                new CacheInvalidationMessage { InstanceId = _instanceId, Key = key },
                cancellationToken
            )
            .ConfigureAwait(false);

        return replaced;
    }

    /// <inheritdoc />
    public async ValueTask<double> IncrementAsync(
        string key,
        double amount,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        if (expiration is { Ticks: <= 0 })
        {
            await RemoveAsync(key, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        var newValue = await l2Cache.IncrementAsync(key, amount, expiration, cancellationToken).ConfigureAwait(false);

        if (newValue == 0)
        {
            // When result is 0, remove from local cache (conservative approach)
            await LocalCache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var localExpiration = _GetLocalExpiration(expiration);
            await LocalCache.UpsertAsync(key, newValue, localExpiration, cancellationToken).ConfigureAwait(false);
        }

        await _PublishInvalidationAsync(
                new CacheInvalidationMessage { InstanceId = _instanceId, Key = key },
                cancellationToken
            )
            .ConfigureAwait(false);

        return newValue;
    }

    /// <inheritdoc />
    public async ValueTask<long> IncrementAsync(
        string key,
        long amount,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        if (expiration is { Ticks: <= 0 })
        {
            await RemoveAsync(key, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        var newValue = await l2Cache.IncrementAsync(key, amount, expiration, cancellationToken).ConfigureAwait(false);

        if (newValue == 0)
        {
            // When result is 0, remove from local cache (conservative approach)
            await LocalCache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var localExpiration = _GetLocalExpiration(expiration);
            await LocalCache.UpsertAsync(key, newValue, localExpiration, cancellationToken).ConfigureAwait(false);
        }

        await _PublishInvalidationAsync(
                new CacheInvalidationMessage { InstanceId = _instanceId, Key = key },
                cancellationToken
            )
            .ConfigureAwait(false);

        return newValue;
    }

    /// <inheritdoc />
    public async ValueTask<double> SetIfHigherAsync(
        string key,
        double value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        if (expiration is { Ticks: <= 0 })
        {
            await RemoveAsync(key, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        var difference = await l2Cache
            .SetIfHigherAsync(key, value, expiration, cancellationToken)
            .ConfigureAwait(false);

        if (Math.Abs(difference) > double.Epsilon)
        {
            // Value was updated - we know the new value is exactly what we passed in
            var localExpiration = _GetLocalExpiration(expiration);
            await LocalCache.UpsertAsync(key, value, localExpiration, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // Value was not updated - remove from local cache since we don't know actual value
            await LocalCache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
        }

        await _PublishInvalidationAsync(
                new CacheInvalidationMessage { InstanceId = _instanceId, Key = key },
                cancellationToken
            )
            .ConfigureAwait(false);

        return difference;
    }

    /// <inheritdoc />
    public async ValueTask<long> SetIfHigherAsync(
        string key,
        long value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        if (expiration is { Ticks: <= 0 })
        {
            await RemoveAsync(key, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        var difference = await l2Cache
            .SetIfHigherAsync(key, value, expiration, cancellationToken)
            .ConfigureAwait(false);

        if (difference != 0)
        {
            var localExpiration = _GetLocalExpiration(expiration);
            await LocalCache.UpsertAsync(key, value, localExpiration, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await LocalCache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
        }

        await _PublishInvalidationAsync(
                new CacheInvalidationMessage { InstanceId = _instanceId, Key = key },
                cancellationToken
            )
            .ConfigureAwait(false);

        return difference;
    }

    /// <inheritdoc />
    public async ValueTask<double> SetIfLowerAsync(
        string key,
        double value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        if (expiration is { Ticks: <= 0 })
        {
            await RemoveAsync(key, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        var difference = await l2Cache.SetIfLowerAsync(key, value, expiration, cancellationToken).ConfigureAwait(false);

        if (Math.Abs(difference) > double.Epsilon)
        {
            var localExpiration = _GetLocalExpiration(expiration);
            await LocalCache.UpsertAsync(key, value, localExpiration, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await LocalCache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
        }

        await _PublishInvalidationAsync(
                new CacheInvalidationMessage { InstanceId = _instanceId, Key = key },
                cancellationToken
            )
            .ConfigureAwait(false);

        return difference;
    }

    /// <inheritdoc />
    public async ValueTask<long> SetIfLowerAsync(
        string key,
        long value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        if (expiration is { Ticks: <= 0 })
        {
            await RemoveAsync(key, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        var difference = await l2Cache.SetIfLowerAsync(key, value, expiration, cancellationToken).ConfigureAwait(false);

        if (difference != 0)
        {
            var localExpiration = _GetLocalExpiration(expiration);
            await LocalCache.UpsertAsync(key, value, localExpiration, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await LocalCache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
        }

        await _PublishInvalidationAsync(
                new CacheInvalidationMessage { InstanceId = _instanceId, Key = key },
                cancellationToken
            )
            .ConfigureAwait(false);

        return difference;
    }

    /// <inheritdoc />
    public async ValueTask<long> SetAddAsync<T>(
        string key,
        IEnumerable<T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        Argument.IsNotNull(value);
        cancellationToken.ThrowIfCancellationRequested();

        var items = value as T[] ?? value.ToArray();
        var addedCount = await l2Cache.SetAddAsync(key, items, expiration, cancellationToken).ConfigureAwait(false);

        if (addedCount == items.Length)
        {
            var localExpiration = _GetLocalExpiration(expiration);
            await LocalCache.SetAddAsync(key, items, localExpiration, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // Partial success - remove to force re-fetch
            await LocalCache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
        }

        await _PublishInvalidationAsync(
                new CacheInvalidationMessage { InstanceId = _instanceId, Key = key },
                cancellationToken
            )
            .ConfigureAwait(false);

        return addedCount;
    }

    #endregion

    #region ICache - Remove Operations

    /// <inheritdoc />
    public async ValueTask<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        bool removed;

        try
        {
            removed = await l2Cache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
            RecoveryQueue?.OnSuccessfulL2Operation(key);
        }
        catch (Exception exception)
            when (!FactoryCacheCoordinator.IsCallerCancellation(exception, cancellationToken)
                && (RecoveryQueue is not null || options.DistributedCacheCircuitBreakerDuration > TimeSpan.Zero)
            )
        {
            // Trip the breaker on a configured-breaker or recovery-enabled L2 failure so concurrent callers stop
            // hammering the down L2 — independent of auto-recovery, matching UpsertAllAsync. (No-op when the
            // breaker duration is zero.)
            _OpenDistributedCacheCircuit(exception, key);
            _logger.LogFailedToWriteToL2Cache(exception, key);

            if (RecoveryQueue is null)
            {
                // No recovery queue to replay the removal: surface the failure so the caller knows the L2 remove
                // may not have applied.
                throw;
            }

            // Degraded mode: L1 is removed below, the L2 removal is queued for replay, and we conservatively
            // report (and publish) the removal because the L2 state is unknown.
            _QueueRemoveRecovery(key);
            removed = true;
        }

        await LocalCache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);

        // Only notify other nodes if the key actually existed and was removed
        if (removed)
        {
            await _PublishInvalidationAsync(
                    new CacheInvalidationMessage { InstanceId = _instanceId, Key = key },
                    cancellationToken,
                    queueOnFailure: true
                )
                .ConfigureAwait(false);
        }

        return removed;
    }

    /// <inheritdoc />
    public async ValueTask<bool> ExpireAsync(string key, CancellationToken cancellationToken = default)
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        bool expired;

        try
        {
            expired = await l2Cache.ExpireAsync(key, cancellationToken).ConfigureAwait(false);
            RecoveryQueue?.OnSuccessfulL2Operation(key);
        }
        catch (Exception exception)
            when (!FactoryCacheCoordinator.IsCallerCancellation(exception, cancellationToken)
                && (RecoveryQueue is not null || options.DistributedCacheCircuitBreakerDuration > TimeSpan.Zero)
            )
        {
            // Trip the breaker on a configured-breaker or recovery-enabled L2 failure so concurrent callers stop
            // hammering the down L2 — independent of auto-recovery, mirrors RemoveAsync. (No-op when the breaker
            // duration is zero.)
            _OpenDistributedCacheCircuit(exception, key);
            _logger.LogFailedToWriteToL2Cache(exception, key);

            if (RecoveryQueue is null)
            {
                // No recovery queue to replay the expiration: surface the failure so the caller knows the L2
                // expire may not have applied.
                throw;
            }

            // Degraded mode: L1 is expired below, the L2 expiration is queued for replay, and we conservatively
            // report (and publish) the expiration because the L2 state is unknown — mirrors RemoveAsync.
            _QueueExpireRecovery(key);
            expired = true;
        }

        // Logically expire the local copy too: a peer's reserve is preserved, and so is ours.
        await LocalCache.ExpireAsync(key, cancellationToken).ConfigureAwait(false);

        // Only notify other nodes if the key actually existed; the Expire flag tells receivers to logically
        // expire their L1 copy (preserving its fail-safe reserve) rather than remove it.
        if (expired)
        {
            await _PublishInvalidationAsync(
                    new CacheInvalidationMessage
                    {
                        InstanceId = _instanceId,
                        Key = key,
                        Expire = true,
                    },
                    cancellationToken,
                    queueOnFailure: true
                )
                .ConfigureAwait(false);
        }

        return expired;
    }

    /// <inheritdoc />
    public async ValueTask<bool> RemoveIfEqualAsync<T>(
        string key,
        T? expected,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        var removed = await l2Cache.RemoveIfEqualAsync(key, expected, cancellationToken).ConfigureAwait(false);

        // Always remove from local cache unconditionally (local cache might have stale value)
        await LocalCache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);

        // Only notify other nodes if the key was actually removed from distributed cache
        if (removed)
        {
            await _PublishInvalidationAsync(
                    new CacheInvalidationMessage { InstanceId = _instanceId, Key = key },
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        return removed;
    }

    /// <inheritdoc />
    public async ValueTask<int> RemoveAllAsync(
        IEnumerable<string> cacheKeys,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        var items = cacheKeys?.ToArray();
        var flushAll = items is null or { Length: 0 };

        int removed;

        try
        {
            removed = await l2Cache.RemoveAllAsync(items!, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (!FactoryCacheCoordinator.IsCallerCancellation(exception, cancellationToken))
        {
            // Bulk ops are not captured by auto-recovery in v1 (issue #440); surface the L2 failure for
            // observability and rethrow so the caller is not told the bulk remove succeeded.
            if (items is { Length: > 0 })
            {
                _OpenDistributedCacheCircuit(exception, items[0]);
            }

            _logger.LogFailedBulkL2CacheOperation(exception, items?.Length ?? 0);
            throw;
        }

        await LocalCache.RemoveAllAsync(items!, cancellationToken).ConfigureAwait(false);

        // Only notify other nodes if keys were actually removed
        if (removed > 0)
        {
            await _PublishInvalidationAsync(
                    new CacheInvalidationMessage
                    {
                        InstanceId = _instanceId,
                        FlushAll = flushAll,
                        Keys = items,
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        return removed;
    }

    /// <inheritdoc />
    public async ValueTask<int> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        _ThrowIfDisposed();
        Argument.IsNotNull(prefix);
        cancellationToken.ThrowIfCancellationRequested();

        var removed = await l2Cache.RemoveByPrefixAsync(prefix, cancellationToken).ConfigureAwait(false);
        await LocalCache.RemoveByPrefixAsync(prefix, cancellationToken).ConfigureAwait(false);

        // Only notify other nodes if keys were actually removed
        if (removed > 0)
        {
            await _PublishInvalidationAsync(
                    new CacheInvalidationMessage { InstanceId = _instanceId, Prefix = prefix },
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        return removed;
    }

    /// <inheritdoc />
    public async ValueTask<int> RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(tag);
        cancellationToken.ThrowIfCancellationRequested();

        // Publish FIRST (matching the write-path ordering rationale: minimize the window in which another
        // instance re-populates its L1 from a not-yet-invalidated L2), then invalidate L2, then our own L1.
        await _PublishInvalidationAsync(
                new CacheInvalidationMessage { InstanceId = _instanceId, Tag = tag },
                cancellationToken
            )
            .ConfigureAwait(false);

        var removed = await l2Cache.RemoveByTagAsync(tag, cancellationToken).ConfigureAwait(false);
        await LocalCache.RemoveByTagAsync(tag, cancellationToken).ConfigureAwait(false);

        return removed;
    }

    /// <inheritdoc />
    public async ValueTask<long> SetRemoveAsync<T>(
        string key,
        IEnumerable<T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        Argument.IsNotNull(value);
        cancellationToken.ThrowIfCancellationRequested();

        var items = value as T[] ?? value.ToArray();
        var removedCount = await l2Cache
            .SetRemoveAsync(key, items, expiration, cancellationToken)
            .ConfigureAwait(false);

        if (removedCount == items.Length)
        {
            await LocalCache.SetRemoveAsync(key, items, expiration, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // Partial success - remove to force re-fetch
            await LocalCache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
        }

        // Only notify other nodes if items were actually removed
        if (removedCount > 0)
        {
            await _PublishInvalidationAsync(
                    new CacheInvalidationMessage { InstanceId = _instanceId, Key = key },
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        return removedCount;
    }

    /// <inheritdoc />
    public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        _ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        await l2Cache.FlushAsync(cancellationToken).ConfigureAwait(false);
        await LocalCache.FlushAsync(cancellationToken).ConfigureAwait(false);
        await _PublishInvalidationAsync(
                new CacheInvalidationMessage { InstanceId = _instanceId, FlushAll = true },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    #endregion
}
