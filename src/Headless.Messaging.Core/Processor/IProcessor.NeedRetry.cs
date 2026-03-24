// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
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
/// </summary>
public sealed class MessageNeedToRetryProcessor : IProcessor, IRetryProcessorMonitor
{
    private const int _MinSuggestedValueForFallbackWindowLookbackSeconds = 30;
    private readonly ILogger<MessageNeedToRetryProcessor> _logger;
    private readonly IDispatcher _dispatcher;
    private readonly TimeSpan _baseInterval;
    private static readonly TimeSpan _lockSafetyMargin = TimeSpan.FromSeconds(10);
    private readonly TimeSpan _maxInterval;
    private readonly IOptions<MessagingOptions> _options;
    private readonly IDataStorage _dataStorage;
    private readonly TimeSpan _lookbackWindow;
    private readonly string _instance;
    private readonly ICircuitBreakerMonitor? _circuitBreakerMonitor;
    private readonly bool _adaptivePolling;
    private readonly double _circuitOpenRateThreshold;
    private Task? _failedRetryConsumeTask;

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
    private long _currentIntervalTicks;
    private int _consecutiveHealthyCycles;
    private int _consecutiveCleanCycles;

    public MessageNeedToRetryProcessor(
        IOptions<MessagingOptions> options,
        IOptions<RetryProcessorOptions> retryOptions,
        ILogger<MessageNeedToRetryProcessor> logger,
        IDispatcher dispatcher,
        IDataStorage dataStorage,
        ICircuitBreakerMonitor? circuitBreakerMonitor = null
    )
    {
        _options = options;
        _logger = logger;
        _dispatcher = dispatcher;
        _baseInterval = TimeSpan.FromSeconds(options.Value.FailedRetryInterval);
        _currentIntervalTicks = _baseInterval.Ticks;
        _lookbackWindow = TimeSpan.FromSeconds(options.Value.FallbackWindowLookbackSeconds);
        _dataStorage = dataStorage;
        _circuitBreakerMonitor = circuitBreakerMonitor;

        _adaptivePolling = retryOptions.Value.AdaptivePolling;
        _maxInterval = retryOptions.Value.MaxPollingInterval;
        _circuitOpenRateThreshold = retryOptions.Value.CircuitOpenRateThreshold;

        _instance = (
            (FormattableString)$"{Helper.GetInstanceHostname()}_{SnowflakeIdLongIdGenerator.GenerateWorkerId()}"
        ).ToString(CultureInfo.InvariantCulture);

        _CheckSafeOptionsSet();
    }

    /// <inheritdoc />
    public TimeSpan CurrentPollingInterval => new(Interlocked.Read(ref _currentIntervalTicks));

    /// <inheritdoc />
    public bool IsBackedOff => Interlocked.Read(ref _currentIntervalTicks) > _baseInterval.Ticks;

    /// <summary>Sets the current polling interval. Exposed for testing via InternalsVisibleTo.</summary>
    internal void SetCurrentIntervalForTest(TimeSpan value) =>
        Interlocked.Exchange(ref _currentIntervalTicks, value.Ticks);

    /// <inheritdoc />
    public ValueTask ResetBackpressureAsync(CancellationToken ct = default)
    {
        Interlocked.Exchange(ref _currentIntervalTicks, _baseInterval.Ticks);
        Interlocked.Exchange(ref _consecutiveHealthyCycles, 0);
        Interlocked.Exchange(ref _consecutiveCleanCycles, 0);
        return ValueTask.CompletedTask;
    }

