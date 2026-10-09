// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Headless.Messaging.Redis;

internal sealed class RedisConsumerClient(
    string subscriptionName,
    byte concurrency,
    IRedisStreamManager redis,
    IOptions<RedisMessagingOptions> options,
    ILogger<RedisConsumerClient> logger,
    MessageLane lane = MessageLane.Queue,
    TimeProvider? timeProvider = null,
    ConsumerSubscriptionKind kind = ConsumerSubscriptionKind.Competing,
    RedisConsumerNameLease? consumerName = null
) : IConsumerClient
{
    // Bounds the drain when the client is disposed without a shutdown budget. A handler still running past it keeps
    // its entry pending, so another consumer claims it after PendingClaimMinIdleTime (at-least-once).
    private static readonly TimeSpan _ShutdownDrainTimeout = TimeSpan.FromSeconds(30);

    private readonly string _groupName = RedisPhysicalAddress.ConsumerGroup(lane, subscriptionName);
    private readonly SemaphoreSlim _semaphore = new(concurrency);
    private readonly ConsumerPauseGate _pauseGate = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    // Tracks the handler tasks of the concurrent path so shutdown can drain them before disposing the semaphore.
    private readonly InFlightHandlerTracker _inFlightHandlers = new();

    private readonly RedisConsumerNameLease _consumerName =
        consumerName ?? new RedisConsumerNames().Acquire(RedisPhysicalAddress.ConsumerGroup(lane, subscriptionName));

    private int _disposed;
    private string[] _messageNames = null!;

    // Where an every-instance client's group-less reads start: each stream's tail when it subscribed.
    private StreamPosition[] _everyInstanceStart = [];

    public Func<TransportMessage, object?, Task>? OnMessageCallback { get; set; }

    public Action<LogMessageEventArgs>? OnLogCallback { get; set; }

    public void AttachCallbacks(Func<TransportMessage, object?, Task>? onMessage, Action<LogMessageEventArgs>? onLog)
    {
        OnMessageCallback = onMessage;
        OnLogCallback = onLog;
    }

    public BrokerAddress BrokerAddress => new("redis", options.Value.DisplayEndpoint);

    public async ValueTask SubscribeAsync(
        IEnumerable<string> messageNames,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(messageNames);

        var arr = messageNames.Select(messageName => RedisPhysicalAddress.ForLane(lane, messageName)).ToArray();

        if (kind is ConsumerSubscriptionKind.EveryInstance)
        {
            // An every-instance client reads the streams without a consumer group, so it creates nothing on the
            // server and a crashed process leaves nothing behind. Fixing the start at each stream's current tail,
            // rather than reading from "$" later, makes readiness exact: every entry added from here on is read.
            _everyInstanceStart = await redis.GetStreamTailPositionsAsync(arr, cancellationToken).ConfigureAwait(false);
            _messageNames = arr;
            _ready.TrySetResult();
            return;
        }

        foreach (var messageName in arr)
        {
            await redis
                .CreateStreamWithConsumerGroupAsync(messageName, _groupName, cancellationToken)
                .ConfigureAwait(false);
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
        // An every-instance client resumes from the last id it read after a lost connection, so it misses nothing
        // the stream still holds and has no gap to report through the re-established callback.
        _ObserveBackgroundHandler(
            kind is ConsumerSubscriptionKind.EveryInstance
                ? _ConsumeMessages(
                    redis.PollStreamsFromAsync(_everyInstanceStart, timeout, cancellationToken),
                    StreamPosition.NewMessages,
                    cancellationToken
                )
                : _ListeningForMessagesAsync(timeout, cancellationToken)
        );

        try
        {
            await _timeProvider.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested — exit cleanly.
        }
    }

    public async ValueTask CommitAsync(object? sender, CancellationToken cancellationToken = default)
    {
        if (!_TryGetDelivery(sender, out var delivery))
        {
            return;
        }

        await redis.Ack(delivery.Stream, delivery.Group, delivery.Id, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask RejectAsync(object? sender, CancellationToken cancellationToken = default)
    {
        // A rejected entry stays pending, unacknowledged, in its place in the stream: the claim pass delivers it again
        // once it has been idle for PendingClaimMinIdleTime. Copying it to the tail would grow the stream and lose its
        // order, and the delay gives an open circuit or a failing store time to recover.
        return ValueTask.CompletedTask;
    }

    public async ValueTask PauseAsync(CancellationToken cancellationToken = default)
    {
        await _pauseGate.PauseAsync().ConfigureAwait(false);
    }

    public async ValueTask ResumeAsync(CancellationToken cancellationToken = default)
    {
        await _pauseGate.ResumeAsync().ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        return ShutdownAsync(_ShutdownDrainTimeout);
    }

    public async ValueTask ShutdownAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _pauseGate.Release();
        _ready.TrySetCanceled(CancellationToken.None);

        // Drain in-flight concurrent handlers before disposing the semaphore, so a running handler settles its entry
        // and releases its slot. Bounded so a stuck handler cannot block shutdown; an entry it never settles stays
        // pending and is claimed by another consumer (at-least-once).
        try
        {
            await _inFlightHandlers.DrainAsync(timeout, _timeProvider).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Handler faults are already logged by _ObserveBackgroundHandler; on a drain timeout or fault, log and
            // proceed, because shutdown must never block or throw.
            logger.RedisShutdownDrainIncomplete(ex, _groupName);
        }

        _semaphore.Dispose();

        // Released last, so a client created while this one drains cannot read under the same name.
        _consumerName.Release();
    }

    private void _ReleaseSemaphore()
    {
        if (concurrency > 0)
        {
            try
            {
                _semaphore.Release();
            }
            catch (SemaphoreFullException)
            {
                // Defensive: ignore over-release
            }
            catch (ObjectDisposedException)
            {
                // A handler that outlived the shutdown drain finishes after the semaphore is gone.
            }
        }
    }

    private async Task _ListeningForMessagesAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        //first time, we want to read our pending messages, in case we crashed and are recovering.
        var pendingMsgs = redis.PollStreamsPendingMessagesAsync(
            _messageNames,
            _groupName,
            _consumerName.Name,
            timeout,
            cancellationToken
        );

        await _ConsumeMessages(pendingMsgs, StreamPosition.Beginning, cancellationToken).ConfigureAwait(false);

        var stalePendingMsgs = redis.PollStreamsStalePendingMessagesAsync(
            _messageNames,
            _groupName,
            _consumerName.Name,
            options.Value.PendingClaimMinIdleTime,
            timeout,
            cancellationToken
        );
        _ObserveBackgroundHandler(_ConsumeMessages(stalePendingMsgs, StreamPosition.Beginning, cancellationToken));

        //Once we consumed our history, we can start getting new messages.
        var newMsgs = redis.PollStreamsLatestMessagesAsync(
            _messageNames,
            _groupName,
            _consumerName.Name,
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

                    if (concurrency > 0)
                    {
                        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                        var handlerTask = Task.Run(
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
                        );

                        _inFlightHandlers.Track(handlerTask);
                        _ObserveBackgroundHandler(handlerTask);
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
                    message = RedisMessage.Create(entry);

                    // Overwrites any wire value so a producer cannot choose the address.
                    message.Headers[Headers.TransportAddress] = stream.Key.ToString();
                }
                catch (Exception ex)
                {
                    var errorContext = _CreateMalformedEntryErrorContext(ex, entry);
                    logger.InvalidRedisEntry(errorContext.Exception, entry.Id, stream.Key, position, _groupName);

                    var logArgs = new LogMessageEventArgs
                    {
                        LogType = MqLogType.RedisConsumeError,
                        Reason = errorContext.Exception.Message,
                    };

                    try
                    {
                        var onError = options.Value.OnConsumeError?.Invoke(errorContext);

                        await (onError ?? Task.CompletedTask).ConfigureAwait(false);
                    }
                    catch (Exception onError)
                    {
                        logger.RedisConsumeErrorCallbackFailed(onError, nameof(RedisMessagingOptions.OnConsumeError));
                    }
                    finally
                    {
                        OnLogCallback?.Invoke(logArgs);

                        // A group-less read keeps no pending entry, so there is nothing to acknowledge.
                        if (kind is ConsumerSubscriptionKind.Competing)
                        {
                            await redis
                                .Ack(stream.Key.ToString(), _groupName, entry.Id.ToString(), CancellationToken.None)
                                .ConfigureAwait(false);
                        }
                    }

                    return;
                }

                object delivery =
                    kind is ConsumerSubscriptionKind.EveryInstance
                        ? new RedisEveryInstanceDelivery(stream.Key.ToString(), entry.Id.ToString())
                        : new RedisConsumerDelivery(stream.Key.ToString(), _groupName, entry.Id.ToString());

                await OnMessageCallback!(message, delivery).ConfigureAwait(false);
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

    private static RedisMessagingOptions.ConsumeErrorContext _CreateMalformedEntryErrorContext(
        Exception exception,
        StreamEntry entry
    )
    {
        var entryId = entry.Id.ToString();
        Exception safeException = exception switch
        {
            RedisConsumeMissingHeadersException => new RedisConsumeMissingHeadersException(entryId),
            RedisConsumeMissingBodyException => new RedisConsumeMissingBodyException(entryId),
            RedisConsumeInvalidHeadersException => new RedisConsumeInvalidHeadersException(
                entryId,
                new InvalidDataException("The Redis message headers could not be parsed.")
            ),
            _ => new InvalidDataException($"Redis entry [{entryId}] contains a malformed Messaging envelope."),
        };

        return new RedisMessagingOptions.ConsumeErrorContext(safeException, new StreamEntry(entry.Id, []));
    }

    private void _ObserveBackgroundHandler(Task task)
    {
        _ = task.ContinueWith(
            completedTask =>
            {
                var exception = completedTask.Exception?.GetBaseException();
                if (exception is not null)
                {
                    logger.RedisBackgroundHandlerFailed(exception, _groupName);
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
                delivery = new RedisConsumerDelivery(stream, group, id);
                return true;

            default:
                delivery = default;
                return false;
        }
    }
}

internal readonly record struct RedisConsumerDelivery(string Stream, string Group, string Id);

/// <summary>
/// The settlement token of a group-less read. CommitAsync and RejectAsync ignore it: the read left no pending entry to
/// acknowledge, and an every-instance delivery is never redelivered.
/// </summary>
internal readonly record struct RedisEveryInstanceDelivery(string Stream, string Id);

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

    [LoggerMessage(
        EventId = 3008,
        Level = LogLevel.Warning,
        Message = "Redis consumer of group {GroupId} shut down before its in-flight handlers finished; their unsettled entries stay pending for another consumer to claim"
    )]
    public static partial void RedisShutdownDrainIncomplete(this ILogger logger, Exception exception, string groupId);
}
