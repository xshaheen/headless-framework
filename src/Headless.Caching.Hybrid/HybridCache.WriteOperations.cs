// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

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
        // Set when the degraded path must queue the L2 write for replay. The queueing is deferred until after the
        // L1 write below so the captured physical stamp reflects the entry this upsert actually committed.
        var queueScalarRecovery = false;

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
                queueScalarRecovery = RecoveryQueue is not null;

                updated = true;
            }
        }

        if (updated)
        {
            var localExpiration = _GetLocalExpiration(expiration);
            await LocalCache.UpsertAsync(key, value, localExpiration, cancellationToken).ConfigureAwait(false);

            if (queueScalarRecovery)
            {
                // Capture the just-written L1 physical stamp so the replay guard can detect a newer local write.
                var stamp = await _CaptureLocalPhysicalStampAsync<T>(key, cancellationToken).ConfigureAwait(false);
                _QueueScalarUpsertRecovery(key, value, expiration, stamp);
            }
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
        // L1 was already written synchronously before this detached tail ran, so its physical stamp is captured
        // here (once) to gate any recovery replay against a newer local write — symmetric with the set-entry path.
        if (!_IsDistributedCacheCircuitClosed())
        {
            if (RecoveryQueue is not null)
            {
                var stamp = await _CaptureLocalPhysicalStampAsync<T>(key, CancellationToken.None).ConfigureAwait(false);
                _QueueScalarUpsertRecovery(key, value, expiration, stamp);
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
                    var stamp = await _CaptureLocalPhysicalStampAsync<T>(key, CancellationToken.None)
                        .ConfigureAwait(false);
                    _QueueScalarUpsertRecovery(key, value, expiration, stamp);
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
        await (this).UpsertEntryAsync(key, value, options, _timeProvider, cancellationToken).ConfigureAwait(false);

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

        if (!_IsDistributedCacheCircuitClosed())
        {
            return false;
        }

        bool added;

        try
        {
            added = await l2Cache.TryInsertAsync(key, value, expiration, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
            when (!FactoryCacheCoordinator.IsCallerCancellation(exception, cancellationToken)
                && (RecoveryQueue is not null || options.DistributedCacheCircuitBreakerDuration > TimeSpan.Zero)
            )
        {
            _OpenDistributedCacheCircuit(exception, key);
            _logger.LogFailedToWriteToL2Cache(exception, key);
            return false;
        }

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

        if (!_IsDistributedCacheCircuitClosed())
        {
            await LocalCache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
            return false;
        }

        bool replaced;

        try
        {
            replaced = await l2Cache.TryReplaceAsync(key, value, expiration, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
            when (!FactoryCacheCoordinator.IsCallerCancellation(exception, cancellationToken)
                && (RecoveryQueue is not null || options.DistributedCacheCircuitBreakerDuration > TimeSpan.Zero)
            )
        {
            _OpenDistributedCacheCircuit(exception, key);
            _logger.LogFailedToWriteToL2Cache(exception, key);
            await LocalCache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
            return false;
        }

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
    public ValueTask<double> IncrementAsync(
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
            return _RemoveAndReturn(key, 0d, cancellationToken);
        }

        return _RunNumericL2AndSyncL1Async(
            key,
            expiration,
            ct => l2Cache.IncrementAsync(key, amount, expiration, ct),
            static result => result != 0,
            static result => result,
            cancellationToken
        );
    }

    /// <inheritdoc />
    public ValueTask<long> IncrementAsync(
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
            return _RemoveAndReturn(key, 0L, cancellationToken);
        }

        return _RunNumericL2AndSyncL1Async(
            key,
            expiration,
            ct => l2Cache.IncrementAsync(key, amount, expiration, ct),
            static result => result != 0,
            static result => result,
            cancellationToken
        );
    }

    /// <inheritdoc />
    public ValueTask<double> SetIfHigherAsync(
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
            return _RemoveAndReturn(key, 0d, cancellationToken);
        }

        return _RunNumericL2AndSyncL1Async(
            key,
            expiration,
            ct => l2Cache.SetIfHigherAsync(key, value, expiration, ct),
            static result => Math.Abs(result) > double.Epsilon,
            _ => value,
            cancellationToken
        );
    }

    /// <inheritdoc />
    public ValueTask<long> SetIfHigherAsync(
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
            return _RemoveAndReturn(key, 0L, cancellationToken);
        }

        return _RunNumericL2AndSyncL1Async(
            key,
            expiration,
            ct => l2Cache.SetIfHigherAsync(key, value, expiration, ct),
            static result => result != 0,
            _ => value,
            cancellationToken
        );
    }

    /// <inheritdoc />
    public ValueTask<double> SetIfLowerAsync(
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
            return _RemoveAndReturn(key, 0d, cancellationToken);
        }

        return _RunNumericL2AndSyncL1Async(
            key,
            expiration,
            ct => l2Cache.SetIfLowerAsync(key, value, expiration, ct),
            static result => Math.Abs(result) > double.Epsilon,
            _ => value,
            cancellationToken
        );
    }

    /// <inheritdoc />
    public ValueTask<long> SetIfLowerAsync(
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
            return _RemoveAndReturn(key, 0L, cancellationToken);
        }

        return _RunNumericL2AndSyncL1Async(
            key,
            expiration,
            ct => l2Cache.SetIfLowerAsync(key, value, expiration, ct),
            static result => result != 0,
            _ => value,
            cancellationToken
        );
    }

    /// <summary>
    /// Removes the key and immediately returns <paramref name="zeroValue"/>; shared early-exit path for
    /// numeric operations whose expiration has already elapsed.
    /// </summary>
    private async ValueTask<TResult> _RemoveAndReturn<TResult>(
        string key,
        TResult zeroValue,
        CancellationToken cancellationToken
    )
    {
        await RemoveAsync(key, cancellationToken).ConfigureAwait(false);
        return zeroValue;
    }

    /// <summary>
    /// Executes a numeric L2 operation, syncs the result to L1, and publishes a key invalidation. Shared by
    /// <see cref="IncrementAsync(string,double,TimeSpan?,CancellationToken)"/>,
    /// <see cref="SetIfHigherAsync(string,double,TimeSpan?,CancellationToken)"/>,
    /// <see cref="SetIfLowerAsync(string,double,TimeSpan?,CancellationToken)"/>, and their <c>long</c> overloads.
    /// </summary>
    /// <typeparam name="TResult">Return type of the L2 operation (e.g. <c>double</c> or <c>long</c>).</typeparam>
    /// <typeparam name="TL1">Type stored in L1 (equals <typeparamref name="TResult"/> for increment, equals the
    /// input value type for set-if-higher/lower).</typeparam>
    /// <param name="isUpdated">
    /// Returns <see langword="true"/> when the L2 result indicates the value was actually changed and should be
    /// populated in L1. When <see langword="false"/>, L1 is cleared instead (we don't know the actual value).
    /// </param>
    /// <param name="l1Value">Derives the L1 value to store from the L2 result.</param>
    private async ValueTask<TResult> _RunNumericL2AndSyncL1Async<TResult, TL1>(
        string key,
        TimeSpan? expiration,
        Func<CancellationToken, ValueTask<TResult>> l2Op,
        Func<TResult, bool> isUpdated,
        Func<TResult, TL1> l1Value,
        CancellationToken cancellationToken
    )
    {
        var result = await l2Op(cancellationToken).ConfigureAwait(false);

        if (isUpdated(result))
        {
            var localExpiration = _GetLocalExpiration(expiration);
            await LocalCache
                .UpsertAsync(key, l1Value(result), localExpiration, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            // Result indicates no change — remove from L1 since we don't know the actual stored value.
            await LocalCache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
        }

        await _PublishInvalidationAsync(
                new CacheInvalidationMessage { InstanceId = _instanceId, Key = key },
                cancellationToken
            )
            .ConfigureAwait(false);

        return result;
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
        Argument.IsNotNull(cacheKeys);
        cancellationToken.ThrowIfCancellationRequested();

        var items = cacheKeys.ToArray();

        int removed;

        try
        {
            removed = await l2Cache.RemoveAllAsync(items, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (!FactoryCacheCoordinator.IsCallerCancellation(exception, cancellationToken))
        {
            // Bulk ops are not captured by auto-recovery in v1 (issue #440); surface the L2 failure for
            // observability. Clean up L1 first so this node is not left with stale entries, then rethrow so
            // the caller is not told the bulk remove succeeded.
            if (items.Length > 0)
            {
                _OpenDistributedCacheCircuit(exception, items[0]);
            }

            _logger.LogFailedBulkL2CacheOperation(exception, items.Length);
            await LocalCache.RemoveAllAsync(items, cancellationToken).ConfigureAwait(false);
            throw;
        }

        await LocalCache.RemoveAllAsync(items, cancellationToken).ConfigureAwait(false);

        // Only notify other nodes if keys were actually removed
        if (removed > 0)
        {
            await _PublishInvalidationAsync(
                    new CacheInvalidationMessage { InstanceId = _instanceId, Keys = items },
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
    public async ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(tag);
        cancellationToken.ThrowIfCancellationRequested();

        // One timestamp generated here flows through L1, the broadcast, and L2 (FusionCache's single-clock model),
        // so every node — origin and peers — version-pins this invalidation against the same instant.
        var invalidatedAt = _timeProvider.GetUtcNow();

        // L1 FIRST and unconditional: the local marker bump is in-process and infallible, so this node's own
        // invalidation always takes effect even when L2 is unreachable. Seed it with invalidatedAt (not the local
        // store's own clock) so L1 agrees with the broadcast timestamp peers will apply.
        if (LocalCache is ISeedableTagMarkerCache l1Markers)
        {
            l1Markers.SeedTagMarker(tag, invalidatedAt);
        }
        else
        {
            await LocalCache.RemoveByTagAsync(tag, cancellationToken).ConfigureAwait(false);
        }

        // Broadcast carrying the timestamp so peers seed their L1/L2 from the origin clock; publish before the L2
        // bump to minimize the window in which a peer re-populates its L1 from a not-yet-invalidated L2. The same
        // message is reused as the auto-recovery replay payload (re-broadcast at its original timestamp).
        var message = new CacheInvalidationMessage
        {
            InstanceId = _instanceId,
            Tag = tag,
            Timestamp = invalidatedAt,
        };
        await _PublishInvalidationAsync(message, cancellationToken).ConfigureAwait(false);

        // L2 marker bump is best-effort under the circuit breaker. When the L2 supports timestamped marker writes
        // (ISeedableTagMarkerCache) and auto-recovery is enabled, a skipped/failed bump is queued and replayed at
        // its original timestamp on recovery; otherwise it is bounded by each entry's physical TTL.
        await _BumpL2MarkerBestEffortAsync(
                (writer, ct) => writer.WriteTagMarkerAsync(tag, invalidatedAt, ct),
                ct => l2Cache.RemoveByTagAsync(tag, ct),
                _TagMarkerRecoveryKey(tag),
                message,
                tag,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask ClearAsync(CancellationToken cancellationToken = default)
    {
        _ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        // Single-clock model (see RemoveByTagAsync): L1 first and unconditional, then broadcast, then best-effort
        // L2 under the circuit breaker. Reserves are preserved on all tiers (logical clear, not a physical wipe).
        var invalidatedAt = _timeProvider.GetUtcNow();

        if (LocalCache is ISeedableTagMarkerCache l1Markers)
        {
            l1Markers.SeedClearMarker(invalidatedAt);
        }
        else
        {
            await LocalCache.ClearAsync(cancellationToken).ConfigureAwait(false);
        }

        var message = new CacheInvalidationMessage
        {
            InstanceId = _instanceId,
            Clear = true,
            Timestamp = invalidatedAt,
        };
        await _PublishInvalidationAsync(message, cancellationToken).ConfigureAwait(false);

        await _BumpL2MarkerBestEffortAsync(
                (writer, ct) => writer.WriteClearMarkerAsync(invalidatedAt, ct),
                ct => l2Cache.ClearAsync(ct),
                _ClearMarkerRecoveryKey,
                message,
                "*",
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    // Synthetic recovery-queue keys for marker bumps. The NUL prefix keeps them in their own namespace within the
    // queue's key-keyed dictionary so they never collide with real cache keys (and per-tag keys coalesce naturally).
    private const string _ClearMarkerRecoveryKey = "\0hybrid-marker:clear";
    private const string _RemoveMarkerRecoveryKey = "\0hybrid-marker:remove";

    private static string _TagMarkerRecoveryKey(string tag) => string.Concat("\0hybrid-marker:tag:", tag);

    /// <summary>
    /// Applies a Family-2 marker bump (tag/clear/remove) to L2 as a best-effort operation: skipped when the
    /// distributed-cache circuit is open, and on failure it trips the circuit and logs rather than propagating —
    /// the caller has already applied the infallible L1 bump and broadcast the invalidation. When the L2 supports
    /// timestamped marker writes (<see cref="ISeedableTagMarkerCache"/>) and auto-recovery is enabled, a
    /// skipped/failed bump is queued and replayed at its <em>original</em> timestamp (raise-only durable write) on
    /// recovery; otherwise the shared-store marker is bounded by each entry's physical TTL. A successful bump
    /// supersedes any queued recovery for the same marker. Honours
    /// <see cref="HybridCacheOptions.ReThrowDistributedCacheExceptions"/>.
    /// </summary>
    private async ValueTask _BumpL2MarkerBestEffortAsync(
        Func<ISeedableTagMarkerCache, CancellationToken, ValueTask> writeMarker,
        Func<CancellationToken, ValueTask> fallbackBump,
        string recoveryKey,
        CacheInvalidationMessage message,
        string circuitKey,
        CancellationToken cancellationToken
    )
    {
        var writer = l2Cache as ISeedableTagMarkerCache;

        if (!_IsDistributedCacheCircuitClosed())
        {
            // Circuit open: skip the live write. Queue for replay only when the L2 can re-assert the original
            // timestamp (raise-only timestamped write) and auto-recovery is on.
            if (writer is not null && RecoveryQueue is not null)
            {
                _QueueMarkerRecovery(writer, writeMarker, recoveryKey, message);
            }

            return;
        }

        try
        {
            if (writer is not null)
            {
                await writeMarker(writer, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await fallbackBump(cancellationToken).ConfigureAwait(false);
            }

            // A successful live bump supersedes a queued bump for the SAME marker only when this write's generation
            // is at least as new — an older live write (raise-only, so it left a newer marker intact) must not drop
            // a queued newer-generation bump, or that newer invalidation would be lost on the shared store.
            RecoveryQueue?.OnSuccessfulMarkerBump(recoveryKey, message.Timestamp ?? _timeProvider.GetUtcNow());
        }
        catch (Exception exception) when (!FactoryCacheCoordinator.IsCallerCancellation(exception, cancellationToken))
        {
            _OpenDistributedCacheCircuit(exception, circuitKey);
            _logger.LogFailedToWriteToL2Cache(exception, circuitKey);

            if (writer is not null && RecoveryQueue is not null)
            {
                _QueueMarkerRecovery(writer, writeMarker, recoveryKey, message);
            }

            if (options.ReThrowDistributedCacheExceptions)
            {
                throw;
            }
        }
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

        // Single-clock model (see RemoveByTagAsync): physically wipe L1 first and unconditionally (in-process,
        // infallible — this drops the local fail-safe reserves), then broadcast FlushAll carrying the timestamp,
        // then bump the L2 remove-generation marker best-effort under the circuit breaker. On a distributed tier
        // FlushAsync is a logical remove marker (cluster-safe, no physical wipe); receivers physically wipe their
        // own L1 on the FlushAll broadcast and seed their L2 remove marker from the origin timestamp.
        var invalidatedAt = _timeProvider.GetUtcNow();

        await LocalCache.FlushAsync(cancellationToken).ConfigureAwait(false);

        var message = new CacheInvalidationMessage
        {
            InstanceId = _instanceId,
            FlushAll = true,
            Timestamp = invalidatedAt,
        };
        await _PublishInvalidationAsync(message, cancellationToken).ConfigureAwait(false);

        await _BumpL2MarkerBestEffortAsync(
                (writer, ct) => writer.WriteRemoveMarkerAsync(invalidatedAt, ct),
                ct => l2Cache.FlushAsync(ct),
                _RemoveMarkerRecoveryKey,
                message,
                "__remove",
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    #endregion
}
