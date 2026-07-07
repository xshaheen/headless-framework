// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Headless.Messaging.Redis;

internal sealed class RedisConsumerClient(
    string groupId,
    byte groupConcurrent,
    IRedisStreamManager redis,
    IOptions<RedisMessagingOptions> options,
    ILogger<RedisConsumerClient> logger,
    TimeSpan? stalePendingClaimMinIdleTime = null,
    TimeProvider? timeProvider = null
) : IConsumerClient
{
    private readonly SemaphoreSlim _semaphore = new(groupConcurrent);
    private readonly ConsumerPauseGate _pauseGate = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly TimeSpan _stalePendingClaimMinIdleTime = stalePendingClaimMinIdleTime ?? TimeSpan.FromMinutes(5);
    private readonly string _consumerName = $"{groupId}:{Environment.MachineName}:{Guid.NewGuid():N}";
    private int _disposed;
    private string[] _messageNames = null!;

    public Func<TransportMessage, object?, Task>? OnMessageCallback { get; set; }

    public Action<LogMessageEventArgs>? OnLogCallback { get; set; }

    public BrokerAddress BrokerAddress => new("redis", options.Value.DisplayEndpoint);

    public async ValueTask SubscribeAsync(IEnumerable<string> messageNames)
    {
        Argument.IsNotNull(messageNames);

        var arr = messageNames.ToArray();

        foreach (var messageName in arr)
        {
            await redis.CreateStreamWithConsumerGroupAsync(messageName, groupId).ConfigureAwait(false);
        }

        _messageNames = arr;
        _ready.TrySetResult();
    }

    public ValueTask WaitUntilReadyAsync(CancellationToken cancellationToken = default)
    {
        return new ValueTask(_ready.Task.WaitAsync(cancellationToken));
    }

    public async ValueTask ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        _ObserveBackgroundHandler(_ListeningForMessagesAsync(timeout, cancellationToken));

        try
        {
            await _timeProvider.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested — exit cleanly.
        }
    }

    public async ValueTask CommitAsync(object? sender)
    {
        if (!_TryGetDelivery(sender, out var delivery))
        {
            return;
        }

        await redis.Ack(delivery.Stream, delivery.Group, delivery.Id).ConfigureAwait(false);
    }

    public async ValueTask RejectAsync(object? sender)
    {
        if (sender is not RedisConsumerDelivery delivery)
        {
            return;
        }

        await redis.RequeueAndAck(delivery.Stream, delivery.Group, delivery.Id, delivery.Entries).ConfigureAwait(false);
    }

    public async ValueTask PauseAsync(CancellationToken cancellationToken = default) =>
        await _pauseGate.PauseAsync().ConfigureAwait(false);

    public async ValueTask ResumeAsync(CancellationToken cancellationToken = default) =>
        await _pauseGate.ResumeAsync().ConfigureAwait(false);

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        _pauseGate.Release();
        _ready.TrySetCanceled();
        _semaphore.Dispose();
        return ValueTask.CompletedTask;
    }

    private void _ReleaseSemaphore()
    {
        if (groupConcurrent > 0)
        {
            try
            {
                _semaphore.Release();
            }
            catch (SemaphoreFullException)
            {
                // Defensive: ignore over-release
            }
        }
    }

    private async Task _ListeningForMessagesAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        //first time, we want to read our pending messages, in case we crashed and are recovering.
        var pendingMsgs = redis.PollStreamsPendingMessagesAsync(
            _messageNames,
            groupId,
            _consumerName,
            timeout,
            cancellationToken
        );

        await _ConsumeMessages(pendingMsgs, StreamPosition.Beginning, cancellationToken).ConfigureAwait(false);

        var stalePendingMsgs = redis.PollStreamsStalePendingMessagesAsync(
            _messageNames,
            groupId,
            _consumerName,
            _stalePendingClaimMinIdleTime,
            timeout,
            cancellationToken
        );
        _ObserveBackgroundHandler(_ConsumeMessages(stalePendingMsgs, StreamPosition.Beginning, cancellationToken));

        //Once we consumed our history, we can start getting new messages.
        var newMsgs = redis.PollStreamsLatestMessagesAsync(
            _messageNames,
            groupId,
            _consumerName,
            timeout,
            cancellationToken
        );

        _ObserveBackgroundHandler(_ConsumeMessages(newMsgs, StreamPosition.NewMessages, cancellationToken));
    }

    private async Task _ConsumeMessages(
        IAsyncEnumerable<IEnumerable<RedisStreamMessages>> streamsSet,
        RedisValue position,
        CancellationToken cancellationToken
    )
    {
        await foreach (var set in streamsSet.WithCancellation(cancellationToken))
        {
            foreach (var stream in set)
            {
                foreach (var entry in stream.Entries)
                {
                    await _pauseGate.WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);
                    if (entry.IsNull)
                    {
                        return;
                    }

                    if (groupConcurrent > 0)
                    {
                        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                        _ObserveBackgroundHandler(
                            Task.Run(
                                async () =>
                                {
                                    try
                                    {
                                        await consumeAsync(position, stream, entry).ConfigureAwait(false);
                                    }
                                    finally
                                    {
                                        _ReleaseSemaphore();
                                    }
                                },
                                CancellationToken.None // Ensure semaphore release even if cancellation is requested during handler execution
                            )
                        );
                    }
                    else
                    {
                        await consumeAsync(position, stream, entry).ConfigureAwait(false);
                    }
                }
            }
        }

        async Task consumeAsync(RedisValue position, RedisStreamMessages stream, StreamEntry entry)
        {
            try
            {
                TransportMessage message;
                try
                {
                    message = RedisMessage.Create(entry, groupId);
                }
                catch (Exception ex)
                {
                    logger.InvalidRedisEntry(ex, entry.Id, stream.Key, position, groupId);

                    var logArgs = new LogMessageEventArgs
                    {
                        LogType = MqLogType.RedisConsumeError,
                        Reason = ex.ToString(),
                    };

                    try
                    {
                        var onError = options.Value.OnConsumeError?.Invoke(
                            new RedisMessagingOptions.ConsumeErrorContext(ex, entry)
                        );

                        await (onError ?? Task.CompletedTask).ConfigureAwait(false);
                    }
                    catch (Exception onError)
                    {
                        logger.RedisConsumeErrorCallbackFailed(onError, nameof(RedisMessagingOptions.OnConsumeError));
                    }
                    finally
                    {
                        OnLogCallback!(logArgs);
                    }

                    return;
                }

                await OnMessageCallback!(
                    message,
                    new RedisConsumerDelivery(stream.Key.ToString(), groupId, entry.Id.ToString(), [.. entry.Values])
                )
                    .ConfigureAwait(false);
            }
            finally
            {
                var positionName =
                    position == StreamPosition.Beginning
                        ? nameof(StreamPosition.Beginning)
                        : nameof(StreamPosition.NewMessages);
                logger.RedisEntryDelivered(entry.Id, positionName);
            }
        }
    }

    private void _ObserveBackgroundHandler(Task task)
    {
        _ = task.ContinueWith(
            completedTask =>
            {
                var exception = completedTask.Exception?.GetBaseException();
                if (exception is not null)
                {
                    logger.RedisBackgroundHandlerFailed(exception, groupId);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
    }

    private static bool _TryGetDelivery(object? sender, out RedisConsumerDelivery delivery)
    {
        switch (sender)
        {
            case RedisConsumerDelivery redisDelivery:
                delivery = redisDelivery;
                return true;

            case (string stream, string group, string id):
                delivery = new RedisConsumerDelivery(stream, group, id, []);
                return true;

            default:
                delivery = default;
                return false;
        }
    }
}

internal readonly record struct RedisConsumerDelivery(string Stream, string Group, string Id, NameValueEntry[] Entries);

internal static partial class RedisConsumerClientLog
{
    [LoggerMessage(
        EventId = 3004,
        Level = LogLevel.Error,
        Message = "Redis entry {EntryId} on stream {StreamKey} at position {Position} of group {GroupId} is not valid for Messaging, see inner exception for more details."
    )]
    public static partial void InvalidRedisEntry(
        this ILogger logger,
        Exception exception,
        RedisValue entryId,
        RedisKey streamKey,
        RedisValue position,
        string groupId
    );

    [LoggerMessage(
        EventId = 3005,
        Level = LogLevel.Error,
        Message = "Unhandled exception occurred in {Action} action, Exception has been caught"
    )]
    public static partial void RedisConsumeErrorCallbackFailed(this ILogger logger, Exception exception, string action);

    [LoggerMessage(
        EventId = 3006,
        Level = LogLevel.Debug,
        Message = "Redis stream entry [{EntryId}] [position : {PositionName}] was delivered"
    )]
    public static partial void RedisEntryDelivered(this ILogger logger, RedisValue entryId, string positionName);

    [LoggerMessage(
        EventId = 3007,
        Level = LogLevel.Error,
        Message = "Unhandled exception in Redis background message handler for group {GroupId}"
    )]
    public static partial void RedisBackgroundHandlerFailed(this ILogger logger, Exception exception, string groupId);
}
