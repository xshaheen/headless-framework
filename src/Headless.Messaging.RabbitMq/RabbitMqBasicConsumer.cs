// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Headless.Messaging.Transport;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Headless.Messaging.RabbitMq;

/// <summary>
/// An AMQP consumer that adapts RabbitMQ delivery events to the framework's <c>TransportMessage</c>
/// model and dispatches them to the registered message handler.
/// </summary>
/// <remarks>
/// When <paramref name="concurrent"/> is greater than zero, each delivery is dispatched on a
/// <c>Task.Run</c> thread pool task and a semaphore limits the number of in-flight handlers.
/// When <paramref name="concurrent"/> is zero, deliveries are handled sequentially on the calling
/// thread. On header or body parsing failure, or when <paramref name="envelopeGate"/> finds the envelope malformed, the
/// delivery is terminally rejected. When the callback throws without settling the delivery, the delivery is rejected
/// with requeue. Every dispatched handler is tracked in both modes, so shutdown can stop dispatching
/// and wait for the running ones before the channel closes.
/// </remarks>
/// <param name="arrivalNames">
/// The message name each subscribed routing key belongs to, built from the same routing keys the client bound, so a
/// delivery without the envelope headers can be matched to its contract without parsing the key.
/// </param>
internal sealed class RabbitMqBasicConsumer(
    IChannel channel,
    byte concurrent,
    Func<TransportMessage, object?, Task> msgCallback,
    Action<LogMessageEventArgs> logCallback,
    Func<BasicDeliverEventArgs, IServiceProvider, List<KeyValuePair<string, string>>>? customHeadersBuilder,
    IServiceProvider serviceProvider,
    Action<string>? onBrokerCancelled = null,
    TransportEnvelopeGate? envelopeGate = null,
    MessageLane lane = MessageLane.Bus,
    IReadOnlyDictionary<string, string>? arrivalNames = null
) : AsyncDefaultBasicConsumer(channel), IDisposable
{
    internal const string TransportName = "RabbitMQ";

    private readonly TransportEnvelopeGate _envelopeGate = envelopeGate ?? TransportEnvelopeGate.Strict;
    private readonly SemaphoreSlim _semaphore = new(concurrent);
    private readonly bool _usingTaskRun = concurrent > 0;

    // A delivery either registers before the drain takes its snapshot or sees dispatching stopped; none slips between.
    private readonly InFlightHandlerTracker _inFlightHandlers = new();

    // The delivery tags handed to the callback and not yet settled. The broker closes the channel over a tag it does not
    // know, so a second settlement of one delivery must never reach it. The client offsets tags across a channel
    // recovery, so a tag is never handed out twice and an entry left by a lost channel cannot match a new delivery.
    private readonly ConcurrentDictionary<ulong, byte> _unsettled = new();

    public override async Task HandleBasicDeliverAsync(
        string consumerTag,
        ulong deliveryTag,
        bool redelivered,
        string exchange,
        string routingKey,
        IReadOnlyBasicProperties properties,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken = default
    )
    {
        if (_usingTaskRun)
        {
            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            if (!_TryBeginHandler(out var handlerDone))
            {
                _ReleaseSemaphore();
                return;
            }

            // Copy of the body safe to use outside the RabbitMQ thread context
            ReadOnlyMemory<byte> safeBody = body.ToArray();
            _ObserveBackgroundHandler(
                Task.Run(
                    async () =>
                    {
                        try
                        {
                            await _Consume(
                                    consumerTag,
                                    deliveryTag,
                                    redelivered,
                                    exchange,
                                    routingKey,
                                    properties,
                                    safeBody
                                )
                                .ConfigureAwait(false);
                        }
                        finally
                        {
                            _EndHandler(handlerDone);
                            _ReleaseSemaphore();
                        }
                    },
                    CancellationToken.None // Ensure semaphore release even if cancellation is requested during handler execution
                )
            );
        }
        else
        {
            if (!_TryBeginHandler(out var handlerDone))
            {
                return;
            }

            try
            {
                await _Consume(consumerTag, deliveryTag, redelivered, exchange, routingKey, properties, body)
                    .ConfigureAwait(false);
            }
            finally
            {
                _EndHandler(handlerDone);
            }
        }
    }

    /// <summary>
    /// Stops dispatching, then waits up to <paramref name="timeout"/> for the handlers already running to finish. A
    /// delivery that arrives afterwards is left unacknowledged, and the broker returns it to the queue when the channel
    /// closes.
    /// </summary>
    /// <exception cref="TimeoutException">A handler was still running when <paramref name="timeout"/> elapsed.</exception>
    public Task DrainAsync(TimeSpan timeout, TimeProvider timeProvider)
    {
        return _inFlightHandlers.DrainAsync(timeout, timeProvider);
    }

    /// <summary>The number of handlers dispatched and not yet finished.</summary>
    internal int InFlightCount => _inFlightHandlers.Count;

    // A completion source stands in for the handler, so registration happens under the lock without running any of the
    // handler there; it always completes successfully, and the handler's own faults are logged where they occur.
    private bool _TryBeginHandler([NotNullWhen(true)] out TaskCompletionSource? handlerDone)
    {
        var candidate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_inFlightHandlers.TryTrack(candidate.Task))
        {
            handlerDone = null;
            return false;
        }

        handlerDone = candidate;
        return true;
    }

    private static void _EndHandler(TaskCompletionSource handlerDone)
    {
        handlerDone.TrySetResult();
    }

    private async Task _Consume(
        string consumerTag,
        ulong deliveryTag,
        bool redelivered,
        string exchange,
        string routingKey,
        IReadOnlyBasicProperties properties,
        ReadOnlyMemory<byte> body
    )
    {
        Dictionary<string, string?> headers;
        try
        {
            headers = ReadHeaders(properties.Headers);
        }
        catch (Exception ex)
        {
            await _TerminallyRejectMalformedEnvelopeAsync(deliveryTag, ex).ConfigureAwait(false);
            return;
        }

        if (customHeadersBuilder != null)
        {
            try
            {
                var e = new BasicDeliverEventArgs(
                    consumerTag,
                    deliveryTag,
                    redelivered,
                    exchange,
                    routingKey,
                    properties,
                    body
                );
                var customHeaders = customHeadersBuilder(e, serviceProvider);
                foreach (var customHeader in customHeaders)
                {
                    headers[customHeader.Key] = customHeader.Value;
                }
            }
            catch (Exception ex)
            {
                logCallback(
                    new LogMessageEventArgs
                    {
                        LogType = MqLogType.ConsumeError,
                        Reason =
                            $"RabbitMQ custom headers builder failed; delivery {deliveryTag} rejected for retry: {ex.GetType().Name}",
                    }
                );

                await _RejectForRetryAsync(deliveryTag).ConfigureAwait(false);
                return;
            }
        }

        // Stamped after the custom headers builder so neither the wire nor the builder can choose the address.
        headers[Headers.TransportAddress] = routingKey;

        TransportMessage message;
        try
        {
            var arrival = new EnvelopeArrival(
                TransportName,
                lane,
                arrivalNames is not null && arrivalNames.TryGetValue(routingKey, out var arrivalName)
                    ? arrivalName
                    : null
            );
            _envelopeGate.Admit(headers, arrival, properties, static p => ReadNativeFacts(p));
            message = new TransportMessage(headers, body);
        }
        catch (Exception ex)
        {
            await _TerminallyRejectMalformedEnvelopeAsync(deliveryTag, ex).ConfigureAwait(false);
            return;
        }

        _unsettled.TryAdd(deliveryTag, 0);

        try
        {
            await msgCallback(message, deliveryTag).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The core settles every handler outcome itself, so its callback throws only when that settlement failed or
            // the core broke. Nothing else will settle the delivery then, and an unsettled delivery on an open channel
            // is never redelivered and keeps its prefetch slot, so it goes back to the queue here.
            logCallback(
                new LogMessageEventArgs { LogType = MqLogType.ConsumeError, Reason = $"Error consuming message: {ex}" }
            );

            if (_unsettled.TryRemove(deliveryTag, out _))
            {
                await _RejectForRetryAsync(deliveryTag).ConfigureAwait(false);
            }
        }
    }

    private async Task _RejectForRetryAsync(ulong deliveryTag)
    {
        try
        {
            if (Channel.IsOpen)
            {
                await Channel.BasicRejectAsync(deliveryTag, requeue: true).ConfigureAwait(false);
            }
        }
        catch (Exception rejectException)
        {
            logCallback(
                new LogMessageEventArgs
                {
                    LogType = MqLogType.ConsumeError,
                    Reason =
                        $"Failed to reject RabbitMQ delivery {deliveryTag} for retry: {rejectException.GetType().Name}",
                }
            );
        }
    }

    private async Task _TerminallyRejectMalformedEnvelopeAsync(ulong deliveryTag, Exception exception)
    {
        logCallback(
            new LogMessageEventArgs
            {
                LogType = MqLogType.ConsumeError,
                Reason =
                    $"Malformed RabbitMQ transport envelope terminally rejected at delivery {deliveryTag}: {exception.GetType().Name}",
            }
        );

        try
        {
            if (Channel.IsOpen)
            {
                await Channel.BasicRejectAsync(deliveryTag, requeue: false).ConfigureAwait(false);
            }
        }
        catch (Exception rejectException)
        {
            logCallback(
                new LogMessageEventArgs
                {
                    LogType = MqLogType.ConsumeError,
                    Reason =
                        $"Failed to terminally reject malformed RabbitMQ delivery {deliveryTag}: {rejectException.GetType().Name}",
                }
            );
        }
    }

    /// <summary>Converts AMQP header values, which carry strings as UTF-8 bytes, to the framework's string headers.</summary>
    internal static Dictionary<string, string?> ReadHeaders(IDictionary<string, object?>? amqpHeaders)
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal);

        if (amqpHeaders is null)
        {
            return headers;
        }

        foreach (var header in amqpHeaders)
        {
            if (header.Value is byte[] val)
            {
                headers.Add(header.Key, Encoding.UTF8.GetString(val));
            }
            else
            {
                headers.Add(header.Key, header.Value?.ToString());
            }
        }

        return headers;
    }

    /// <summary>
    /// Reads the AMQP properties that complete a raw envelope: the producer's <c>message-id</c> and its
    /// <c>timestamp</c>.
    /// </summary>
    /// <remarks>
    /// There is no position fallback. A delivery tag is scoped to its channel, restarts at 1 on every channel, and changes
    /// when the broker redelivers, so it cannot identify a message; a raw delivery without <c>message-id</c> is therefore
    /// malformed. The AMQP timestamp is whole Unix seconds, and only the producer sets it.
    /// </remarks>
    internal static NativeEnvelopeFacts ReadNativeFacts(IReadOnlyBasicProperties properties)
    {
        var producerMessageId = properties.IsMessageIdPresent() ? properties.MessageId : null;
        DateTimeOffset? storedAt = properties.IsTimestampPresent()
            ? DateTimeOffset.FromUnixTimeSeconds(properties.Timestamp.UnixTime)
            : null;

        return new NativeEnvelopeFacts(producerMessageId, PositionId: null, storedAt);
    }

    /// <summary>
    /// Acknowledges a handled delivery. On a closed channel the acknowledgement cannot be sent and is not attempted:
    /// a delivery tag belongs to its channel, so the broker already requeued the message when the channel closed and
    /// redelivers it, which the inbox deduplicates. That lost acknowledgement is logged and counted, not thrown.
    /// </summary>
    /// <remarks>
    /// Each settlement method settles a delivery at most once and ignores a tag this consumer did not hand out or has
    /// already settled, because the broker closes the channel over a tag it does not know.
    /// </remarks>
    public async Task BasicAck(ulong deliveryTag, CancellationToken cancellationToken = default)
    {
        if (!_unsettled.TryRemove(deliveryTag, out _))
        {
            return;
        }

        if (Channel.IsOpen)
        {
            await Channel.BasicAckAsync(deliveryTag, multiple: false, cancellationToken).ConfigureAwait(false);
            return;
        }

        MessagingMetrics.RecordSettlementFailure(
            "rabbitmq",
            MessagingMetrics.OperationNameAck,
            MessagingMetrics.ErrorTypeChannelClosed
        );
        logCallback(
            new LogMessageEventArgs
            {
                LogType = MqLogType.SettlementFailed,
                Reason =
                    $"RabbitMQ message ACK for delivery tag {deliveryTag} skipped: the channel is closed, so the broker will redeliver it.",
            }
        );
    }

    public async Task BasicReject(ulong deliveryTag, CancellationToken cancellationToken = default)
    {
        if (_unsettled.TryRemove(deliveryTag, out _) && Channel.IsOpen)
        {
            await Channel.BasicRejectAsync(deliveryTag, requeue: true, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Rejects the delivery without requeue, so the queue's dead-letter exchange receives it when one is set, through
    /// <see cref="RabbitMqMessagingOptions.QueueArgumentsOptions.EnableDeadLettering"/> or a broker policy; without one,
    /// the broker discards it exactly as an acknowledgement would.
    /// </summary>
    public async Task BasicDeadLetter(ulong deliveryTag, CancellationToken cancellationToken = default)
    {
        if (_unsettled.TryRemove(deliveryTag, out _) && Channel.IsOpen)
        {
            await Channel.BasicRejectAsync(deliveryTag, requeue: false, cancellationToken).ConfigureAwait(false);
        }
    }

    protected override async Task OnCancelAsync(string[] consumerTags, CancellationToken cancellationToken = default)
    {
        await base.OnCancelAsync(consumerTags, cancellationToken).ConfigureAwait(false);

        var args = new LogMessageEventArgs
        {
            LogType = MqLogType.ConsumerCancelled,
            Reason = string.Join(',', consumerTags),
        };

        logCallback(args);
    }

    // basic.cancel from the broker, not a reply to this client's own BasicCancelAsync: the queue was deleted or the
    // node holding it went away, and the channel stays open while nothing is delivered any more.
    public override async Task HandleBasicCancelAsync(string consumerTag, CancellationToken cancellationToken = default)
    {
        await base.HandleBasicCancelAsync(consumerTag, cancellationToken).ConfigureAwait(false);

        onBrokerCancelled?.Invoke(consumerTag);
    }

    public override async Task HandleBasicCancelOkAsync(
        string consumerTag,
        CancellationToken cancellationToken = default
    )
    {
        await base.HandleBasicCancelOkAsync(consumerTag, cancellationToken).ConfigureAwait(false);

        var args = new LogMessageEventArgs { LogType = MqLogType.ConsumerUnregistered, Reason = consumerTag };

        logCallback(args);
    }

    public override async Task HandleBasicConsumeOkAsync(
        string consumerTag,
        CancellationToken cancellationToken = default
    )
    {
        await base.HandleBasicConsumeOkAsync(consumerTag, cancellationToken).ConfigureAwait(false);

        var args = new LogMessageEventArgs { LogType = MqLogType.ConsumerRegistered, Reason = consumerTag };

        logCallback(args);
    }

    public override async Task HandleChannelShutdownAsync(object channel, ShutdownEventArgs reason)
    {
        await base.HandleChannelShutdownAsync(channel, reason).ConfigureAwait(false);

        var args = new LogMessageEventArgs { LogType = MqLogType.ConsumerShutdown, Reason = reason.ReplyText };

        logCallback(args);
    }

    private void _ReleaseSemaphore()
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
            // Shutdown in progress
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
                    logCallback(
                        new LogMessageEventArgs
                        {
                            LogType = MqLogType.ConsumeError,
                            Reason = $"Error consuming message: {exception}",
                        }
                    );
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
    }

    public void Dispose()
    {
        _semaphore.Dispose();
    }
}
