// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Caching;

// Eager (proactive) refresh: a read past the entry's eager threshold detaches a single-flight refresh that
// re-runs the factory under the per-key (and optionally distributed) lock while the still-fresh value is served.
// Also hosts the read-path sliding re-arm and the shared best-effort distributed-lease release helper.
public sealed partial class FactoryCacheCoordinator
{
    private void _MaybeStartEagerRefresh<T>(
        IFactoryCacheStore store,
        string key,
        Func<CacheFactoryContext<T>, CancellationToken, ValueTask<CacheFactoryResult<T>>> factory,
        CacheEntryOptions options,
        CacheStoreEntry<T> entry,
        DateTime now
    )
    {
        // The trigger is the entry's own stamp, so any reader of an eager-stamped entry can refresh it with
        // its current factory and options. Entries without both expirations cannot be gate-rewritten safely.
        if (entry.EagerRefreshAt is not { } eagerRefreshAt || eagerRefreshAt > now)
        {
            return;
        }

        if (!entry.LogicalExpiresAt.HasValue || !entry.PhysicalExpiresAt.HasValue)
        {
            return;
        }

        // Zero-timeout dedup: the first reader past the eager point wins the per-key lock; everyone else
        // returns the still-fresh value untouched. Lock ownership transfers to the detached refresh.
        var releaser = _keyedLock.TryLock(key);

        if (releaser is null)
        {
            return;
        }

        var refreshTask = _RunEagerRefreshAsync(store, key, factory, options, releaser);
        _ObserveFaultedTask(refreshTask, key);
    }

    private async Task _RunEagerRefreshAsync<T>(
        IFactoryCacheStore store,
        string key,
        Func<CacheFactoryContext<T>, CancellationToken, ValueTask<CacheFactoryResult<T>>> factory,
        CacheEntryOptions options,
        IDisposable releaser
    )
    {
        var ownsReleaser = true;
        IAsyncDisposable? distributedLease = null;

        try
        {
            // Yield so the triggering caller returns its fresh value without paying for the gate write or
            // the factory's synchronous prologue.
            await Task.Yield();

            // Double-check under the lock: a concurrent refresh may have already advanced or cleared the stamp.
            var entry = await _TryGetEntryAsync<T>(store, key, CancellationToken.None).ConfigureAwait(false);
            var now = _GetUtcNow();

            if (
                !entry.IsFresh(now)
                || entry.EagerRefreshAt is not { } eagerRefreshAt
                || eagerRefreshAt > now
                || entry.LogicalExpiresAt is not { } logicalExpiresAt
                || entry.PhysicalExpiresAt is not { } physicalExpiresAt
            )
            {
                return;
            }

            if (options.UseDistributedFactoryLock)
            {
                // Cross-node dedup mirrors the local zero-timeout TryLock: a single non-blocking attempt. When the
                // lock is held elsewhere another node is already refreshing, so skip silently and leave the entry
                // (including its eager stamp) untouched; that node's gate write clears the stamp for everyone.
                distributedLease = await _factoryLockProvider!
                    .TryAcquireAsync(key, TimeSpan.Zero, CancellationToken.None)
                    .ConfigureAwait(false);

                if (distributedLease is null)
                {
                    return;
                }
            }

            // Build the per-execution context from the double-checked entry (before the gate write, which only
            // clears the eager stamp) so a conditional eager factory can extend the still-fresh value in place.
            var context = _CreateFactoryContext(key, entry, options, now);

            // Write gate: clear the stamp before the factory starts so other readers (including other nodes
            // reading through a shared store) stop triggering while the refresh is in flight. Best-effort:
            // when the gate write fails the refresh is skipped and the entry stays fresh and re-triggerable.
            var gateEntry = new CacheStoreEntryWrite<T>
            {
                Value = entry.Value,
                IsNull = entry.IsNull,
                LogicalExpiresAt = logicalExpiresAt,
                PhysicalExpiresAt = physicalExpiresAt,
                SlidingExpiration = entry.SlidingExpiration,
                EagerRefreshAt = null,
                ETag = entry.ETag,
                LastModifiedAt = entry.LastModifiedAt,
                Tags = entry.Tags,
                ExpectedConcurrencyStamp = entry.ConcurrencyStamp,
                // Gate write is a restamp of the still-fresh entry — preserve its original birth time.
                CreatedAt = entry.CreatedAt,
                IsRestamp = true,
            };

            try
            {
                var gateCommitted = await store
                    .SetEntryAsync(key, in gateEntry, CancellationToken.None)
                    .ConfigureAwait(false);

                if (!gateCommitted)
                {
                    return;
                }
            }
            catch (Exception exception)
            {
                _logger.LogEagerRefreshSkipped(exception, key);
                return;
            }

            // Re-read the just-committed gate entry so the final eager write CAS-guards against the post-gate
            // concurrency stamp: a concurrent Remove/Upsert landing during the (potentially long) factory run then
            // fails the final write's CAS instead of being silently clobbered or the removed key resurrected. The
            // gate write carried the original birth time forward, so the re-read's CreatedAt preserves it on the
            // NotModified path. A failed re-read degrades to NotFound (an unconditional write), matching the
            // best-effort tolerance of the rest of this path.
            var postGateEntry = await _TryGetEntryAsync<T>(store, key, CancellationToken.None).ConfigureAwait(false);

            ownsReleaser = false;
            await _StartEagerFactoryAsync(
                    store,
                    key,
                    context,
                    postGateEntry,
                    factory,
                    options,
                    releaser,
                    distributedLease
                )
                .ConfigureAwait(false);
        }
        finally
        {
            if (ownsReleaser)
            {
                if (distributedLease is not null)
                {
                    await _ReleaseFactoryLockAsync(distributedLease, key).ConfigureAwait(false);
                }

                releaser.Dispose();
                BackgroundOperationFinished?.Invoke();
            }
        }
    }

