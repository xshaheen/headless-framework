// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Caching;

/// <summary>Coordinates factory-backed cache operations across cache providers.</summary>
[PublicAPI]
public sealed class FactoryCacheCoordinator(TimeProvider timeProvider, ILogger? logger = null)
    : IDisposable
{
    private readonly TimeProvider _timeProvider = Argument.IsNotNull(timeProvider);
    private readonly KeyedAsyncLock _keyedLock = new();
    private readonly ILogger _logger = logger ?? NullLogger<FactoryCacheCoordinator>.Instance;
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
            return _ToCacheValue(entry, isStale: false);
        }

        var staleCandidate = _IsStaleCandidate(entry, now) ? entry : CacheStoreEntry<T>.NotFound;
        var lockTimeout = _SelectLockTimeout(options, staleCandidate, now);
        var releaser = await _keyedLock.LockAsync(key, lockTimeout, _timeProvider, cancellationToken).ConfigureAwait(false);

        if (releaser is null)
        {
            _logger.LogCacheFactoryTimedOut(key, "lock-soft", lockTimeout);
            return _ToCacheValue(staleCandidate, isStale: true);
        }

        var ownsReleaser = true;

        try
        {
            entry = await _TryGetEntryAsync<T>(store, key, cancellationToken).ConfigureAwait(false);
            now = _GetUtcNow();

            if (entry.IsFresh(now))
            {
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

                await _TryRestampStaleAsync(store, key, staleCandidate, options, now).ConfigureAwait(false);

                _logger.LogFailSafeActivated(key, exception.GetType().Name);
                return _ToCacheValue(staleCandidate, isStale: true);
            }

            if (factoryResult.IsTimedOut)
            {
                _logger.LogCacheFactoryTimedOut(key, timeoutSelection.Kind.ToString(), timeoutSelection.Timeout);

                if (timeoutSelection.Kind == FactoryTimeoutKind.Soft)
                {
                    _StartBackgroundCompletion(
                        store,
                        key,
                        factoryResult.RunningTask!,
                        factoryResult.InternalCancellationTokenSource!,
                        staleCandidate,
                        options,
                        releaser
                    );

                    ownsReleaser = false;
                    return _ToCacheValue(staleCandidate, isStale: true);
                }

                _ObserveAbandonedFactory(factoryResult.RunningTask!, key);

                now = _GetUtcNow();

                if (_IsStaleCandidate(staleCandidate, now))
                {
                    await _TryRestampStaleAsync(store, key, staleCandidate, options, now).ConfigureAwait(false);

                    _logger.LogFailSafeActivated(key, nameof(CacheFactoryTimeoutException));
                    return _ToCacheValue(staleCandidate, isStale: true);
                }

                throw new CacheFactoryTimeoutException(key, timeoutSelection.Timeout, timeoutSelection.Timeout);
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

        _ObserveBackgroundCompletion(backgroundTask, key);
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
            await _TryRestampStaleAsync(store, key, staleCandidate, options, _GetUtcNow()).ConfigureAwait(false);
            return;
        }

        using var ceilingCts = new CancellationTokenSource();
        var restampTask = _TryRestampStaleAsync(store, key, staleCandidate, options, _GetUtcNow()).AsTask();
        var ceilingTask = Task.Delay(options.BackgroundFactoryCeiling, _timeProvider, ceilingCts.Token);
        var winner = await Task.WhenAny(restampTask, ceilingTask).ConfigureAwait(false);

        if (winner == restampTask)
        {
            await ceilingCts.CancelAsync().ConfigureAwait(false);
            await restampTask.ConfigureAwait(false);
        }
    }

    private async ValueTask _TryRestampStaleAsync<T>(
        IFactoryCacheStore store,
        string key,
        CacheStoreEntry<T> staleCandidate,
        CacheEntryOptions options,
        DateTime now
    )
    {
        // staleCandidate always carries a physical expiration: _IsStaleCandidate (the only gate that assigns a
        // stale candidate) requires PhysicalExpiresAt.HasValue, so the throttle restamp can always be written.
        var physicalExpiresAt = staleCandidate.PhysicalExpiresAt!.Value;
        var logicalExpiresAt = _Min(now.Add(options.FailSafeThrottleDuration), physicalExpiresAt);

        try
        {
            // The restamp is a throttle optimization, not caller work, so it uses CancellationToken.None: a
            // caller cancellation between the factory throw and this await must not abort the stale return.
            await store
                .SetEntryAsync(
                    key,
                    staleCandidate.Value,
                    staleCandidate.IsNull,
                    logicalExpiresAt,
                    physicalExpiresAt,
                    CancellationToken.None
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

        await store
            .SetEntryAsync(key, value, isNull: value is null, logicalExpiresAt, physicalExpiresAt, cancellationToken)
            .ConfigureAwait(false);
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

    private static TimeSpan _SelectLockTimeout<T>(CacheEntryOptions options, CacheStoreEntry<T> staleCandidate, DateTime now) =>
        options.IsFailSafeEnabled
        && _IsStaleCandidate(staleCandidate, now)
        && options.FactorySoftTimeout != Timeout.InfiniteTimeSpan
            ? options.FactorySoftTimeout
            : Timeout.InfiniteTimeSpan;

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
        TimeSpan timeout,
        bool cancelOnTimeout,
        CancellationToken cancellationToken
    )
    {
        CancellationTokenSource? internalCts = new();
        using var delayCts = new CancellationTokenSource();
        CancellationTokenRegistration cancellationRegistration = default;

        try
        {
            var factoryTask = factory(internalCts.Token).AsTask();
            var delayTask = Task.Delay(timeout, _timeProvider, delayCts.Token);
            FactoryTimeoutTimerRegistered?.Invoke();
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
                throw new OperationCanceledException(cancellationToken);
            }

            if (cancelOnTimeout)
            {
                await internalCts.CancelAsync().ConfigureAwait(false);
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

    private void _ObserveAbandonedFactory<T>(Task<T?> factoryTask, string key)
    {
        _ = factoryTask.ContinueWith(
            task => _logger.LogCacheBackgroundCompletionFailed(task.Exception!, key, task.Exception!.GetType().Name),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default
        );
    }

    private void _ObserveBackgroundCompletion(Task task, string key)
    {
        _ = task.ContinueWith(
            faulted => _logger.LogCacheBackgroundCompletionFailed(faulted.Exception!, key, faulted.Exception!.GetType().Name),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default
        );
    }

    private static void _ValidateOptions(CacheEntryOptions options)
    {
        Argument.IsPositive(options.Duration);

        if (options.IsFailSafeEnabled)
        {
            Argument.IsPositive(options.FailSafeMaxDuration);
            Argument.IsPositive(options.FailSafeThrottleDuration);
        }

        _ValidateOptionalTimeout(options.FactorySoftTimeout, nameof(options.FactorySoftTimeout));
        _ValidateOptionalTimeout(options.FactoryHardTimeout, nameof(options.FactoryHardTimeout));
        _ValidateOptionalTimeout(options.BackgroundFactoryCeiling, nameof(options.BackgroundFactoryCeiling));

        if (
            options.FactorySoftTimeout != Timeout.InfiniteTimeSpan
            && options.FactoryHardTimeout != Timeout.InfiniteTimeSpan
            && options.FactoryHardTimeout <= options.FactorySoftTimeout
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "FactoryHardTimeout must be greater than FactorySoftTimeout when both are finite."
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

    private sealed record FactoryTimeoutSelection(FactoryTimeoutKind Kind, TimeSpan Timeout);

    private sealed class FactoryRunResult<T>
    {
        private FactoryRunResult(T? value, Task<T?>? runningTask, CancellationTokenSource? internalCancellationTokenSource)
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
    public static partial void LogCacheFactoryTimedOut(this ILogger logger, string key, string timeoutKind, TimeSpan timeout);

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
        Message =
            "Cache factory soft timeout {Timeout} is configured for key {Key}, but fail-safe is disabled; the soft timeout is inert."
    )]
    public static partial void LogCacheSoftTimeoutInert(this ILogger logger, string key, TimeSpan timeout);
}
