// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.DistributedLocks;
using Headless.Messaging.CircuitBreaker;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Processor;

/// <summary>
/// Processes messages that need to be retried, with adaptive polling and circuit breaker awareness.
/// Exposed publicly so dashboards and observability extensions can resolve it through
/// <see cref="IRetryProcessorMonitor"/> when reporting pickup health.
/// </summary>
[PublicAPI]
public sealed class MessageNeedToRetryProcessor : IProcessor, IRetryProcessorMonitor
{
    private static readonly TimeSpan _LockSafetyMargin = TimeSpan.FromSeconds(10);

    private readonly ILogger<MessageNeedToRetryProcessor> _logger;
    private readonly IDispatcher _dispatcher;
    private readonly TimeSpan _baseInterval;
    private readonly TimeSpan _maxInterval;
    private readonly IOptions<MessagingOptions> _options;
    private readonly ICircuitBreakerMonitor? _circuitBreakerMonitor;
    private readonly bool _adaptivePolling;
    private readonly double _circuitOpenRateThreshold;
    private readonly string _publishRetryResource;
    private readonly string _receiveRetryResource;
    private Task? _receivedRetryConsumeTask;
    private Task? _publishedRetryConsumeTask;
    private volatile IDistributedLock? _receivedRetryHandle;

    // Threading contract:
    // - _AdjustPollingInterval is called only from ProcessAsync (sequential).
    // - _GetLockTtl reads _currentIntervalTicks from the same sequential context.
    // - _currentIntervalTicks is mutated by both _AdjustPollingInterval (sequential) and
    //   ResetBackpressureAsync (callable from any thread). All mutations use CAS loops
    //   (Interlocked.CompareExchange) to avoid non-atomic read-modify-write races.
    // - _consecutiveHealthyCycles and _consecutiveCleanCycles are written by both ProcessAsync
    //   (sequential) and ResetBackpressureAsync (callable from any thread). All increments use
    //   Interlocked.Increment and all resets use Interlocked.Exchange to avoid non-atomic
    //   read-modify-write races. Direct reads (comparisons) are safe for aligned int fields.
    // - _receivedRetryHandle is declared volatile and has two writers:
    //     (a) _ProcessReceivedAsync (background Task) — assigns the handle after acquire and clears it
    //         in the finally block when the work body completes.
    //     (b) ProcessAsync (poll-tick thread) — clears the handle when RenewAsync returns false.
    //   Volatile is sufficient because neither writer performs a compound read-modify-write on the
    //   field; both perform unconditional reference assignments (which are atomic on .NET). The
    //   reader (the renewal branch in ProcessAsync) observes the latest assignment without needing
    //   a full Interlocked operation.
    private long _currentIntervalTicks;
    private int _consecutiveHealthyCycles;
    private int _consecutiveCleanCycles;

    // Tracks consecutive failures per call site so adaptive polling backs off (rather than
    // accelerating from artificially "clean" cycles when work throws and returns an empty list).
    // BOTH storage-pickup failures (_GetSafelyAsync) AND lock-acquire failures
    // (_RecordLockAcquireFailure) increment the same field per kind — the conflation is
    // intentional and documented on _RecordLockAcquireFailure, pending #296. Escalates to Error
    // after _StoragePickupErrorEscalationThreshold to surface ongoing outages.
    //
    // The counter is kept per pickup kind (Published / Received) because both paths run
    // independently. A shared counter would let a healthy path reset the streak every cycle,
    // masking a persistent failure on the other path — so the Error escalation log would never
    // fire even when one side has been down for hours.
    private int _consecutivePublishedPickupFailures;
    private int _consecutiveReceivedPickupFailures;

    private const int _StoragePickupErrorEscalationThreshold = 3;

    private enum StoragePickupKind
    {
        Published,
        Received,
    }

