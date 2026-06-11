// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Headless.Checks;
using Headless.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Caching;

/// <summary>Coordinates factory-backed cache operations across cache providers.</summary>
[PublicAPI]
public sealed class FactoryCacheCoordinator(
    TimeProvider timeProvider,
    ILogger? logger = null,
    ICacheFactoryLockProvider? factoryLockProvider = null
) : IDisposable
{
    private readonly TimeProvider _timeProvider = Argument.IsNotNull(timeProvider);
    private readonly KeyedAsyncLock _keyedLock = new();
    private readonly ILogger _logger = logger ?? NullLogger<FactoryCacheCoordinator>.Instance;
    private readonly ICacheFactoryLockProvider? _factoryLockProvider = factoryLockProvider;
    private const int _MaxInertWarningKeys = 1024;
    private readonly ConcurrentDictionary<string, byte> _softTimeoutInertWarnings = new(StringComparer.Ordinal);

    /// <summary>Signals test code that a detached background completion has registered its ceiling timer.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal Action? BackgroundCompletionCeilingTimerRegistered { get; set; }

    /// <summary>Signals test code that a foreground factory timeout timer has been registered.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal Action? FactoryTimeoutTimerRegistered { get; set; }

    /// <summary>Signals test code that a detached background completion has finished or abandoned its factory.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal Action? BackgroundCompletionFinished { get; set; }

    /// <summary>Gets or creates a cache value by using the provider store primitive.</summary>
    /// <remarks>
    /// This overload adapts the simple value factory onto the conditional-factory engine
    /// (<see cref="GetOrAddAsync{T}(IFactoryCacheStore, string, Func{CacheFactoryContext{T}, CancellationToken, ValueTask{CacheFactoryResult{T}}}, CacheEntryOptions, CancellationToken)"/>),
    /// so both overloads share one state machine with identical timeout, fail-safe, and refresh semantics.
    /// </remarks>
    /// <typeparam name="T">The cached value type.</typeparam>
    /// <param name="store">The provider store.</param>
    /// <param name="key">The cache key.</param>
    /// <param name="factory">The value factory.</param>
    /// <param name="options">The cache entry options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public ValueTask<CacheValue<T>> GetOrAddAsync<T>(
        IFactoryCacheStore store,
        string key,
        Func<CancellationToken, ValueTask<T?>> factory,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(factory);

        return GetOrAddAsync<T>(
            store,
            key,
            async (context, token) => context.Modified(await factory(token).ConfigureAwait(false)),
            options,
            cancellationToken
        );
    }

    /// <summary>
    /// Gets or refreshes a cache value using a conditional factory (the HTTP-304 pattern). The factory receives a
    /// <see cref="CacheFactoryContext{T}"/> carrying the last-known cached value and its validators and returns
    /// <see cref="CacheFactoryContext{T}.NotModified"/> to re-stamp the existing entry as fresh, or
    /// <see cref="CacheFactoryContext{T}.Modified(T, string?, DateTime?)"/> to replace it. The factory may also
    /// replace <see cref="CacheFactoryContext{T}.Options"/> before returning (adaptive caching); the replacement
    /// is re-validated before the write and an invalid mutation throws after the factory has run.
    /// </summary>
    /// <typeparam name="T">The cached value type.</typeparam>
    /// <param name="store">The provider store.</param>
    /// <param name="key">The cache key.</param>
    /// <param name="factory">The conditional value factory; receives the per-execution context and the cancellation token.</param>
    /// <param name="options">The cache entry options; the factory may replace them via the context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask<CacheValue<T>> GetOrAddAsync<T>(
        IFactoryCacheStore store,
        string key,
        Func<CacheFactoryContext<T>, CancellationToken, ValueTask<CacheFactoryResult<T>>> factory,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(store);
        Argument.IsNotNullOrEmpty(key);
        Argument.IsNotNull(factory);
        _ValidateOptions(options);

        if (options.UseDistributedFactoryLock && _factoryLockProvider is null)
        {
            throw new InvalidOperationException(
                $"{nameof(CacheEntryOptions)}.{nameof(CacheEntryOptions.UseDistributedFactoryLock)} is enabled for "
                    + $"cache key '{key}' but no {nameof(ICacheFactoryLockProvider)} is registered. Reference the "
                    + "Headless.Caching.DistributedLocks adapter package and call "
                    + "services.AddCachingDistributedFactoryLock(), or disable the option."
            );
        }

        cancellationToken.ThrowIfCancellationRequested();

        var entry = await _TryGetEntryAsync<T>(store, key, cancellationToken).ConfigureAwait(false);
        var now = _GetUtcNow();

        if (entry.IsFresh(now))
        {
            await _TryRearmSlidingEntryAsync(store, key, entry, now).ConfigureAwait(false);
            _MaybeStartEagerRefresh(store, key, factory, options, entry, now);
            return _ToCacheValue(entry, isStale: false);
        }

        var staleCandidate = _IsStaleCandidate(entry, now) ? entry : CacheStoreEntry<T>.NotFound;
        var lockTimeout = _SelectLockTimeout(options, staleCandidate, now);
        var releaser = await _keyedLock
            .LockAsync(key, lockTimeout, _timeProvider, cancellationToken)
            .ConfigureAwait(false);

        if (releaser is null)
        {
            // With a stale reserve the wait is the soft timeout and we serve stale; without one it is LockTimeout
            // and _ToCacheValue(NotFound) degrades to a miss (NoValue).
            _logger.LogCacheFactoryTimedOut(key, staleCandidate.Found ? "lock-soft" : "lock-timeout", lockTimeout);
            return _ToCacheValue(staleCandidate, isStale: true);
        }

        var ownsReleaser = true;
        IAsyncDisposable? distributedLease = null;

        try
        {
            entry = await _TryGetEntryAsync<T>(store, key, cancellationToken).ConfigureAwait(false);
            now = _GetUtcNow();

            if (entry.IsFresh(now))
            {
                await _TryRearmSlidingEntryAsync(store, key, entry, now).ConfigureAwait(false);
                return _ToCacheValue(entry, isStale: false);
            }

            if (_IsStaleCandidate(entry, now))
            {
                staleCandidate = entry;
            }

            if (options.UseDistributedFactoryLock)
            {
                // Cross-node single-flight: acquire the distributed lock with the same wait budget the local lock
                // used (soft timeout when a fail-safe stale reserve can absorb the elapse, LockTimeout otherwise),
                // so the degradation semantics on elapse mirror the local lock-timeout path exactly.
                var distributedLockTimeout = _SelectLockTimeout(options, staleCandidate, now);

                try
                {
                    distributedLease = await _factoryLockProvider!
                        .TryAcquireAsync(key, distributedLockTimeout, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception exception) when (!IsCallerCancellation(exception, cancellationToken))
                {
                    // A throwing acquire means the lock backend is down (vs null = held elsewhere). Stale beats
                    // failure: with fail-safe and a usable stale reserve, serve it without running the factory and
                    // restamp the throttle so per-call retries stop hammering the down backend. With no usable
                    // reserve the provider's failure propagates, mirroring the factory-throw fail-safe path.
                    now = _GetUtcNow();

                    if (!options.IsFailSafeEnabled || !_IsStaleCandidate(staleCandidate, now))
                    {
                        throw;
                    }

                    await _TryRestampStaleAsync(store, key, staleCandidate, options, now, CancellationToken.None)
                        .ConfigureAwait(false);

                    _logger.LogCacheFactoryLockAcquireFailed(exception, key, exception.GetType().Name);
                    return _ToCacheValue(staleCandidate, isStale: true);
                }

                if (distributedLease is null)
                {
                    _logger.LogCacheFactoryTimedOut(
                        key,
                        staleCandidate.Found ? "distributed-lock-soft" : "distributed-lock-timeout",
                        distributedLockTimeout
                    );

                    return _ToCacheValue(staleCandidate, isStale: true);
                }

                // The previous lock owner on another node may have just written a fresh value to the shared store;
                // re-check before running the factory so the loser of the cross-node race serves the winner's value.
                entry = await _TryGetEntryAsync<T>(store, key, cancellationToken).ConfigureAwait(false);
                now = _GetUtcNow();

                if (entry.IsFresh(now))
                {
                    await _TryRearmSlidingEntryAsync(store, key, entry, now).ConfigureAwait(false);
                    return _ToCacheValue(entry, isStale: false);
                }

                if (_IsStaleCandidate(entry, now))
                {
                    staleCandidate = entry;
                }
            }

            // One context per factory execution, built from the current physically-present entry so the factory
            // can see the last-known-good value and its validators (conditional refresh) and mutate the options
            // and tags it will be written with (adaptive caching).
            var context = _CreateFactoryContext(key, entry, options, now);
            var boundFactory = (CancellationToken token) => factory(context, token);

            var timeoutSelection = _SelectFactoryTimeout(options, staleCandidate, now, key);
            FactoryRunResult<CacheFactoryResult<T>> factoryResult;

            try
            {
                factoryResult = await _RunFactoryWithTimeoutAsync(
                        boundFactory,
                        key,
                        timeoutSelection.Timeout,
                        cancelOnTimeout: timeoutSelection.Kind == FactoryTimeoutKind.Hard,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (!IsCallerCancellation(exception, cancellationToken))
            {
                now = _GetUtcNow();

                if (!options.IsFailSafeEnabled || !_IsStaleCandidate(staleCandidate, now))
                {
                    throw;
                }

                await _TryRestampStaleAsync(store, key, staleCandidate, options, now, CancellationToken.None)
                    .ConfigureAwait(false);

                _logger.LogFailSafeActivated(key, exception.GetType().Name);
                return _ToCacheValue(staleCandidate, isStale: true);
            }

            if (factoryResult.IsTimedOut)
            {
                _logger.LogCacheFactoryTimedOut(
                    key,
                    _TimeoutKindLabel(timeoutSelection.Kind),
                    timeoutSelection.Timeout
                );

                if (factoryResult.IsSoftTimeout)
                {
                    // The distributed lease (when present) transfers into the background completion together with
                    // the local lock releaser, so the cross-node guard stays held until the detached factory lands.
                    _StartBackgroundCompletion(
                        store,
                        key,
                        context,
                        factoryResult.RunningTask,
                        factoryResult.InternalCancellationTokenSource,
                        staleCandidate,
                        options,
                        releaser,
                        distributedLease
                    );

                    ownsReleaser = false;
                    return _ToCacheValue(staleCandidate, isStale: true);
                }

                _ObserveFaultedTask(factoryResult.RunningTask, key);

                now = _GetUtcNow();

                if (_IsStaleCandidate(staleCandidate, now))
                {
                    await _TryRestampStaleAsync(store, key, staleCandidate, options, now, CancellationToken.None)
                        .ConfigureAwait(false);

                    _logger.LogFailSafeActivated(key, nameof(CacheFactoryTimeoutException));
                    return _ToCacheValue(staleCandidate, isStale: true);
                }

                throw new CacheFactoryTimeoutException(key, timeoutSelection.Timeout);
            }

            return await _WriteFactoryResultAsync(
                    store,
                    key,
                    context,
                    factoryResult.Value,
                    previousTags: entry.Tags,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        finally
        {
            if (ownsReleaser)
            {
                // Release in reverse acquisition order: free the distributed lease (so other nodes can proceed)
                // before the local per-key lock. Covers every foreground completion path — fresh write, fail-safe
                // return, hard timeout, and exceptions; the soft-timeout path transfers ownership instead.
                if (distributedLease is not null)
                {
                    await _ReleaseFactoryLockAsync(distributedLease, key).ConfigureAwait(false);
                }

                releaser.Dispose();
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _keyedLock.Dispose();
    }

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
        try
        {
            // No ceiling configured: let the detached factory run to completion, matching comparable caches.
            if (options.BackgroundFactoryCeiling == Timeout.InfiniteTimeSpan)
            {
                await _ObserveBackgroundFactoryAsync(
                        store,
                        key,
                        context,
                        factoryTask,
                        internalCts,
                        staleCandidate,
                        options
                    )
                    .ConfigureAwait(false);

                return;
            }

            using var ceilingCts = new CancellationTokenSource();
            var ceilingTask = Task.Delay(options.BackgroundFactoryCeiling, _timeProvider, ceilingCts.Token);
            BackgroundCompletionCeilingTimerRegistered?.Invoke();

            var winner = await Task.WhenAny(factoryTask, ceilingTask).ConfigureAwait(false);

            if (winner == factoryTask)
            {
                await ceilingCts.CancelAsync().ConfigureAwait(false);
                await _ObserveBackgroundFactoryAsync(
                        store,
                        key,
                        context,
                        factoryTask,
                        internalCts,
                        staleCandidate,
                        options
                    )
                    .ConfigureAwait(false);

                return;
            }

            await internalCts.CancelAsync().ConfigureAwait(false);
            // The ceiling fired but the factory may ignore cancellation and keep running. Observe its task so a
            // later fault is logged rather than lost, mirroring the hard-timeout abandonment path.
            _ObserveFaultedTask(factoryTask, key);
            _logger.LogCacheFactoryTimedOut(key, "background-ceiling", options.BackgroundFactoryCeiling);
            await _TryRestampStaleWithCeilingAsync(store, key, staleCandidate, options).ConfigureAwait(false);
        }
#pragma warning restore VSTHRD003
        finally
        {
            internalCts.Dispose();

            if (distributedLease is not null)
            {
                await _ReleaseFactoryLockAsync(distributedLease, key).ConfigureAwait(false);
            }

            releaser.Dispose();
            BackgroundCompletionFinished?.Invoke();
        }
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
                        previousTags: staleCandidate.Tags,
                        CancellationToken.None
                    )
                    .ConfigureAwait(false);
                _logger.LogCacheBackgroundCompletionSucceeded(key);
            }
        }
        catch (Exception exception)
        {
            _logger.LogCacheBackgroundCompletionFailed(exception, key, exception.GetType().Name);
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
            IsRestamp = true,
        };

        try
        {
            // Caller-facing restamps pass CancellationToken.None (a caller cancellation between the factory
            // throw and this await must not abort the stale return); the ceiling-bounded background restamp
            // passes the ceiling token so a hung store write can be cancelled instead of orphaning it.
            await store.SetEntryAsync(key, in restampEntry, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // Swallow all exceptions (including cancellation): the stale value must always be returned.
            _logger.LogFailSafeRestampFailed(exception, key);
        }
    }

    // Builds the per-execution factory context from the current physically-present entry: the last-known-good
    // value (including a cached null) plus its validators and tags. When no physical entry exists the context is
    // cold (HasStaleValue false, NoValue, null validators).
    private static CacheFactoryContext<T> _CreateFactoryContext<T>(
        string key,
        CacheStoreEntry<T> entry,
        CacheEntryOptions options,
        DateTime now
    )
    {
        if (!entry.IsPhysicallyPresent(now))
        {
            return new CacheFactoryContext<T>(CacheValue<T>.NoValue)
            {
                Key = key,
                Options = options,
                Tags = options.Tags,
            };
        }

        var staleValue = entry.IsNull
            ? new CacheValue<T>(default, hasValue: true)
            : new CacheValue<T>(entry.Value, hasValue: true);

        return new CacheFactoryContext<T>(staleValue)
        {
            Key = key,
            ETag = entry.ETag,
            LastModifiedAt = entry.LastModifiedAt,
            Options = options,
            // Call-provided tags win; otherwise carry the existing entry's tags forward.
            Tags = options.Tags ?? entry.Tags,
        };
    }

    // Persists a factory result as a fresh entry and returns the caller-facing value. Shared by the foreground
    // path, soft-timeout background completion, and eager refresh so conditional refresh (NotModified) and
    // adaptive options behave identically everywhere. A store-write failure on this path must propagate rather
    // than activate fail-safe (which would discard the fresh result).
    private async ValueTask<CacheValue<T>> _WriteFactoryResultAsync<T>(
        IFactoryCacheStore store,
        string key,
        CacheFactoryContext<T> context,
        CacheFactoryResult<T> result,
        IReadOnlyCollection<string>? previousTags,
        CancellationToken cancellationToken
    )
    {
        // Adaptive caching: the factory may have replaced the context's options. Re-validate before writing so an
        // invalid adaptive mutation throws (after the factory ran) instead of persisting a malformed entry.
        var options = context.Options;
        _ValidateOptions(options);
        // Factory-mutated context tags bypass options validation; hold them to the same envelope limits.
        CacheEntryStamps.ValidateTags(context.Tags, paramName: "context.Tags");

        T? value;
        string? eTag;
        DateTime? lastModifiedAt;

        if (result.IsNotModified)
        {
            if (!context.HasStaleValue)
            {
                // Unreachable through CacheFactoryContext<T>.NotModified() (which throws first); guards a
                // hand-constructed result so a cold miss can never silently cache a default value.
                throw new InvalidOperationException(
                    $"Cannot apply a NotModified result for cache key '{key}': no cached value exists to extend."
                );
            }

            // Conditional refresh: re-stamp the existing last-known-good value as fresh, preserving its
            // validators. A NotModified result's own validators are ignored.
            value = context.StaleValue.Value;
            eTag = context.ETag;
            lastModifiedAt = context.LastModifiedAt;
        }
        else
        {
            value = result.Value;
            eTag = result.ETag;
            lastModifiedAt = result.LastModifiedAt;
        }

        var now = _GetUtcNow();
        var stamps = CacheEntryStamps.Compute(options, now);

        var entry = new CacheStoreEntryWrite<T>
        {
            Value = value,
            IsNull = value is null,
            LogicalExpiresAt = stamps.LogicalExpiresAt,
            PhysicalExpiresAt = stamps.PhysicalExpiresAt,
            SlidingExpiration = options.SlidingExpiration,
            EagerRefreshAt = stamps.EagerRefreshAt,
            ETag = eTag,
            LastModifiedAt = lastModifiedAt,
            Tags = context.Tags,
            RemovedTags = CacheEntryStamps.ComputeRemovedTags(previousTags, context.Tags),
            // A NotModified extension re-stamps the existing value: peers' cached bytes stay valid, so
            // multi-tier stores must not broadcast an invalidation for it.
            IsRestamp = result.IsNotModified,
        };

        await store.SetEntryAsync(key, in entry, cancellationToken).ConfigureAwait(false);

        return new CacheValue<T>(value, hasValue: true);
    }

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
                IsRestamp = true,
            };

            try
            {
                await store.SetEntryAsync(key, in gateEntry, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogEagerRefreshSkipped(exception, key);
                return;
            }

            ownsReleaser = false;
            await _StartEagerFactoryAsync(store, key, context, factory, options, entry.Tags, releaser, distributedLease)
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
                BackgroundCompletionFinished?.Invoke();
            }
        }
    }

    private async Task _StartEagerFactoryAsync<T>(
        IFactoryCacheStore store,
        string key,
        CacheFactoryContext<T> context,
        Func<CacheFactoryContext<T>, CancellationToken, ValueTask<CacheFactoryResult<T>>> factory,
        CacheEntryOptions options,
        IReadOnlyCollection<string>? previousTags,
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
                factoryTask,
                internalCts,
                options,
                previousTags,
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
        Task<CacheFactoryResult<T>> factoryTask,
        CancellationTokenSource internalCts,
        CacheEntryOptions options,
        IReadOnlyCollection<string>? previousTags,
        IDisposable releaser,
        IAsyncDisposable? distributedLease
    )
    {
#pragma warning disable VSTHRD003 // This continuation intentionally races/observes the detached factory task.
        try
        {
            if (options.BackgroundFactoryCeiling == Timeout.InfiniteTimeSpan)
            {
                await _ObserveEagerFactoryAsync(store, key, context, factoryTask, internalCts, previousTags)
                    .ConfigureAwait(false);
                return;
            }

            using var ceilingCts = new CancellationTokenSource();
            var ceilingTask = Task.Delay(options.BackgroundFactoryCeiling, _timeProvider, ceilingCts.Token);
            BackgroundCompletionCeilingTimerRegistered?.Invoke();

            var winner = await Task.WhenAny(factoryTask, ceilingTask).ConfigureAwait(false);

            if (winner == factoryTask)
            {
                await ceilingCts.CancelAsync().ConfigureAwait(false);
                await _ObserveEagerFactoryAsync(store, key, context, factoryTask, internalCts, previousTags)
                    .ConfigureAwait(false);
                return;
            }

            await internalCts.CancelAsync().ConfigureAwait(false);
            // The ceiling fired but the factory may ignore cancellation and keep running. Observe its task so a
            // later fault is logged rather than lost. Unlike the soft-timeout path there is no restamp: the
            // entry is still fresh and rides to its natural expiry.
            _ObserveFaultedTask(factoryTask, key);
            _logger.LogCacheFactoryTimedOut(key, "eager-ceiling", options.BackgroundFactoryCeiling);
        }
#pragma warning restore VSTHRD003
        finally
        {
            internalCts.Dispose();

            if (distributedLease is not null)
            {
                await _ReleaseFactoryLockAsync(distributedLease, key).ConfigureAwait(false);
            }

            releaser.Dispose();
            BackgroundCompletionFinished?.Invoke();
        }
    }

    private async Task _ObserveEagerFactoryAsync<T>(
        IFactoryCacheStore store,
        string key,
        CacheFactoryContext<T> context,
        Task<CacheFactoryResult<T>> factoryTask,
        CancellationTokenSource internalCts,
        IReadOnlyCollection<string>? previousTags
    )
    {
#pragma warning disable VSTHRD003 // This continuation deliberately observes the detached eager factory task.
        try
        {
            var result = await factoryTask.ConfigureAwait(false);

            if (!internalCts.IsCancellationRequested)
            {
                await _WriteFactoryResultAsync(store, key, context, result, previousTags, CancellationToken.None)
                    .ConfigureAwait(false);
                _logger.LogEagerRefreshSucceeded(key);
            }
        }
        catch (Exception exception)
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

    private async ValueTask<CacheStoreEntry<T>> _TryGetEntryAsync<T>(
        IFactoryCacheStore store,
        string key,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await store.TryGetEntryAsync<T>(key, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (!IsCallerCancellation(exception, cancellationToken))
        {
            _logger.LogCacheStoreReadFailed(exception, key);
            return CacheStoreEntry<T>.NotFound;
        }
    }

    private static CacheValue<T> _ToCacheValue<T>(CacheStoreEntry<T> entry, bool isStale)
    {
        if (!entry.Found)
        {
            return CacheValue<T>.NoValue;
        }

        return entry.IsNull
            ? new CacheValue<T>(default, hasValue: true, isStale)
            : new CacheValue<T>(entry.Value, hasValue: true, isStale);
    }

    // A fail-safe stale candidate must carry a non-null physical expiration. A genuine fail-safe reserve
    // always has one (the coordinator writes it); requiring it here closes the throttle hole where a
    // null-physical entry would be served as stale without a throttle write, hammering the factory.
    private static bool _IsStaleCandidate<T>(CacheStoreEntry<T> entry, DateTime now) =>
        entry.IsPhysicallyPresent(now) && entry.PhysicalExpiresAt.HasValue;

    // Soft timeout governs the waiter wait only when a stale reserve can be served on elapse; otherwise the
    // waiter is bounded by LockTimeout (default Timeout.InfiniteTimeSpan), and on elapse with no stale reserve
    // the waiter degrades to a miss. This mirrors FusionCache's GetAppropriateMemoryLockTimeout: a stale + fail-safe
    // + finite-soft caller waits FactorySoftTimeout, every other caller waits the base LockTimeout.
    private static TimeSpan _SelectLockTimeout<T>(
        CacheEntryOptions options,
        CacheStoreEntry<T> staleCandidate,
        DateTime now
    ) =>
        options.IsFailSafeEnabled
        && _IsStaleCandidate(staleCandidate, now)
        && options.FactorySoftTimeout != Timeout.InfiniteTimeSpan
            ? options.FactorySoftTimeout
            : options.LockTimeout;

    private FactoryTimeoutSelection _SelectFactoryTimeout<T>(
        CacheEntryOptions options,
        CacheStoreEntry<T> staleCandidate,
        DateTime now,
        string key
    )
    {
        if (
            options.FactorySoftTimeout != Timeout.InfiniteTimeSpan
            && !options.IsFailSafeEnabled
            // Cap the per-key dedup set so a high-cardinality key space under this misconfiguration cannot grow
            // memory without bound; the Count check before TryAdd may overshoot slightly under concurrency, which
            // is harmless for a warn-once notice.
            && _softTimeoutInertWarnings.Count < _MaxInertWarningKeys
            && _softTimeoutInertWarnings.TryAdd(key, 0)
        )
        {
            _logger.LogCacheSoftTimeoutInert(key, options.FactorySoftTimeout);
        }

        var hasFallback = options.IsFailSafeEnabled && _IsStaleCandidate(staleCandidate, now);

        if (hasFallback && options.FactorySoftTimeout != Timeout.InfiniteTimeSpan)
        {
            return new FactoryTimeoutSelection(FactoryTimeoutKind.Soft, options.FactorySoftTimeout);
        }

        if (options.FactoryHardTimeout != Timeout.InfiniteTimeSpan)
        {
            return new FactoryTimeoutSelection(FactoryTimeoutKind.Hard, options.FactoryHardTimeout);
        }

        return new FactoryTimeoutSelection(FactoryTimeoutKind.None, Timeout.InfiniteTimeSpan);
    }

    private async ValueTask<FactoryRunResult<T>> _RunFactoryWithTimeoutAsync<T>(
        Func<CancellationToken, ValueTask<T?>> factory,
        string key,
        TimeSpan timeout,
        bool cancelOnTimeout,
        CancellationToken cancellationToken
    )
    {
        // No factory timeout configured: skip the delay timer/race machinery. A non-cancellable caller cannot
        // interrupt the factory, so await it directly (the common default-options hot path); allocate nothing.
        if (timeout == Timeout.InfiniteTimeSpan && !cancellationToken.CanBeCanceled)
        {
            var directValue = await factory(cancellationToken).ConfigureAwait(false);
            return FactoryRunResult<T>.Completed(directValue);
        }

        CancellationTokenSource? internalCts = new();
        CancellationTokenRegistration cancellationRegistration = default;

        try
        {
            var factoryTask = factory(internalCts.Token).AsTask();
            Task? callerCancellationTask = null;

            if (cancellationToken.CanBeCanceled)
            {
                var cancellationTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                cancellationRegistration = cancellationToken.Register(
                    static state => ((TaskCompletionSource)state!).TrySetResult(),
                    cancellationTcs
                );
                callerCancellationTask = cancellationTcs.Task;
            }

            if (timeout == Timeout.InfiniteTimeSpan)
            {
                // Cancellable caller, no factory timeout: race the factory against caller cancellation only, with
                // no delay timer. R6 (caller cancellation wins) still holds, even against a non-cooperative factory.
                var untimedWinner = await Task.WhenAny(factoryTask, callerCancellationTask!).ConfigureAwait(false);

                if (untimedWinner == callerCancellationTask)
                {
                    await internalCts.CancelAsync().ConfigureAwait(false);
                    _ObserveFaultedTask(factoryTask, key);
                    // Defer disposal until the abandoned factory finishes so a non-cooperative factory never
                    // touches a disposed token source; null out so the finally does not dispose it under the factory.
                    _DisposeAfter(internalCts, factoryTask);
                    internalCts = null;
                    throw new OperationCanceledException(cancellationToken);
                }

                return FactoryRunResult<T>.Completed(await factoryTask.ConfigureAwait(false));
            }

            using var delayCts = new CancellationTokenSource();
            var delayTask = Task.Delay(timeout, _timeProvider, delayCts.Token);
            FactoryTimeoutTimerRegistered?.Invoke();

            var winner = callerCancellationTask is null
                ? await Task.WhenAny(factoryTask, delayTask).ConfigureAwait(false)
                : await Task.WhenAny(factoryTask, delayTask, callerCancellationTask).ConfigureAwait(false);

            if (winner == factoryTask)
            {
                await delayCts.CancelAsync().ConfigureAwait(false);
                var value = await factoryTask.ConfigureAwait(false);
                return FactoryRunResult<T>.Completed(value);
            }

            if (winner == callerCancellationTask)
            {
                await internalCts.CancelAsync().ConfigureAwait(false);
                // The caller abandoned the wait but a non-cooperative factory may keep running and later fault.
                // Observe its task so the fault is logged rather than lost, mirroring the hard-timeout and
                // background-ceiling abandonment paths.
                _ObserveFaultedTask(factoryTask, key);
                // Defer disposal until the abandoned factory finishes so it never touches a disposed token source;
                // null out so the finally does not dispose it while the factory still holds the token.
                _DisposeAfter(internalCts, factoryTask);
                internalCts = null;
                throw new OperationCanceledException(cancellationToken);
            }

            if (cancelOnTimeout)
            {
                await internalCts.CancelAsync().ConfigureAwait(false);
                // The factory is abandoned but may keep running; the caller observes its fault at the hard-timeout
                // branch. Defer CTS disposal until the factory finishes so it never touches a disposed token source.
                _DisposeAfter(internalCts, factoryTask);
                internalCts = null;
                return FactoryRunResult<T>.TimedOut(factoryTask, internalCancellationTokenSource: null);
            }

            await delayCts.CancelAsync().ConfigureAwait(false);
            var transferredCts = internalCts;
            internalCts = null;
            return FactoryRunResult<T>.TimedOut(factoryTask, transferredCts);
        }
        finally
        {
            await cancellationRegistration.DisposeAsync().ConfigureAwait(false);
            internalCts?.Dispose();
        }
    }

    // Caller cancellation (the caller's own token) must always propagate and never activate fail-safe (KTD-7).
    // Use token identity, not just IsCancellationRequested, so an OperationCanceledException raised by an
    // unrelated linked/internal token (e.g. a downstream timeout) still activates fail-safe.
    /// <summary>
    /// Returns whether <paramref name="exception"/> represents cancellation of the caller's own token, which must
    /// always propagate rather than activate fail-safe (KTD-7). An <see cref="OperationCanceledException"/> raised
    /// by an unrelated linked/internal token (for example a downstream timeout) is NOT caller cancellation and
    /// should activate fail-safe / degrade to a miss. Providers composing this engine (e.g. a hybrid store)
    /// should use this predicate for their best-effort catch filters so cancellation semantics stay consistent.
    /// </summary>
    /// <param name="exception">The exception thrown by the factory or store operation.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    [Pure]
    public static bool IsCallerCancellation(Exception exception, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return true;
        }

        // Identity-match only when the caller supplied a cancellable token. CancellationToken.None compares equal
        // to a token-less OperationCanceledException's token (default == default), which would otherwise wrongly
        // suppress fail-safe for a downstream OCE when the caller passed no token (the default-token call shape).
        return cancellationToken.CanBeCanceled
            && exception is OperationCanceledException operationCanceled
            && operationCanceled.CancellationToken == cancellationToken;
    }

    private DateTime _GetUtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private static DateTime _Min(DateTime left, DateTime right) => left <= right ? left : right;

    // Keep the structured TimeoutKind log value consistent (kebab/lower) with the other LogCacheFactoryTimedOut
    // call sites (lock-soft, lock-timeout, background-ceiling) so log-based monitoring can match on one shape.
    private static string _TimeoutKindLabel(FactoryTimeoutKind kind) =>
        kind switch
        {
            FactoryTimeoutKind.Soft => "soft",
            FactoryTimeoutKind.Hard => "hard",
            _ => "none",
        };

    // Attach a fault-only observer to a detached task (an abandoned factory or a background completion) so its
    // exception is logged rather than lost. Task<T?> upcasts to Task, so both call shapes share this one observer.
    private void _ObserveFaultedTask(Task task, string key)
    {
        _ = task.ContinueWith(
            faulted =>
                _logger.LogCacheBackgroundCompletionFailed(faulted.Exception!, key, faulted.Exception!.GetType().Name),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default
        );
    }

    // Dispose a transferred internal CTS only after the detached factory completes, so a still-running,
    // non-cooperative factory never touches a disposed token source (e.g. via Token.WaitHandle). Cancellation
    // has already signalled the factory; disposal only frees the timer, which can wait until the token is released.
    private static void _DisposeAfter(CancellationTokenSource cts, Task task)
    {
        _ = task.ContinueWith(
            static (_, state) => ((CancellationTokenSource)state!).Dispose(),
            cts,
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default
        );
    }

    // The validation rules live in CacheEntryStamps so the coordinator and the providers' direct
    // options-based upserts always agree; keep this thin wrapper for call-site brevity.
    private static void _ValidateOptions(CacheEntryOptions options) => CacheEntryStamps.ValidateOptions(options);

    private enum FactoryTimeoutKind
    {
        None,
        Soft,
        Hard,
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct FactoryTimeoutSelection(FactoryTimeoutKind Kind, TimeSpan Timeout);

    [StructLayout(LayoutKind.Auto)]
    private readonly struct FactoryRunResult<T>
    {
        private FactoryRunResult(
            T? value,
            Task<T?>? runningTask,
            CancellationTokenSource? internalCancellationTokenSource
        )
        {
            Value = value;
            RunningTask = runningTask;
            InternalCancellationTokenSource = internalCancellationTokenSource;
        }

        public T? Value { get; }

        public Task<T?>? RunningTask { get; }

        public CancellationTokenSource? InternalCancellationTokenSource { get; }

        [MemberNotNullWhen(true, nameof(RunningTask))]
        public bool IsTimedOut => RunningTask is not null;

        // A soft timeout transfers the internal CTS to the caller so the factory can complete in the background;
        // a hard timeout cancels and discards it (null). The CTS presence is therefore the soft/hard discriminant.
        [MemberNotNullWhen(true, nameof(RunningTask), nameof(InternalCancellationTokenSource))]
        public bool IsSoftTimeout => InternalCancellationTokenSource is not null;

        public static FactoryRunResult<T> Completed(T? value) =>
            new(value, runningTask: null, internalCancellationTokenSource: null);

        public static FactoryRunResult<T> TimedOut(
            Task<T?> runningTask,
            CancellationTokenSource? internalCancellationTokenSource
        ) => new(value: default, runningTask, internalCancellationTokenSource);
    }
}

internal static partial class FactoryCacheCoordinatorLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "CacheFailSafeActivated",
        Level = LogLevel.Warning,
        Message = "Cache fail-safe activated for key {Key}; serving stale value after factory exception {ExceptionType}."
    )]
    public static partial void LogFailSafeActivated(this ILogger logger, string key, string exceptionType);

    [LoggerMessage(
        EventId = 2,
        EventName = "CacheFailSafeRestampFailed",
        Level = LogLevel.Warning,
        Message = "Cache fail-safe restamp failed for key {Key}; stale value will still be returned, but the "
            + "throttle window was not persisted so the factory may be retried sooner than expected."
    )]
    public static partial void LogFailSafeRestampFailed(this ILogger logger, Exception exception, string key);

    [LoggerMessage(
        EventId = 3,
        EventName = "CacheStoreReadFailed",
        Level = LogLevel.Debug,
        Message = "Cache store read failed for key {Key}; treating it as a cache miss."
    )]
    public static partial void LogCacheStoreReadFailed(this ILogger logger, Exception exception, string key);

    [LoggerMessage(
        EventId = 4,
        EventName = "CacheFactoryTimedOut",
        Level = LogLevel.Warning,
        Message = "Cache factory timeout fired for key {Key}; kind={TimeoutKind}, limit={Timeout}."
    )]
    public static partial void LogCacheFactoryTimedOut(
        this ILogger logger,
        string key,
        string timeoutKind,
        TimeSpan timeout
    );

    [LoggerMessage(
        EventId = 5,
        EventName = "CacheBackgroundCompletionSucceeded",
        Level = LogLevel.Debug,
        Message = "Cache background completion succeeded for key {Key}."
    )]
    public static partial void LogCacheBackgroundCompletionSucceeded(this ILogger logger, string key);

    [LoggerMessage(
        EventId = 6,
        EventName = "CacheBackgroundCompletionFailed",
        Level = LogLevel.Warning,
        Message = "Cache background completion failed for key {Key}; exception={ExceptionType}."
    )]
    public static partial void LogCacheBackgroundCompletionFailed(
        this ILogger logger,
        Exception exception,
        string key,
        string exceptionType
    );

    [LoggerMessage(
        EventId = 7,
        EventName = "CacheSoftTimeoutInert",
        Level = LogLevel.Warning,
        Message = "Cache factory soft timeout {Timeout} is configured for key {Key}, but fail-safe is disabled; the soft timeout is inert."
    )]
    public static partial void LogCacheSoftTimeoutInert(this ILogger logger, string key, TimeSpan timeout);

    [LoggerMessage(
        EventId = 8,
        EventName = "CacheSlidingExpirationRearmFailed",
        Level = LogLevel.Debug,
        Message = "Cache sliding-expiration re-arm failed for key {Key}; the cached value will still be returned."
    )]
    // Named "Coordinator…" (not the bare "LogSlidingExpirationRearmFailed") to avoid extension-method ambiguity
    // with provider log classes (e.g. RedisCacheLog) now that the provider assemblies see Core internals via
    // InternalsVisibleTo.
    public static partial void LogCoordinatorSlidingExpirationRearmFailed(
        this ILogger logger,
        Exception exception,
        string key
    );

    [LoggerMessage(
        EventId = 9,
        EventName = "CacheEagerRefreshSucceeded",
        Level = LogLevel.Debug,
        Message = "Cache eager refresh succeeded for key {Key}."
    )]
    public static partial void LogEagerRefreshSucceeded(this ILogger logger, string key);

    [LoggerMessage(
        EventId = 10,
        EventName = "CacheEagerRefreshFailed",
        Level = LogLevel.Warning,
        Message = "Cache eager refresh failed for key {Key}; exception={ExceptionType}. The entry stays fresh until natural expiry."
    )]
    public static partial void LogEagerRefreshFailed(
        this ILogger logger,
        Exception exception,
        string key,
        string exceptionType
    );

    [LoggerMessage(
        EventId = 11,
        EventName = "CacheEagerRefreshSkipped",
        Level = LogLevel.Debug,
        Message = "Cache eager refresh skipped for key {Key}: the gate write failed; the entry stays fresh and re-triggerable."
    )]
    public static partial void LogEagerRefreshSkipped(this ILogger logger, Exception exception, string key);

    [LoggerMessage(
        EventId = 12,
        EventName = "CacheFactoryLockReleaseFailed",
        Level = LogLevel.Warning,
        Message = "Cache distributed factory-lock release failed for key {Key}; the lease TTL is the backstop and "
            + "other nodes may be delayed until it expires."
    )]
    public static partial void LogCacheFactoryLockReleaseFailed(this ILogger logger, Exception exception, string key);

    [LoggerMessage(
        EventId = 13,
        EventName = "CacheFactoryLockAcquireFailed",
        Level = LogLevel.Warning,
        Message = "Cache distributed factory-lock acquire failed for key {Key}; serving stale value after lock "
            + "provider exception {ExceptionType}."
    )]
    public static partial void LogCacheFactoryLockAcquireFailed(
        this ILogger logger,
        Exception exception,
        string key,
        string exceptionType
    );
}
