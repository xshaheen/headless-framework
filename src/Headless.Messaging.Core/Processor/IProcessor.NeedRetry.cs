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

public sealed class MessageNeedToRetryProcessor : IProcessor
{
    private const int _MinSuggestedValueForFallbackWindowLookbackSeconds = 30;
    private readonly ILogger<MessageNeedToRetryProcessor> _logger;
    private readonly IDispatcher _dispatcher;
    private readonly TimeSpan _baseInterval;
    private readonly TimeSpan _maxInterval;
    private readonly IOptions<MessagingOptions> _options;
    private readonly IDataStorage _dataStorage;
    private readonly TimeSpan _ttl;
    private readonly TimeSpan _lookbackSeconds;
    private readonly string _instance;
    private readonly ICircuitBreakerStateManager? _circuitBreakerStateManager;
    private readonly bool _adaptivePolling;
    private readonly double _transientFailureRateThreshold;
    private Task? _failedRetryConsumeTask;
    private TimeSpan _currentInterval;
    private int _consecutiveHealthyCycles;
    private int _consecutiveCleanCycles;

    public MessageNeedToRetryProcessor(
        IOptions<MessagingOptions> options,
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
        _currentInterval = _baseInterval;
        _lookbackSeconds = TimeSpan.FromSeconds(options.Value.FallbackWindowLookbackSeconds);
        _dataStorage = dataStorage;
        _ttl = _baseInterval.Add(TimeSpan.FromSeconds(10));
        _circuitBreakerStateManager = serviceProvider.GetService<ICircuitBreakerStateManager>();

        var retryProcessorOptions = options.Value.RetryProcessor;
        _adaptivePolling = retryProcessorOptions.AdaptivePolling;
        _maxInterval = TimeSpan.FromSeconds(retryProcessorOptions.MaxPollingInterval);
        _transientFailureRateThreshold = retryProcessorOptions.TransientFailureRateThreshold;

        _instance = (
            (FormattableString)$"{Helper.GetInstanceHostname()}_{SnowflakeIdLongIdGenerator.GenerateWorkerId()}"
        ).ToString(CultureInfo.InvariantCulture);

        _CheckSafeOptionsSet();
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
                _ttl,
                _instance,
                context.CancellationToken
            );

            await context.WaitAsync(_currentInterval).ConfigureAwait(false);

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

        await context.WaitAsync(_currentInterval).ConfigureAwait(false);
    }

    private async Task _ProcessPublishedAsync(IDataStorage connection, ProcessingContext context)
    {
        context.ThrowIfStopping();

        if (
            _options.Value.UseStorageLock
            && !await connection.AcquireLockAsync(
                $"publish_retry_{_options.Value.Version}",
                _ttl,
                _instance,
                context.CancellationToken
            )
        )
        {
            return;
        }

        var messages = await _GetSafelyAsync(connection.GetPublishedMessagesOfNeedRetry, _lookbackSeconds)
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
                _ttl,
                _instance,
                context.CancellationToken
            )
        )
        {
            return;
        }

        var messages = await _GetSafelyAsync(connection.GetReceivedMessagesOfNeedRetry, _lookbackSeconds)
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
                    group
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
        TimeSpan lookbackSeconds,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            return await getMessagesAsync(lookbackSeconds, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(1, ex, "Get messages from storage failed. Retrying...");

            return [];
        }
    }

    private void _AdjustPollingInterval(int enqueued, int skippedCircuitOpen)
    {
        var total = enqueued + skippedCircuitOpen;

        // No messages at all — clean cycle
        if (total == 0)
        {
            _consecutiveCleanCycles++;
            _consecutiveHealthyCycles++;

            if (_consecutiveCleanCycles >= 3)
            {
                _currentInterval = _baseInterval;
                _consecutiveCleanCycles = 0;
                _consecutiveHealthyCycles = 0;
            }
            else if (_consecutiveHealthyCycles >= 2 && _currentInterval > _baseInterval)
            {
                _currentInterval = TimeSpan.FromTicks(Math.Max(_currentInterval.Ticks / 2, _baseInterval.Ticks));
            }

            return;
        }

        var transientRate = (double)skippedCircuitOpen / total;

        if (transientRate > _transientFailureRateThreshold)
        {
            // High failure rate — back off
            _consecutiveHealthyCycles = 0;
            _consecutiveCleanCycles = 0;

            var doubled = TimeSpan.FromTicks(_currentInterval.Ticks * 2);
            _currentInterval = doubled < _maxInterval ? doubled : _maxInterval;

            _logger.LogDebug(
                "Adaptive polling: transient rate {Rate:P0} exceeds threshold, interval increased to {Interval}",
                transientRate,
                _currentInterval
            );
        }
        else if (transientRate <= _transientFailureRateThreshold / 2.0)
        {
            // Healthy cycle — well below backoff threshold
            _consecutiveHealthyCycles++;
            _consecutiveCleanCycles = 0;

            if (_consecutiveHealthyCycles >= 2 && _currentInterval > _baseInterval)
            {
                _currentInterval = TimeSpan.FromTicks(Math.Max(_currentInterval.Ticks / 2, _baseInterval.Ticks));
                _consecutiveHealthyCycles = 0;

                _logger.LogDebug(
                    "Adaptive polling: healthy for 2 cycles, interval decreased to {Interval}",
                    _currentInterval
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

    private void _CheckSafeOptionsSet()
    {
        if (_lookbackSeconds < TimeSpan.FromSeconds(_MinSuggestedValueForFallbackWindowLookbackSeconds))
        {
            _logger.LogWarning(
                "The provided FallbackWindowLookbackSeconds of {CurrentSetFallbackWindowLookbackSeconds} is set to a value lower than {MinSuggestedSeconds} seconds. This might cause unwanted unsafe behavior if the consumer takes more than the provided FallbackWindowLookbackSeconds to execute. ",
                _options.Value.FallbackWindowLookbackSeconds,
                _MinSuggestedValueForFallbackWindowLookbackSeconds
            );
        }
    }
}