    public MessageNeedToRetryProcessor(
        IOptions<MessagingOptions> options,
        IOptions<RetryProcessorOptions> retryOptions,
        ILogger<MessageNeedToRetryProcessor> logger,
        IDispatcher dispatcher,
        [FromKeyedServices(MessagingKeys.LockProvider)] IDistributedLockProvider lockProvider,
        ICircuitBreakerMonitor? circuitBreakerMonitor = null
    )
    {
        _options = options;
        _logger = logger;
        _dispatcher = dispatcher;
        _baseInterval = retryOptions.Value.BaseInterval;
        _currentIntervalTicks = _baseInterval.Ticks;
        LockProvider = lockProvider;
        _circuitBreakerMonitor = circuitBreakerMonitor;

        _adaptivePolling = retryOptions.Value.AdaptivePolling;
        _maxInterval = retryOptions.Value.MaxPollingInterval;
        _circuitOpenRateThreshold = retryOptions.Value.CircuitOpenRateThreshold;

        // Cache the resource names once; MessagingOptions.Version is effectively immutable after
        // bootstrap so per-tick string interpolation is wasted work.
        _publishRetryResource = MessagingKeys.PublishRetryResource(options.Value.Version);
        _receiveRetryResource = MessagingKeys.ReceiveRetryResource(options.Value.Version);
    }

    /// <inheritdoc />
    public TimeSpan CurrentPollingInterval => new(Interlocked.Read(ref _currentIntervalTicks));

    /// <inheritdoc />
    public bool IsBackedOff => Interlocked.Read(ref _currentIntervalTicks) > _baseInterval.Ticks;

    /// <summary>The keyed-DI lock provider that was injected. Internal accessor — production code uses this; tests verify injection via InternalsVisibleTo.</summary>
    internal IDistributedLockProvider LockProvider { get; }

    /// <summary>Sets the current polling interval. Exposed for testing via InternalsVisibleTo.</summary>
    internal void SetCurrentIntervalForTest(TimeSpan value) =>
        Interlocked.Exchange(ref _currentIntervalTicks, value.Ticks);

    /// <summary>One-shot flag set after the startup jitter delay fires on the first poll.</summary>
    /// <remarks>
    /// The first <see cref="ProcessAsync"/> call waits a random fraction of <see cref="_baseInterval"/>
    /// before performing any work, so that replicas booting simultaneously do not synchronize their
    /// poll ticks and overwhelm the storage layer (poll-tick storm). Subsequent polls use the
    /// configured interval. Mutated only by <see cref="ProcessAsync"/>, which is invoked sequentially
    /// per processor instance — a plain bool is sufficient.
    /// </remarks>
    internal bool StartupJitterApplied { get; private set; }

    /// <inheritdoc />
    public ValueTask ResetBackpressureAsync(CancellationToken ct = default)
    {
        Interlocked.Exchange(ref _currentIntervalTicks, _baseInterval.Ticks);
        Interlocked.Exchange(ref _consecutiveHealthyCycles, 0);
        Interlocked.Exchange(ref _consecutiveCleanCycles, 0);
        Interlocked.Exchange(ref _consecutivePublishedPickupFailures, 0);
        Interlocked.Exchange(ref _consecutiveReceivedPickupFailures, 0);
        return ValueTask.CompletedTask;
    }

