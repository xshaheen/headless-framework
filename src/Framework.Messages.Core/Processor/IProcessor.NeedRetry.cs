// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Abstractions;
using Framework.Checks;
using Framework.Messages.Configuration;
using Framework.Messages.Internal;
using Framework.Messages.Persistence;
using Framework.Messages.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Framework.Messages.Processor;

public class MessageNeedToRetryProcessor : IProcessor
{
    private const int _MinSuggestedValueForFallbackWindowLookbackSeconds = 30;
    private readonly ILogger<MessageNeedToRetryProcessor> _logger;
    private readonly IDispatcher _dispatcher;
    private readonly TimeSpan _waitingInterval;
    private readonly IOptions<MessagingOptions> _options;
    private readonly IDataStorage _dataStorage;
    private readonly TimeSpan _ttl;
    private readonly TimeSpan _lookbackSeconds;
    private readonly string _instance;
    private Task? _failedRetryConsumeTask;

    public MessageNeedToRetryProcessor(
        IOptions<MessagingOptions> options,
        ILogger<MessageNeedToRetryProcessor> logger,
        IDispatcher dispatcher,
        IDataStorage dataStorage
    )
    {
        _options = options;
        _logger = logger;
        _dispatcher = dispatcher;
        _waitingInterval = TimeSpan.FromSeconds(options.Value.FailedRetryInterval);
        _lookbackSeconds = TimeSpan.FromSeconds(options.Value.FallbackWindowLookbackSeconds);
        _dataStorage = dataStorage;
        _ttl = _waitingInterval.Add(TimeSpan.FromSeconds(10));

        _instance = string.Concat(Helper.GetInstanceHostname(), "_", SnowflakeIdLongIdGenerator.GenerateWorkerId());

        _CheckSafeOptionsSet();
    }

    public virtual async Task ProcessAsync(ProcessingContext context)
    {
        Argument.IsNotNull(context);

        var storage = context.Provider.GetRequiredService<IDataStorage>();

        _ = Task.Run(() => _ProcessPublishedAsync(storage, context));

        if (_options.Value.UseStorageLock && _failedRetryConsumeTask is { IsCompleted: false })
        {
            await _dataStorage.RenewLockAsync(
                $"received_retry_{_options.Value.Version}",
                _ttl,
                _instance,
                context.CancellationToken
            );

            await context.WaitAsync(_waitingInterval).AnyContext();

            return;
        }

        _failedRetryConsumeTask = Task.Run(() => _ProcessReceivedAsync(storage, context));

        _ = _failedRetryConsumeTask.ContinueWith(_ =>
        {
            _failedRetryConsumeTask = null;
        });

        await context.WaitAsync(_waitingInterval).AnyContext();
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

        var messages = await _GetSafelyAsync(connection.GetPublishedMessagesOfNeedRetry, _lookbackSeconds).AnyContext();

        foreach (var message in messages)
        {
            context.ThrowIfStopping();

            await _dispatcher.EnqueueToPublish(message, context.CancellationToken).AnyContext();
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

        var messages = await _GetSafelyAsync(connection.GetReceivedMessagesOfNeedRetry, _lookbackSeconds).AnyContext();

        foreach (var message in messages)
        {
            context.ThrowIfStopping();

            await _dispatcher.EnqueueToExecute(message, null, context.CancellationToken).AnyContext();
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
        Func<TimeSpan, Task<IEnumerable<T>>> getMessagesAsync,
        TimeSpan lookbackSeconds
    )
    {
        try
        {
            return await getMessagesAsync(lookbackSeconds).AnyContext();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(1, ex, "Get messages from storage failed. Retrying...");

            return [];
        }
    }

    private void _CheckSafeOptionsSet()
    {
        if (_lookbackSeconds < TimeSpan.FromSeconds(_MinSuggestedValueForFallbackWindowLookbackSeconds))
        {
            _logger.LogWarning(
                "The provided FallbackWindowLookbackSeconds of {currentSetFallbackWindowLookbackSeconds} is set to a value lower than {minSuggestedSeconds} seconds. This might cause unwanted unsafe behavior if the consumer takes more than the provided FallbackWindowLookbackSeconds to execute. ",
                _options.Value.FallbackWindowLookbackSeconds,
                _MinSuggestedValueForFallbackWindowLookbackSeconds
            );
        }
    }
}
