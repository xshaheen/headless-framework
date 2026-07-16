// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Caching;

// Soft-timeout background completion: when the foreground factory soft-times out the per-key lock (and any
// distributed lease) transfers here so the detached factory can land its value, bounded by BackgroundFactoryCeiling.
public sealed partial class FactoryCacheCoordinator
{
    private void _StartBackgroundCompletion<T>(
        IFactoryCacheStore store,
        string key,
        CacheFactoryContext<T> context,
        Task<CacheFactoryResult<T>> factoryTask,
        CancellationTokenSource internalCts,
        CacheStoreEntry<T> staleCandidate,
        CacheEntryOptions options,
        IDisposable releaser,
        IAsyncDisposable? distributedLease
    )
    {
        var backgroundTask = _CompleteFactoryInBackgroundAsync(
            store,
            key,
            context,
            factoryTask,
            internalCts,
            staleCandidate,
            options,
            releaser,
            distributedLease
        );

        _ObserveFaultedTask(backgroundTask, key);
    }

    private async Task _CompleteFactoryInBackgroundAsync<T>(
        IFactoryCacheStore store,
        string key,
        CacheFactoryContext<T> context,
        Task<CacheFactoryResult<T>> factoryTask,
        CancellationTokenSource internalCts,
        CacheStoreEntry<T> staleCandidate,
        CacheEntryOptions options,
        IDisposable releaser,
        IAsyncDisposable? distributedLease
    )
    {
#pragma warning disable VSTHRD003 // This continuation intentionally races/observes the transferred factory task.
        var ctsTransferred = false;
        try
        {
            // Race the detached factory against the ceiling. Returns true only when the ceiling fired and the
            // factory was abandoned (CTS deferred-disposed), in which case the finally must skip CTS disposal.
            ctsTransferred = await _RunCeilingRaceAsync(
                    factoryTask,
                    internalCts,
                    options,
                    key,
                    ceilingLabel: "background-ceiling",
                    // The observe lambda runs inside the awaited race; the finally below disposes internalCts only
                    // when ctsTransferred is false (i.e. the factory was observed to completion, not abandoned), so
                    // the closure never touches a disposed CTS.
                    // ReSharper disable once AccessToDisposedClosure
                    () =>
                        _ObserveBackgroundFactoryAsync(
                            store,
                            key,
                            context,
                            factoryTask,
                            internalCts,
                            staleCandidate,
                            options
                        )
                )
                .ConfigureAwait(false);

            if (ctsTransferred)
            {
                // Ceiling-fired consequence (background only): restamp the stale reserve's throttle window so the
                // abandoned factory's elapse does not let callers immediately re-hammer it.
                await _TryRestampStaleWithCeilingAsync(store, key, staleCandidate, options).ConfigureAwait(false);
            }
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

    // Shared ceiling race for the two detached-completion paths (soft-timeout background completion and eager
    // refresh). Runs the WhenAny(factory, ceiling) state machine and the abandon+DisposeAfter bookkeeping so both
    // callers stay bit-for-bit identical on the CTS-lifetime-sensitive path. Returns whether the ceiling fired and
    // the CTS was transferred to deferred disposal (the caller's finally must then skip its own CTS disposal).
    // Callers differ only in the observe action (passed as observeFactory) and the ceiling-fired consequence (run
    // by the caller after this returns true) and the ceilingLabel ("background-ceiling" vs "eager-ceiling").
    private async Task<bool> _RunCeilingRaceAsync<T>(
        Task<CacheFactoryResult<T>> factoryTask,
        CancellationTokenSource internalCts,
        CacheEntryOptions options,
        string key,
        string ceilingLabel,
        Func<Task> observeFactory
    )
    {
#pragma warning disable VSTHRD003 // This continuation intentionally races/observes the transferred factory task.
        // No ceiling configured: let the detached factory run to completion, matching comparable caches.
        if (options.BackgroundFactoryCeiling == Timeout.InfiniteTimeSpan)
        {
            await observeFactory().ConfigureAwait(false);
            return false;
        }

        using var ceilingCts = new CancellationTokenSource();
        var ceilingTask = Task.Delay(options.BackgroundFactoryCeiling, _timeProvider, ceilingCts.Token);
        BackgroundCompletionCeilingTimerRegistered?.Invoke();

        var winner = await Task.WhenAny(factoryTask, ceilingTask).ConfigureAwait(false);

        if (winner == factoryTask)
        {
            await ceilingCts.CancelAsync().ConfigureAwait(false);
            await observeFactory().ConfigureAwait(false);
            return false;
        }

        await internalCts.CancelAsync().ConfigureAwait(false);
        // The ceiling fired but the factory may ignore cancellation and keep running. Observe its task so a
        // later fault is logged rather than lost, mirroring the hard-timeout abandonment path.
        _ObserveFaultedTask(factoryTask, key);
        // Defer disposal until the abandoned factory finishes so it never touches a disposed token source;
        // the returned true makes the caller's synchronous finally skip disposal, mirroring the hard-timeout path.
        CacheDetachedTask.DisposeAfter(internalCts, factoryTask);
        _logger.LogCacheFactoryTimedOut(key, ceilingLabel, options.BackgroundFactoryCeiling);
        return true;
#pragma warning restore VSTHRD003
    }

    private async Task _ObserveBackgroundFactoryAsync<T>(
        IFactoryCacheStore store,
        string key,
        CacheFactoryContext<T> context,
        Task<CacheFactoryResult<T>> factoryTask,
        CancellationTokenSource internalCts,
        CacheStoreEntry<T> staleCandidate,
        CacheEntryOptions options
    )
    {
#pragma warning disable VSTHRD003 // This continuation deliberately observes the transferred background factory task.
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
                        sourceEntry: staleCandidate,
                        CancellationToken.None
                    )
                    .ConfigureAwait(false);
                _logger.LogCacheBackgroundCompletionSucceeded(key);
                CachingMetrics.RecordRefresh(
                    _cacheName,
                    CachingMetrics.RefreshBackground,
                    CachingMetrics.OutcomeSuccess
                );
            }
        }
        // Genuine failures only. When internalCts fired this OperationCanceledException is OUR deliberate ceiling
        // abandonment (the ceiling-fired path already logged background-ceiling), not a factory failure, so it must
        // not be logged as a failure nor trigger the restamp-on-failure path; the filter excludes that case.
        catch (Exception exception)
            when (exception is not OperationCanceledException || !internalCts.IsCancellationRequested)
        {
            _logger.LogCacheBackgroundCompletionFailed(exception, key, exception.GetType().Name);
            CachingMetrics.RecordRefresh(_cacheName, CachingMetrics.RefreshBackground, CachingMetrics.OutcomeError);
            await _TryRestampStaleWithCeilingAsync(store, key, staleCandidate, options).ConfigureAwait(false);
        }
#pragma warning restore VSTHRD003
    }

    private async ValueTask _TryRestampStaleWithCeilingAsync<T>(
        IFactoryCacheStore store,
        string key,
        CacheStoreEntry<T> staleCandidate,
        CacheEntryOptions options
    )
    {
        if (options.BackgroundFactoryCeiling == Timeout.InfiniteTimeSpan)
        {
            await _TryRestampStaleAsync(store, key, staleCandidate, options, _GetUtcNow(), CancellationToken.None)
                .ConfigureAwait(false);
            return;
        }

        // The restamp store write is bounded by its own ceiling so a hung store cannot hold the per-key lock
        // indefinitely; worst-case background lock-hold is therefore ~2x ceiling (factory race + restamp). The
        // restamp shares the ceiling token so the ceiling can cancel the in-flight write.
        using var ceilingCts = new CancellationTokenSource();
        var restampTask = _TryRestampStaleAsync(store, key, staleCandidate, options, _GetUtcNow(), ceilingCts.Token)
            .AsTask();
        var ceilingTask = Task.Delay(options.BackgroundFactoryCeiling, _timeProvider, ceilingCts.Token);
        _ = await Task.WhenAny(restampTask, ceilingTask).ConfigureAwait(false);

        // Cancel the loser and ALWAYS await the restamp before returning. When the ceiling wins, cancellation
        // stops the in-flight store write and awaiting it keeps the per-key lock held until it unwinds, so an
        // orphaned restamp can never land after the lock is released and clobber a concurrent caller's fresh
        // value. _TryRestampStaleAsync swallows the resulting cancellation, so this await never throws.
        await ceilingCts.CancelAsync().ConfigureAwait(false);
        await restampTask.ConfigureAwait(false);
    }

    private async ValueTask _TryRestampStaleAsync<T>(
        IFactoryCacheStore store,
        string key,
        CacheStoreEntry<T> staleCandidate,
        CacheEntryOptions options,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        // staleCandidate always carries a physical expiration: _IsStaleCandidate (the only gate that assigns a
        // stale candidate) requires PhysicalExpiresAt.HasValue, so the throttle restamp can always be written.
        var physicalExpiresAt = staleCandidate.PhysicalExpiresAt!.Value;
        var logicalExpiresAt = _Min(now.Add(options.FailSafeThrottleDuration), physicalExpiresAt);

        // Preserve the stale entry's metadata across the throttle restamp (ETag/LastModifiedAt/Tags), but drop
        // EagerRefreshAt: a restamped stale reserve must not trigger an eager refresh on top of the throttle.
        var restampEntry = new CacheStoreEntryWrite<T>
        {
            Value = staleCandidate.Value,
            IsNull = staleCandidate.IsNull,
            LogicalExpiresAt = logicalExpiresAt,
            PhysicalExpiresAt = physicalExpiresAt,
            SlidingExpiration = null,
            EagerRefreshAt = null,
            ETag = staleCandidate.ETag,
            LastModifiedAt = staleCandidate.LastModifiedAt,
            Tags = staleCandidate.Tags,
            ExpectedConcurrencyStamp = staleCandidate.ConcurrencyStamp,
            // Throttle restamp preserves the reserve's original birth time (not a new write).
            CreatedAt = staleCandidate.CreatedAt,
            IsRestamp = true,
        };

        try
        {
            // Caller-facing restamps pass CancellationToken.None (a caller cancellation between the factory
            // throw and this await must not abort the stale return); the ceiling-bounded background restamp
            // passes the ceiling token so a hung store write can be cancelled instead of orphaning it.
            _ = await store.SetEntryAsync(key, in restampEntry, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // Swallow all exceptions (including cancellation): the stale value must always be returned.
            _logger.LogFailSafeRestampFailed(exception, key);
        }
    }
}