    public async Task ProcessAsync(ProcessingContext context)
    {
        Argument.IsNotNull(context);

        if (!StartupJitterApplied)
        {
            var jitter = TimeSpan.FromTicks((long)(_baseInterval.Ticks * Random.Shared.NextDouble()));
            await context.WaitAsync(jitter).ConfigureAwait(false);
            StartupJitterApplied = true;
        }

        var storage = context.Provider.GetRequiredService<IDataStorage>();

        // Mirror the received-retry guard below: skip spawning a new published-retry task while
        // the previous one is still running under UseStorageLock to avoid concurrent lock-renewal
        // contention. Without UseStorageLock the guard is a no-op (multiple in-flight tasks are
        // acceptable since the storage layer's own concurrency primitives serialize writes).
        if (!_options.Value.UseStorageLock || _publishedRetryConsumeTask is not { IsCompleted: false })
        {
            _publishedRetryConsumeTask = Task
                .Factory.StartNew(
                    () => _ProcessPublishedAsync(storage, context),
                    CancellationToken.None,
                    TaskCreationOptions.DenyChildAttach,
                    TaskScheduler.Default
                )
                .Unwrap();

            _ = _publishedRetryConsumeTask.ContinueWith(
                t => _logger.PublishedRetryProcessingUnhandled(t.Exception),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default
            );

            // VSTHRD003 false positive: we're not awaiting `t` here — Interlocked.CompareExchange
            // performs an atomic reference comparison + swap. The lambda parameter happens to be
            // a Task, but the call is a pure scalar CAS on the reference slot. Pattern matches the
            // CAS loops at lines 553/576 that use the same primitive on int fields.
#pragma warning disable VSTHRD003
            _ = _publishedRetryConsumeTask.ContinueWith(
                t => Interlocked.CompareExchange(ref _publishedRetryConsumeTask, null, t),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default
            );
#pragma warning restore VSTHRD003
        }

        if (_options.Value.UseStorageLock && _receivedRetryConsumeTask is { IsCompleted: false })
        {
            var handle = _receivedRetryHandle;
            if (handle is not null)
            {
                try
                {
                    var renewed = await handle
                        .RenewAsync(_GetLockTtl(), context.CancellationToken)
                        .ConfigureAwait(false);
                    if (!renewed)
                    {
                        // Best-effort coarse coordination: clear the handle so the next tick re-acquires
                        // from scratch. The in-flight dispatch task keeps running under per-row LockedUntil,
                        // which is the actual correctness primitive against double-dispatch (see the
                        // "Distributed Lock Integration" section in docs/llms/messaging.md). The full
                        // lock-loss signaling story — LeaseMonitor + IDistributedLock.HandleLostToken —
                        // is tracked in #296 (depends on #289 Phase 2 shipping the abstraction).
                        _logger.ReceivedRetryLockOwnershipLost();
                        _receivedRetryHandle = null;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.ReceivedRetryLockRenewalFailed(ex);
                    // Mirror the !renewed branch above: null the handle on exception so the next tick
                    // re-acquires fresh instead of retrying the same possibly-dead handle on every
                    // subsequent poll. The whole manual-renewal loop is replaced by LeaseMonitor in #296.
                    _receivedRetryHandle = null;
                }
            }

            await context
                .WaitAsync(TimeSpan.FromTicks(Interlocked.Read(ref _currentIntervalTicks)))
                .ConfigureAwait(false);

            return;
        }

        _receivedRetryConsumeTask = Task
            .Factory.StartNew(
                () => _ProcessReceivedAsync(storage, context),
                CancellationToken.None,
                TaskCreationOptions.DenyChildAttach,
                TaskScheduler.Default
            )
            .Unwrap();

        _ = _receivedRetryConsumeTask.ContinueWith(
            t => _logger.ReceivedRetryProcessingUnhandled(t.Exception),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default
        );

        // VSTHRD003 false positive: same scalar-CAS pattern as the published-path continuation above.
        // Interlocked.CompareExchange atomically nulls the field only if it still points at `t`.
#pragma warning disable VSTHRD003
        _ = _receivedRetryConsumeTask.ContinueWith(
            t => Interlocked.CompareExchange(ref _receivedRetryConsumeTask, null, t),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
#pragma warning restore VSTHRD003

        await context.WaitAsync(TimeSpan.FromTicks(Interlocked.Read(ref _currentIntervalTicks))).ConfigureAwait(false);
    }

    private async Task _ProcessPublishedAsync(IDataStorage connection, ProcessingContext context)
    {
        context.ThrowIfStopping();

        if (!_options.Value.UseStorageLock)
        {
            await _ExecutePublishedWorkAsync(connection, context).ConfigureAwait(false);
            return;
        }

        await using var acquiredHandle = await _TryAcquireLockAsync(StoragePickupKind.Published, context).ConfigureAwait(false);
        if (acquiredHandle is null)
        {
            return;
        }

        await _ExecutePublishedWorkAsync(connection, context).ConfigureAwait(false);
    }

    private async Task _ProcessReceivedAsync(IDataStorage connection, ProcessingContext context)
    {
        context.ThrowIfStopping();

        if (!_options.Value.UseStorageLock)
        {
            await _ExecuteReceivedWorkAsync(connection, context).ConfigureAwait(false);
            return;
        }

        await using var acquiredHandle = await _TryAcquireLockAsync(StoragePickupKind.Received, context).ConfigureAwait(false);
        if (acquiredHandle is null)
        {
            return;
        }

        _receivedRetryHandle = acquiredHandle;
        try
        {
            await _ExecuteReceivedWorkAsync(connection, context).ConfigureAwait(false);
        }
        finally
        {
            _receivedRetryHandle = null;
        }
    }

    /// <summary>
    /// Attempts to acquire the published-retry or received-retry distributed lock, wrapping
    /// <see cref="IDistributedLockProvider.TryAcquireAsync"/> in the per-kind escalation-counter pattern shared with
    /// storage-pickup failures so adaptive polling backs off on lock-store outages rather than tight-looping.
    /// </summary>
    private async Task<IDistributedLock?> _TryAcquireLockAsync(StoragePickupKind kind, ProcessingContext context)
    {
        var resource = kind switch
        {
            StoragePickupKind.Published => _publishRetryResource,
            StoragePickupKind.Received => _receiveRetryResource,
            _ => throw new InvalidOperationException($"Unknown storage pickup kind: {kind}"),
        };

        try
        {
            return await LockProvider
                .TryAcquireAsync(
                    resource,
                    timeUntilExpires: _GetLockTtl(),
                    acquireTimeout: TimeSpan.Zero,
                    cancellationToken: context.CancellationToken
                )
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _RecordLockAcquireFailure(kind, ex);
            return null;
        }
    }

    private async Task _ExecutePublishedWorkAsync(IDataStorage connection, ProcessingContext context)
    {
        var messages = await _GetSafelyAsync(
                connection.GetPublishedMessagesOfNeedRetryAsync,
                StoragePickupKind.Published,
                context.CancellationToken
            )
            .ConfigureAwait(false);

        foreach (var message in messages)
        {
            context.ThrowIfStopping();

            await _dispatcher.EnqueueToPublish(message, context.CancellationToken).ConfigureAwait(false);
        }
    }

    private async Task _ExecuteReceivedWorkAsync(IDataStorage connection, ProcessingContext context)
    {
        var messages = await _GetSafelyAsync(
                connection.GetReceivedMessagesOfNeedRetryAsync,
                StoragePickupKind.Received,
                context.CancellationToken
            )
            .ConfigureAwait(false);

        var enqueued = 0;
        var skippedCircuitOpen = 0;
        var circuitOpenCache = new Dictionary<string, bool>(StringComparer.Ordinal);

        foreach (var message in messages)
        {
            context.ThrowIfStopping();

            var group = message.Origin.GetGroup();
            if (group is not null && _IsCircuitOpen(group, circuitOpenCache))
            {
                skippedCircuitOpen++;
                var safeGroup = LogSanitizer.Sanitize(group);
                _logger.RetrySkippedBecauseCircuitOpen(message.StorageId, safeGroup);
                continue;
            }

            await _dispatcher.EnqueueToExecute(message, null, context.CancellationToken).ConfigureAwait(false);
            enqueued++;
        }

        if (_adaptivePolling)
        {
            _AdjustPollingInterval(enqueued, skippedCircuitOpen);
        }
    }

    private async Task<IEnumerable<T>> _GetSafelyAsync<T>(
        Func<CancellationToken, ValueTask<IEnumerable<T>>> getMessagesAsync,
        StoragePickupKind kind,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var result = await getMessagesAsync(cancellationToken).ConfigureAwait(false);
            Interlocked.Exchange(ref _CounterRef(kind), 0);
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Back off polling so a sustained storage outage isn't masked by artificially "clean"
            // cycles (empty list ≠ healthy). Escalate the log to Error after a small streak so
            // monitoring picks up persistent failures.
            var failureCount = Interlocked.Increment(ref _CounterRef(kind));
            _CompareExchangeDouble();

            if (failureCount >= _StoragePickupErrorEscalationThreshold)
            {
                _logger.RetryStoragePickupFailureEscalated(ex, failureCount);
            }
            else
            {
                _logger.GetMessagesFromStorageFailed(ex);
            }

            return [];
        }
    }

    private ref int _CounterRef(StoragePickupKind kind)
    {
        switch (kind)
        {
            case StoragePickupKind.Published:
                return ref _consecutivePublishedPickupFailures;
            case StoragePickupKind.Received:
                return ref _consecutiveReceivedPickupFailures;
            default:
                throw new InvalidOperationException($"Unknown storage pickup kind: {kind}");
        }
    }

    /// <summary>
    /// Records a lock-acquire failure on the same per-kind counter used by storage-pickup failures so
    /// adaptive polling backs off rather than tight-looping a sick lock store. Escalates the log to
    /// Error after the same _StoragePickupErrorEscalationThreshold streak so monitoring
    /// (<see cref="IRetryProcessorMonitor"/>) picks up persistent lock-store outages. Distinct
    /// EventIds from the pickup-failure events keep the two cases filterable in log aggregators.
    /// </summary>
    private void _RecordLockAcquireFailure(StoragePickupKind kind, Exception ex)
    {
        // The failure counter here is shared with storage-pickup failures: _GetSafelyAsync
        // (above) resets the same counter to 0 on a healthy storage call, which means a
        // persistent lock-store outage is masked whenever the database itself is fine. The
        // counter conflation is documented as intentional pending #296 (split lock vs storage
        // counters once #289 surfaces IDistributedLock.HandleLostToken as the canonical
        // lock-loss signal — at that point the lock-side gets its own escalation EventId).
        var failureCount = Interlocked.Increment(ref _CounterRef(kind));
        _CompareExchangeDouble();

        switch (kind)
        {
            case StoragePickupKind.Published:
                if (failureCount >= _StoragePickupErrorEscalationThreshold)
                {
                    _logger.PublishedRetryLockAcquireFailureEscalated(ex, failureCount);
                }
                else
                {
                    _logger.PublishedRetryLockAcquireFailed(ex);
                }
                break;
            case StoragePickupKind.Received:
                if (failureCount >= _StoragePickupErrorEscalationThreshold)
                {
                    _logger.ReceivedRetryLockAcquireFailureEscalated(ex, failureCount);
                }
                else
                {
                    _logger.ReceivedRetryLockAcquireFailed(ex);
                }
                break;
            default:
                throw new InvalidOperationException($"Unknown storage pickup kind: {kind}");
        }
    }

    /// <summary>
    /// Two-counter adaptive polling:
    /// - _consecutiveHealthyCycles: cycles with zero circuit-open messages → halves interval at >=2.
    /// - _consecutiveCleanCycles: cycles with zero total retry messages → resets to base at >=3.
    /// Clean cycles (total==0) increment both counters; the >=3 reset check runs before the >=2
    /// halving check, so a sustained quiet period snaps back to base rather than halving stepwise.
    ///
    /// All mutations of _currentIntervalTicks use CAS (CompareExchange) loops to avoid
    /// non-atomic read-modify-write races with concurrent ResetBackpressureAsync calls.
    /// </summary>
    internal void _AdjustPollingInterval(int enqueued, int skippedCircuitOpen)
    {
        var total = enqueued + skippedCircuitOpen;

        // No messages at all — clean cycle.
        // Zero messages means both "healthy" (no circuit-open skips) and "clean" (no retries
        // pending), so both counters are incremented. The _consecutiveCleanCycles >= 3 check
        // resets to base interval before _consecutiveHealthyCycles >= 2 would halve, giving
        // a full reset priority over gradual step-down when the system is completely idle.
        if (total == 0)
        {
            Interlocked.Increment(ref _consecutiveCleanCycles);
            Interlocked.Increment(ref _consecutiveHealthyCycles);

            if (_consecutiveCleanCycles >= 3)
            {
                Interlocked.Exchange(ref _currentIntervalTicks, _baseInterval.Ticks);
                Interlocked.Exchange(ref _consecutiveCleanCycles, 0);
                Interlocked.Exchange(ref _consecutiveHealthyCycles, 0);
            }
            else if (_consecutiveHealthyCycles >= 2)
            {
                _CompareExchangeHalve();
            }

            return;
        }

        var circuitOpenSkipRate = (double)skippedCircuitOpen / total;

        if (circuitOpenSkipRate > _circuitOpenRateThreshold)
        {
            // High circuit-open rate — back off
            Interlocked.Exchange(ref _consecutiveHealthyCycles, 0);
            Interlocked.Exchange(ref _consecutiveCleanCycles, 0);

            _CompareExchangeDouble();
        }
        else if (circuitOpenSkipRate <= _circuitOpenRateThreshold / 2.0)
        {
            // Healthy cycle — well below backoff threshold
            Interlocked.Increment(ref _consecutiveHealthyCycles);
            Interlocked.Exchange(ref _consecutiveCleanCycles, 0);

            if (_consecutiveHealthyCycles >= 2)
            {
                _CompareExchangeHalve();
                Interlocked.Exchange(ref _consecutiveHealthyCycles, 0);
            }
        }
        else
        {
            // Between backoff threshold and recovery threshold — hold steady
            Interlocked.Exchange(ref _consecutiveHealthyCycles, 0);
            Interlocked.Exchange(ref _consecutiveCleanCycles, 0);
        }
    }

    /// <summary>
    /// CAS loop: doubles _currentIntervalTicks, capped at _maxInterval.
    /// If a concurrent ResetBackpressureAsync modifies the value between read and write,
    /// the loop retries with the fresh value. Logs after successful CAS.
    /// </summary>
    private void _CompareExchangeDouble()
    {
        long snapshot;
        long desired;
        do
        {
            snapshot = Interlocked.Read(ref _currentIntervalTicks);
            desired = snapshot <= _maxInterval.Ticks / 2 ? snapshot * 2 : _maxInterval.Ticks;
        } while (Interlocked.CompareExchange(ref _currentIntervalTicks, desired, snapshot) != snapshot);

        var increasedInterval = TimeSpan.FromTicks(desired);
        _logger.AdaptivePollingIntervalIncreased(increasedInterval);
    }

    /// <summary>
    /// CAS loop: halves _currentIntervalTicks, floored at _baseInterval.
    /// No-op if already at base interval. Logs after successful CAS.
    /// </summary>
    private void _CompareExchangeHalve()
    {
        long snapshot;
        long desired;
        do
        {
            snapshot = Interlocked.Read(ref _currentIntervalTicks);
            if (snapshot <= _baseInterval.Ticks)
            {
                return; // already at base — nothing to halve
            }

            desired = Math.Max(snapshot / 2, _baseInterval.Ticks);
        } while (Interlocked.CompareExchange(ref _currentIntervalTicks, desired, snapshot) != snapshot);

        var decreasedInterval = TimeSpan.FromTicks(desired);
        _logger.AdaptivePollingIntervalDecreased(decreasedInterval);
    }

    private bool _IsCircuitOpen(string group, Dictionary<string, bool> cache)
    {
        if (cache.TryGetValue(group, out var isOpen))
        {
            return isOpen;
        }

        isOpen = _circuitBreakerMonitor?.IsOpen(group) == true;
        cache[group] = isOpen;
        return isOpen;
    }

    internal TimeSpan _GetLockTtl()
    {
        var ticks = Interlocked.Read(ref _currentIntervalTicks);
        var effectiveTicks = ticks > _baseInterval.Ticks ? ticks : _baseInterval.Ticks;
        return TimeSpan.FromTicks(effectiveTicks).Add(_LockSafetyMargin);
    }
}

internal static partial class RetryProcessorLog
{
    [LoggerMessage(
        EventId = 3107,
        Level = LogLevel.Error,
        Message = "Unhandled exception in published-message retry processing"
    )]
    public static partial void PublishedRetryProcessingUnhandled(this ILogger logger, Exception? ex);

