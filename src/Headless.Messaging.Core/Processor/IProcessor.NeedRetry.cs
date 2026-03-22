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
    private readonly ICircuitBreakerStateManager? _circuitBreakerStateManager;
    private readonly bool _adaptivePolling;
    private readonly double _circuitOpenRateThreshold;
    private Task? _failedRetryConsumeTask;

    // Threading contract:
    // - _AdjustPollingInterval is called only from ProcessAsync (sequential).
    // - _GetLockTtl reads _currentIntervalTicks from the same sequential context.
    // - Interlocked is used on _currentIntervalTicks for cross-thread visibility (future-proofing).
    // - _consecutiveHealthyCycles and _consecutiveCleanCycles are only accessed from ProcessAsync — no sync needed.
    private long _currentIntervalTicks;
    private int _consecutiveHealthyCycles;
    private int _consecutiveCleanCycles;

    public MessageNeedToRetryProcessor(
        IOptions<MessagingOptions> options,
        IOptions<RetryProcessorOptions> retryOptions,
        ILogger<MessageNeedToRetryProcessor> logger,
        IDispatcher dispatcher,
        IDataStorage dataStorage,
        IServiceProvider serviceProvider
    )
    {
        _options = options;
        _logger = logger;
        _dispatcher = dispatcher;
        _baseInterval = TimeSpan.FromSeconds(options.Value.FailedRetryInterval);
        _currentIntervalTicks = _baseInterval.Ticks;
        _lookbackWindow = TimeSpan.FromSeconds(options.Value.FallbackWindowLookbackSeconds);
        _dataStorage = dataStorage;
        _circuitBreakerStateManager = serviceProvider.GetService<ICircuitBreakerStateManager>();

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

    /// <inheritdoc />
    public ValueTask ResetBackpressureAsync(CancellationToken ct = default)
    {
        Interlocked.Exchange(ref _currentIntervalTicks, _baseInterval.Ticks);
        _consecutiveHealthyCycles = 0;
        _consecutiveCleanCycles = 0;
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
            .Unwrap();

        if (_options.Value.UseStorageLock && _failedRetryConsumeTask is { IsCompleted: false })
        {
            await _dataStorage.RenewLockAsync(
                $"received_retry_{_options.Value.Version}",
                _GetLockTtl(),
                _instance,
                context.CancellationToken
            );

            await context.WaitAsync(TimeSpan.FromTicks(Interlocked.Read(ref _currentIntervalTicks))).ConfigureAwait(false);

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

        if (
            _options.Value.UseStorageLock
            && !await connection.AcquireLockAsync(
                $"publish_retry_{_options.Value.Version}",
                _GetLockTtl(),
                _instance,
                context.CancellationToken
            )
        )
        {
            return;
        }

        var messages = await _GetSafelyAsync(connection.GetPublishedMessagesOfNeedRetry, _lookbackWindow)
            .ConfigureAwait(false);

        foreach (var message in messages)
        {
            context.ThrowIfStopping();

            await _dispatcher.EnqueueToPublish(message, context.CancellationToken).ConfigureAwait(false);
        }

        if (_options.Value.UseStorageLock)
        {
            await connection.ReleaseLockAsync(
                $"publish_retry_{_options.Value.Version}",
                _instance,
                context.CancellationToken
            );
        }
    }

    private async Task _ProcessReceivedAsync(IDataStorage connection, ProcessingContext context)
    {
        context.ThrowIfStopping();

        if (
            _options.Value.UseStorageLock
            && !await connection.AcquireLockAsync(
                $"received_retry_{_options.Value.Version}",
                _GetLockTtl(),
                _instance,
                context.CancellationToken
            )
        )
        {
            return;
        }

        var messages = await _GetSafelyAsync(connection.GetReceivedMessagesOfNeedRetry, _lookbackWindow)
            .ConfigureAwait(false);

        var enqueued = 0;
        var skippedCircuitOpen = 0;

        foreach (var message in messages)
        {
            context.ThrowIfStopping();

            var group = message.Origin.GetGroup();
            if (group is not null && _circuitBreakerStateManager?.IsOpen(group) == true)
            {
                skippedCircuitOpen++;
                _logger.LogDebug(
                    "Skipping retry for message {DbId} — circuit open for group {Group}",
                    message.DbId,
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

        if (_options.Value.UseStorageLock)
        {
            await connection.ReleaseLockAsync(
                $"received_retry_{_options.Value.Version}",
                _instance,
                context.CancellationToken
            );
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
    /// </summary>
    private void _AdjustPollingInterval(int enqueued, int skippedCircuitOpen)
    {
        var total = enqueued + skippedCircuitOpen;
        var current = Interlocked.Read(ref _currentIntervalTicks);

        // No messages at all — clean cycle.
        // Zero messages means both "healthy" (no circuit-open skips) and "clean" (no retries
        // pending), so both counters are incremented. The _consecutiveCleanCycles >= 3 check
        // resets to base interval before _consecutiveHealthyCycles >= 2 would halve, giving
        // a full reset priority over gradual step-down when the system is completely idle.
        if (total == 0)
        {
            _consecutiveCleanCycles++;
            _consecutiveHealthyCycles++;

            if (_consecutiveCleanCycles >= 3)
            {
                Interlocked.Exchange(ref _currentIntervalTicks, _baseInterval.Ticks);
                _consecutiveCleanCycles = 0;
                _consecutiveHealthyCycles = 0;
            }
            else if (_consecutiveHealthyCycles >= 2 && current > _baseInterval.Ticks)
            {
                Interlocked.Exchange(ref _currentIntervalTicks, Math.Max(current / 2, _baseInterval.Ticks));
            }

            return;
        }

        var transientRate = (double)skippedCircuitOpen / total;

        if (transientRate > _circuitOpenRateThreshold)
        {
            // High circuit-open rate — back off
            _consecutiveHealthyCycles = 0;
            _consecutiveCleanCycles = 0;

            var newTicks = current <= _maxInterval.Ticks / 2 ? current * 2 : _maxInterval.Ticks;
            Interlocked.Exchange(ref _currentIntervalTicks, newTicks);

            _logger.LogDebug(
                "Adaptive polling: circuit-open rate {Rate:P0} exceeds threshold, interval increased to {Interval}",
                transientRate,
                TimeSpan.FromTicks(newTicks)
            );
        }
        else if (transientRate <= _circuitOpenRateThreshold / 2.0)
        {
            // Healthy cycle — well below backoff threshold
            _consecutiveHealthyCycles++;
            _consecutiveCleanCycles = 0;

            if (_consecutiveHealthyCycles >= 2 && current > _baseInterval.Ticks)
            {
                var newTicks = Math.Max(current / 2, _baseInterval.Ticks);
                Interlocked.Exchange(ref _currentIntervalTicks, newTicks);
                _consecutiveHealthyCycles = 0;

                _logger.LogDebug(
                    "Adaptive polling: healthy for 2 cycles, interval decreased to {Interval}",
                    TimeSpan.FromTicks(newTicks)
                );
            }
        }
        else
        {
            // Between backoff threshold and recovery threshold — hold steady
            _consecutiveHealthyCycles = 0;
            _consecutiveCleanCycles = 0;
        }
    }

    private TimeSpan _GetLockTtl()
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
