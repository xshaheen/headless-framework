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
public sealed class FactoryCacheCoordinator(TimeProvider timeProvider, ILogger? logger = null) : IDisposable
{
    private readonly TimeProvider _timeProvider = Argument.IsNotNull(timeProvider);
    private readonly KeyedAsyncLock _keyedLock = new();
    private readonly ILogger _logger = logger ?? NullLogger<FactoryCacheCoordinator>.Instance;
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
    /// <typeparam name="T">The cached value type.</typeparam>
    /// <param name="store">The provider store.</param>
    /// <param name="key">The cache key.</param>
    /// <param name="factory">The value factory.</param>
    /// <param name="options">The cache entry options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask<CacheValue<T>> GetOrAddAsync<T>(
        IFactoryCacheStore store,
        string key,
        Func<CancellationToken, ValueTask<T?>> factory,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(store);
        Argument.IsNotNullOrEmpty(key);
        Argument.IsNotNull(factory);
        _ValidateOptions(options);

        cancellationToken.ThrowIfCancellationRequested();

        var entry = await _TryGetEntryAsync<T>(store, key, cancellationToken).ConfigureAwait(false);
        var now = _GetUtcNow();

        if (entry.IsFresh(now))
        {
            await _TryRearmSlidingEntryAsync(store, key, entry, now).ConfigureAwait(false);
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

            var timeoutSelection = _SelectFactoryTimeout(options, staleCandidate, now, key);
            FactoryRunResult<T> factoryResult;

            try
            {
                factoryResult = await _RunFactoryWithTimeoutAsync(
                        factory,
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
                    _StartBackgroundCompletion(
                        store,
                        key,
                        factoryResult.RunningTask,
                        factoryResult.InternalCancellationTokenSource,
                        staleCandidate,
                        options,
                        releaser
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

            await _SetFreshEntryAsync(store, key, factoryResult.Value, options, cancellationToken)
                .ConfigureAwait(false);

            return new CacheValue<T>(factoryResult.Value, hasValue: true);
        }
        finally
        {
            if (ownsReleaser)
            {
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
        Task<T?> factoryTask,
        CancellationTokenSource internalCts,
        CacheStoreEntry<T> staleCandidate,
        CacheEntryOptions options,
        IDisposable releaser
    )
    {
        var backgroundTask = _CompleteFactoryInBackgroundAsync(
            store,
            key,
            factoryTask,
            internalCts,
            staleCandidate,
            options,
            releaser
        );

        _ObserveFaultedTask(backgroundTask, key);
    }

    private async Task _CompleteFactoryInBackgroundAsync<T>(
        IFactoryCacheStore store,
        string key,
        Task<T?> factoryTask,
        CancellationTokenSource internalCts,
        CacheStoreEntry<T> staleCandidate,
        CacheEntryOptions options,
        IDisposable releaser
    )
    {
#pragma warning disable VSTHRD003 // This continuation intentionally races/observes the transferred factory task.
        try
        {
            // No ceiling configured: let the detached factory run to completion, matching comparable caches.
            if (options.BackgroundFactoryCeiling == Timeout.InfiniteTimeSpan)
            {
                await _ObserveBackgroundFactoryAsync(store, key, factoryTask, internalCts, staleCandidate, options)
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
                await _ObserveBackgroundFactoryAsync(store, key, factoryTask, internalCts, staleCandidate, options)
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
            releaser.Dispose();
            BackgroundCompletionFinished?.Invoke();
        }
    }

    private async Task _ObserveBackgroundFactoryAsync<T>(
        IFactoryCacheStore store,
        string key,
        Task<T?> factoryTask,
        CancellationTokenSource internalCts,
        CacheStoreEntry<T> staleCandidate,
        CacheEntryOptions options
    )
    {
#pragma warning disable VSTHRD003 // This continuation deliberately observes the transferred background factory task.
        try
        {
            var value = await factoryTask.ConfigureAwait(false);

            if (!internalCts.IsCancellationRequested)
            {
                await _SetFreshEntryAsync(store, key, value, options, CancellationToken.None).ConfigureAwait(false);
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

        try
        {
            // Caller-facing restamps pass CancellationToken.None (a caller cancellation between the factory
            // throw and this await must not abort the stale return); the ceiling-bounded background restamp
            // passes the ceiling token so a hung store write can be cancelled instead of orphaning it.
            await store
                .SetEntryAsync(
                    key,
                    staleCandidate.Value,
                    staleCandidate.IsNull,
                    logicalExpiresAt,
                    physicalExpiresAt,
                    slidingExpiration: null,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // Swallow all exceptions (including cancellation): the stale value must always be returned.
            _logger.LogFailSafeRestampFailed(exception, key);
        }
    }

    private async ValueTask _SetFreshEntryAsync<T>(
        IFactoryCacheStore store,
        string key,
        T? value,
        CacheEntryOptions options,
        CancellationToken cancellationToken
    )
    {
        // The factory succeeded: persist the fresh value and return it. A store-write failure on the
        // fresh path must propagate rather than activate fail-safe (which would discard the fresh value).
        var now = _GetUtcNow();
        var logicalExpiresAt = now.Add(options.Duration);
        var physicalDuration = options.IsFailSafeEnabled
            ? _Max(options.Duration, options.FailSafeMaxDuration)
            : options.Duration;
        var physicalExpiresAt = now.Add(physicalDuration);
        var slidingExpiration = options.SlidingExpiration;

        if (slidingExpiration.HasValue)
        {
            logicalExpiresAt = _Min(now.Add(slidingExpiration.Value), physicalExpiresAt);
        }

        await store
            .SetEntryAsync(
                key,
                value,
                isNull: value is null,
                logicalExpiresAt,
                physicalExpiresAt,
                slidingExpiration,
                cancellationToken
            )
            .ConfigureAwait(false);
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
            _logger.LogSlidingExpirationRearmFailed(exception, key);
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

    private static TimeSpan _Max(TimeSpan left, TimeSpan right) => left >= right ? left : right;

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

    private static void _ValidateOptions(CacheEntryOptions options)
    {
        Argument.IsPositive(options.Duration);

        if (options.SlidingExpiration is { } configuredSlidingExpiration)
        {
            Argument.IsPositive(configuredSlidingExpiration);

            // Redis encodes the idle window as whole milliseconds; a sub-millisecond span floors to 0 and the
            // frame then decodes as unframed (silent value loss), while in-memory would keep it natively. Reject
            // it at the single sliding write choke point so every provider behaves identically.
            Argument.IsGreaterThanOrEqualTo(configuredSlidingExpiration, TimeSpan.FromMilliseconds(1));

            Ensure.False(
                options.IsFailSafeEnabled,
                "Sliding expiration and fail-safe are not supported together in this version."
            );
        }

        if (options.IsFailSafeEnabled)
        {
            Argument.IsPositive(options.FailSafeMaxDuration);
            Argument.IsPositive(options.FailSafeThrottleDuration);
        }

        _ValidateOptionalTimeout(options.FactorySoftTimeout, nameof(options.FactorySoftTimeout));
        _ValidateOptionalTimeout(options.FactoryHardTimeout, nameof(options.FactoryHardTimeout));
        _ValidateOptionalTimeout(options.BackgroundFactoryCeiling, nameof(options.BackgroundFactoryCeiling));
        _ValidateOptionalTimeout(options.LockTimeout, nameof(options.LockTimeout));

        if (
            options.FactorySoftTimeout != Timeout.InfiniteTimeSpan
            && options.FactoryHardTimeout != Timeout.InfiniteTimeSpan
        )
        {
            Argument.IsGreaterThan(
                options.FactoryHardTimeout,
                options.FactorySoftTimeout,
                message: "FactoryHardTimeout must be greater than FactorySoftTimeout when both are finite.",
                paramName: nameof(options.FactoryHardTimeout)
            );
        }
    }

    private static void _ValidateOptionalTimeout(TimeSpan timeout, string paramName)
    {
        if (timeout == Timeout.InfiniteTimeSpan)
        {
            return;
        }

        Argument.IsPositive(timeout, paramName: paramName);
    }

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
    public static partial void LogSlidingExpirationRearmFailed(this ILogger logger, Exception exception, string key);
}