    [LoggerMessage(
        EventId = 3108,
        Level = LogLevel.Error,
        Message = "Unhandled exception in received-message retry processing"
    )]
    public static partial void ReceivedRetryProcessingUnhandled(this ILogger logger, Exception? ex);

    [LoggerMessage(
        EventId = 3109,
        Level = LogLevel.Debug,
        Message = "Skipping retry for message {StorageId} — circuit open for group {Group}"
    )]
    public static partial void RetrySkippedBecauseCircuitOpen(this ILogger logger, long storageId, string? group);

    [LoggerMessage(EventId = 3110, Level = LogLevel.Warning, Message = "Get messages from storage failed. Retrying...")]
    public static partial void GetMessagesFromStorageFailed(this ILogger logger, Exception ex);

    [LoggerMessage(
        EventId = 3111,
        Level = LogLevel.Debug,
        Message = "Adaptive polling: circuit-open rate exceeds threshold, interval increased to {Interval}"
    )]
    public static partial void AdaptivePollingIntervalIncreased(this ILogger logger, TimeSpan interval);

    [LoggerMessage(
        EventId = 3112,
        Level = LogLevel.Debug,
        Message = "Adaptive polling: healthy for 2 cycles, interval decreased to {Interval}"
    )]
    public static partial void AdaptivePollingIntervalDecreased(this ILogger logger, TimeSpan interval);
}