    public async Task ProcessAsync(ProcessingContext context)
    {
        Argument.IsNotNull(context);

        var storage = context.Provider.GetRequiredService<IDataStorage>();

        _ = Task
            .Factory.StartNew(
                () => _ProcessPublishedAsync(storage, context),
                CancellationToken.None,
                TaskCreationOptions.DenyChildAttach,
                TaskScheduler.Default
            )
            .Unwrap()
            .ContinueWith(
                t => _logger.LogError(t.Exception, "Unhandled exception in published-message retry processing"),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default
            );

        if (_options.Value.UseStorageLock && _failedRetryConsumeTask is { IsCompleted: false })
        {
            await _dataStorage.RenewLockAsync(
                $"received_retry_{_options.Value.Version}",
                _GetLockTtl(),
                _instance,
                context.CancellationToken
            );

            await context
                .WaitAsync(TimeSpan.FromTicks(Interlocked.Read(ref _currentIntervalTicks)))
                .ConfigureAwait(false);

            return;
        }

        _failedRetryConsumeTask = Task
            .Factory.StartNew(
                () => _ProcessReceivedAsync(storage, context),
                CancellationToken.None,
                TaskCreationOptions.DenyChildAttach,
                TaskScheduler.Default
            )
            .Unwrap();

        _ = _failedRetryConsumeTask.ContinueWith(
            t => _logger.LogError(t.Exception, "Unhandled exception in received-message retry processing"),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default
        );

        _ = _failedRetryConsumeTask.ContinueWith(
            _ => _failedRetryConsumeTask = null,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );

        await context.WaitAsync(TimeSpan.FromTicks(Interlocked.Read(ref _currentIntervalTicks))).ConfigureAwait(false);
    }

    private async Task _ProcessPublishedAsync(IDataStorage connection, ProcessingContext context)
    {
        context.ThrowIfStopping();

        var lockName = $"publish_retry_{_options.Value.Version}";
        var lockAcquired = false;
        if (
            _options.Value.UseStorageLock
            && !(
                lockAcquired = await connection.AcquireLockAsync(
                    lockName,
                    _GetLockTtl(),
                    _instance,
                    context.CancellationToken
                )
            )
        )
        {
            return;
        }

        try
        {
            var messages = await _GetSafelyAsync(connection.GetPublishedMessagesOfNeedRetry, _lookbackWindow)
                .ConfigureAwait(false);

            foreach (var message in messages)
            {
                context.ThrowIfStopping();

                await _dispatcher.EnqueueToPublish(message, context.CancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            if (lockAcquired)
            {
                await connection.ReleaseLockAsync(lockName, _instance, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private async Task _ProcessReceivedAsync(IDataStorage connection, ProcessingContext context)
    {
        context.ThrowIfStopping();

        var lockName = $"received_retry_{_options.Value.Version}";
        var lockAcquired = false;
        if (
            _options.Value.UseStorageLock
            && !(
                lockAcquired = await connection.AcquireLockAsync(
                    lockName,
                    _GetLockTtl(),
                    _instance,
                    context.CancellationToken
                )
            )
        )
        {
            return;
        }

        try
        {
            var messages = await _GetSafelyAsync(connection.GetReceivedMessagesOfNeedRetry, _lookbackWindow)
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
                    _logger.LogDebug(
                        "Skipping retry for message {StorageId} — circuit open for group {Group}",
                        message.StorageId,
                        LogSanitizer.Sanitize(group)
                    );
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
        finally
        {
            if (lockAcquired)
            {
                await connection.ReleaseLockAsync(lockName, _instance, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private async Task<IEnumerable<T>> _GetSafelyAsync<T>(
        Func<TimeSpan, CancellationToken, ValueTask<IEnumerable<T>>> getMessagesAsync,
        TimeSpan lookbackWindow,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            return await getMessagesAsync(lookbackWindow, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(1, ex, "Get messages from storage failed. Retrying...");

            return [];
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

        _logger.LogDebug(
            "Adaptive polling: circuit-open rate exceeds threshold, interval increased to {Interval}",
            TimeSpan.FromTicks(desired)
        );
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

        _logger.LogDebug(
            "Adaptive polling: healthy for 2 cycles, interval decreased to {Interval}",
            TimeSpan.FromTicks(desired)
        );
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
        return TimeSpan.FromTicks(effectiveTicks).Add(_lockSafetyMargin);
    }

    private void _CheckSafeOptionsSet()
    {
        if (_lookbackWindow < TimeSpan.FromSeconds(_MinSuggestedValueForFallbackWindowLookbackSeconds))
        {
            _logger.LogWarning(
                "The provided FallbackWindowLookbackSeconds of {CurrentSetFallbackWindowLookbackSeconds} is set to a value lower than {MinSuggestedSeconds} seconds. This might cause unwanted unsafe behavior if the consumer takes more than the provided FallbackWindowLookbackSeconds to execute. ",
                _options.Value.FallbackWindowLookbackSeconds,
                _MinSuggestedValueForFallbackWindowLookbackSeconds
            );
        }
    }
}
