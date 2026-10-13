// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Frozen;
using Headless.Checks;
using Headless.Messaging.Internal;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;
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
    RedisConsumerNameLease? consumerName = null,
    IServiceProvider? serviceProvider = null
) : IConsumerClient
{
    private const string _TransportName = "Redis";

    // The longest envelope rejection reason an error context carries; it reaches logs.
    private const int _MaxLoggedReasonLength = 512;

    // A client built without a container still hands a custom headers builder a provider, one with no services.
    private static readonly IServiceProvider _NoServices = new NoServices();

    // Bounds the drain when the client is disposed without a shutdown budget. A handler still running past it keeps
    // its entry pending, so another consumer claims it after PendingClaimMinIdleTime (at-least-once).
    private static readonly TimeSpan _ShutdownDrainTimeout = TimeSpan.FromSeconds(30);

    private readonly string _groupName = RedisPhysicalAddress.ConsumerGroup(lane, subscriptionName);
    private readonly SemaphoreSlim _semaphore = new(concurrency);
    private readonly ConsumerPauseGate _pauseGate = new();
    private readonly BrokerReachabilityReporter _reachability = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly IServiceProvider _serviceProvider = serviceProvider ?? _NoServices;

    private readonly TransportEnvelopeGate _envelopeGate =
        serviceProvider?.GetService<TransportEnvelopeGate>() ?? TransportEnvelopeGate.Strict;

    // Maps each subscribed stream key to the message name it was derived from, so an entry a foreign producer wrote
    // without headers is routed by the stream it arrived on. Keys are escaped names, so a name is never parsed back
    // out of one.
    private FrozenDictionary<string, string> _arrivalNames = FrozenDictionary<string, string>.Empty;

    // Tracks the read loops and the handler tasks of the concurrent path so shutdown can drain them before disposing
    // the semaphore.
    private readonly InFlightHandlerTracker _inFlightHandlers = new();

    // Cancelled by shutdown, so the read loops stop on their own when the caller never cancels listening.
    private readonly CancellationTokenSource _stopReading = new();

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

        var names = messageNames.ToArray();
        var arr = names.Select(messageName => RedisPhysicalAddress.ForLane(lane, messageName)).ToArray();
        var arrivalNames = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < names.Length; i++)
        {
            arrivalNames.TryAdd(arr[i], names[i]);
        }

        _arrivalNames = arrivalNames.ToFrozenDictionary(StringComparer.Ordinal);

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
        // The read loops are drained with the handlers, so shutdown disposes the semaphore only after they stopped
        // dispatching: a loop that already passed its shutdown check never waits on a disposed semaphore, and a handler
        // it starts is still waited for.
        var listening = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_inFlightHandlers.TryTrack(listening.Task))
        {
            return;
        }

        try
        {
            using var readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopReading.Token);

            // An every-instance client resumes from the last id it read after a lost connection, so it misses nothing
            // the stream still holds and has no gap to report through the re-established callback.
            var reads =
                kind is ConsumerSubscriptionKind.EveryInstance
                    ? _ConsumeMessages(
                        redis.PollStreamsFromAsync(_everyInstanceStart, timeout, _ReportRead, readCts.Token),
                        StreamPosition.NewMessages,
                        readCts.Token
                    )
                    : _ListeningForMessagesAsync(timeout, readCts.Token);
            _ObserveBackgroundHandler(reads);

            try
            {
                await _timeProvider.Delay(Timeout.InfiniteTimeSpan, readCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Shutdown requested — exit cleanly.
            }

            // A read fault is already logged by _ObserveBackgroundHandler.
            await reads.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
        finally
        {
            listening.TrySetResult();
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
        await _stopReading.CancelAsync().ConfigureAwait(false);

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
        _stopReading.Dispose();

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

    // The stream manager retries a failed read on its own and the multiplexer reconnects underneath it, so its read
    // loops are the only place the host can learn that Redis went away and came back.
    private void _ReportRead(Exception? failure)
    {
        if (failure is null)
        {
            _reachability.ReportSuccess(OnLogCallback);
        }
        else
        {
            _reachability.ReportFailure(OnLogCallback, failure);
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
            _ReportRead,
            cancellationToken
        );

        await _ConsumeMessages(pendingMsgs, StreamPosition.Beginning, cancellationToken).ConfigureAwait(false);

        var stalePendingMsgs = redis.PollStreamsStalePendingMessagesAsync(
            _messageNames,
            _groupName,
            _consumerName.Name,
            options.Value.PendingClaimMinIdleTime,
            timeout,
            _ReportRead,
            cancellationToken
        );
        var stalePendingReads = _ConsumeMessages(stalePendingMsgs, StreamPosition.Beginning, cancellationToken);
        _ObserveBackgroundHandler(stalePendingReads);

        //Once we consumed our history, we can start getting new messages.
        var newMsgs = redis.PollStreamsLatestMessagesAsync(
            _messageNames,
            _groupName,
            _consumerName.Name,
            timeout,
            _ReportRead,
            cancellationToken
        );

        var newReads = _ConsumeMessages(newMsgs, StreamPosition.NewMessages, cancellationToken);
        _ObserveBackgroundHandler(newReads);

        // Awaited so listening, which the shutdown drain waits for, ends only once neither read can start a handler.
        await Task.WhenAll(stalePendingReads, newReads).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
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
                    message = _CreateMessage(stream.Key.ToString(), entry);
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

    /// <summary>The message name whose stream is <paramref name="streamKey"/>, when this client subscribed it.</summary>
    internal string? ArrivalName(string streamKey) => _arrivalNames.GetValueOrDefault(streamKey);

    /// <summary>
    /// Identifies an entry by its stream and id. Redis has no producer message id field, and an entry id is fixed for
    /// the entry's life, so a redelivery from the pending list carries the same identity.
    /// </summary>
    internal static NativeEnvelopeFacts NativeFacts(string streamKey, string entryId) =>
        new(
            ProducerMessageId: null,
            PositionId: NativeEnvelopeFacts.Position("redis", streamKey, entryId),
            StoredAt: RedisMessage.StoredAt(entryId)
        );

    /// <summary>
    /// Builds the transport message of <paramref name="entry"/>: reads its fields, applies the custom headers builder,
    /// stamps the address, then completes and checks the envelope. Every failure means the entry is malformed.
    /// </summary>
    private TransportMessage _CreateMessage(string streamKey, StreamEntry entry)
    {
        var arrivalName = ArrivalName(streamKey);
        var message = RedisMessage.Create(entry, headersOptional: _envelopeGate.IsRaw(lane, arrivalName));

        if (options.Value.CustomHeadersBuilder is { } customHeadersBuilder)
        {
            foreach (var customHeader in customHeadersBuilder(entry, _serviceProvider))
            {
                message.Headers[customHeader.Key] = customHeader.Value;
            }
        }

        // Stamped after the custom headers builder so neither the wire nor the builder can choose the address.
        message.Headers[Headers.TransportAddress] = streamKey;

        var entryId = entry.Id.ToString();
        try
        {
            _envelopeGate.Admit(
                message.Headers,
                new EnvelopeArrival(_TransportName, lane, arrivalName),
                (StreamKey: streamKey, EntryId: entryId),
                static position => NativeFacts(position.StreamKey, position.EntryId)
            );
        }
        catch (InvalidDataException ex)
        {
            throw new RedisConsumeInvalidHeadersException(entryId, ex);
        }

        return message;
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
            // Only the envelope gate wraps an InvalidDataException. Its reason tells a missing id from a conflicting
            // name or an over-long id, and quotes no header value except a sanitized, bounded message name, so it is
            // kept; it is sanitized again here because it reaches logs. A JSON parse failure keeps the generic text
            // because the serializer's message can quote the raw headers field.
            RedisConsumeInvalidHeadersException { InnerException: InvalidDataException rejection } =>
                new RedisConsumeInvalidHeadersException(
                    entryId,
                    new InvalidDataException(LogSanitizer.Sanitize(rejection.Message, _MaxLoggedReasonLength))
                ),
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

/// <summary>A service provider with no services, for a client built without a container.</summary>
file sealed class NoServices : IServiceProvider
{
    public object? GetService(Type serviceType) => null;
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