    private async Task _StartEagerFactoryAsync<T>(
        IFactoryCacheStore store,
        string key,
        CacheFactoryContext<T> context,
        CacheStoreEntry<T> sourceEntry,
        Func<CacheFactoryContext<T>, CancellationToken, ValueTask<CacheFactoryResult<T>>> factory,
        CacheEntryOptions options,
        IDisposable releaser,
        IAsyncDisposable? distributedLease
    )
    {
#pragma warning disable CA2000 // Ownership transfers to _CompleteEagerRefreshAsync, which disposes it in its finally.
        var internalCts = new CancellationTokenSource();
#pragma warning restore CA2000
        var factoryTask = _RunDetachedFactoryAsync(token => factory(context, token), internalCts.Token);
        await _CompleteEagerRefreshAsync(
                store,
                key,
                context,
                sourceEntry,
                factoryTask,
                internalCts,
                options,
                releaser,
                distributedLease
            )
            .ConfigureAwait(false);
    }

    private static async Task<T?> _RunDetachedFactoryAsync<T>(
        Func<CancellationToken, ValueTask<T?>> factory,
        CancellationToken cancellationToken
    )
    {
        await Task.Yield();
        return await factory(cancellationToken).ConfigureAwait(false);
    }

    private async Task _CompleteEagerRefreshAsync<T>(
        IFactoryCacheStore store,
        string key,
        CacheFactoryContext<T> context,
        CacheStoreEntry<T> sourceEntry,
        Task<CacheFactoryResult<T>> factoryTask,
        CancellationTokenSource internalCts,
        CacheEntryOptions options,
        IDisposable releaser,
        IAsyncDisposable? distributedLease
    )
    {
#pragma warning disable VSTHRD003 // This continuation intentionally races/observes the detached factory task.
        var ctsTransferred = false;
        try
        {
            // Race the detached factory against the ceiling. Returns true only when the ceiling fired and the
            // factory was abandoned (CTS deferred-disposed), in which case the finally must skip CTS disposal.
            // Unlike the soft-timeout path there is no ceiling-fired consequence here: the entry is still fresh
            // and rides to its natural expiry, so nothing runs after a true return.
            ctsTransferred = await _RunCeilingRaceAsync(
                    factoryTask,
                    internalCts,
                    options,
                    key,
                    ceilingLabel: "eager-ceiling",
                    // The observe lambda runs inside the awaited race; the finally below disposes internalCts only
                    // when ctsTransferred is false (the factory was observed to completion), so the closure never
                    // touches a disposed CTS.
                    // ReSharper disable once AccessToDisposedClosure
                    () => _ObserveEagerFactoryAsync(store, key, context, sourceEntry, factoryTask, internalCts)
                )
                .ConfigureAwait(false);
        }
#pragma warning restore VSTHRD003
        finally
        {
            if (!ctsTransferred)
            {
                internalCts.Dispose();
            }

            if (distributedLease is not null)
            {
                await _ReleaseFactoryLockAsync(distributedLease, key).ConfigureAwait(false);
            }

            releaser.Dispose();
            BackgroundOperationFinished?.Invoke();
        }
    }

    private async Task _ObserveEagerFactoryAsync<T>(
        IFactoryCacheStore store,
        string key,
        CacheFactoryContext<T> context,
        CacheStoreEntry<T> sourceEntry,
        Task<CacheFactoryResult<T>> factoryTask,
        CancellationTokenSource internalCts
    )
    {
#pragma warning disable VSTHRD003 // This continuation deliberately observes the detached eager factory task.
        try
        {
            var result = await factoryTask.ConfigureAwait(false);

            if (!internalCts.IsCancellationRequested)
            {
                await _WriteFactoryResultAsync(
                        store,
                        key,
                        context,
                        result,
                        sourceEntry: sourceEntry,
                        CancellationToken.None
                    )
                    .ConfigureAwait(false);
                _logger.LogEagerRefreshSucceeded(key);
            }
        }
        // Genuine failures only. When internalCts fired this OperationCanceledException is OUR deliberate ceiling
        // abandonment (the ceiling-fired path already logged eager-ceiling), not a refresh failure, so the filter
        // excludes it from the failure log.
        catch (Exception exception)
            when (exception is not OperationCanceledException || !internalCts.IsCancellationRequested)
        {
            // The entry is still fresh; failure only means the proactive refresh is lost. Natural expiry and
            // fail-safe (when enabled) take over from here, so log and move on without touching the entry.
            _logger.LogEagerRefreshFailed(exception, key, exception.GetType().Name);
        }
#pragma warning restore VSTHRD003
    }

    private async ValueTask _TryRearmSlidingEntryAsync<T>(
        IFactoryCacheStore store,
        string key,
        CacheStoreEntry<T> entry,
        DateTime now
    )
    {
        if (
            entry.SlidingExpiration is not { } slidingExpiration
            || entry.PhysicalExpiresAt is not { } physicalExpiresAt
        )
        {
            return;
        }

        // Nothing left to extend once the physical cap has passed.
        if (physicalExpiresAt <= now)
        {
            return;
        }

        try
        {
            // Delegate to the provider's metadata-only, throttled primitive instead of rewriting the whole value.
            // The throttle and the "only extend" / physical-cap rules live in the store, which is the only layer
            // that cheaply knows the entry's true remaining lifetime (Redis key TTL / in-memory logical deadline);
            // after a metadata-only re-arm the coordinator's embedded logical is no longer authoritative, so the
            // decision cannot be made here. CancellationToken.None: a caller cancellation must not abort the
            // best-effort re-arm of a value read that already succeeded.
            await store
                .TryRearmSlidingAsync(key, slidingExpiration, physicalExpiresAt, now, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogCoordinatorSlidingExpirationRearmFailed(exception, key);
        }
    }

    // Best-effort release of the distributed factory lock: a failed release must never mask the operation's
    // outcome (the lease's own TTL is the backstop), so swallow and log. Mirrors the other best-effort cleanups.
    private async ValueTask _ReleaseFactoryLockAsync(IAsyncDisposable distributedLease, string key)
    {
        try
        {
            await distributedLease.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogCacheFactoryLockReleaseFailed(exception, key);
        }
    }
}
