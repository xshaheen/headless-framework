// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;

namespace Headless.Messaging.Nats;

/// <summary>
/// A process's NATS reply channel: a core subscription, outside JetStream, on one subject under
/// <see cref="ReplyAddresses.Prefix"/>, held on a connection of the shared pool.
/// </summary>
/// <remarks>
/// <para>
/// A core subscription keeps nothing on the server once it ends: unsubscribing, closing the connection, or the process
/// dying removes it, so no reply object is left behind. A reply published while nobody is subscribed is discarded by the
/// server, and the call it answered times out.
/// </para>
/// <para>
/// The address stays the same for the listener's whole life. The client re-sends every subscription after it
/// reconnects, so the subject is live again under the same name, and nothing else can hold it: a core subject has no
/// owner to wait out, unlike an exclusive queue. While the connection is down, <see cref="WaitForAddressAsync"/> waits
/// for it to come back, because a reply published during the outage reaches nobody. A call already waiting keeps
/// waiting and completes if its reply arrives after the reconnect; one whose reply was published during the outage
/// times out.
/// </para>
/// <para>
/// Subscribing opens the pooled connection when it is not open yet. That happens in the background, so a server that
/// is unreachable at startup delays calls, which wait for the address inside their own timeout, instead of failing the
/// host.
/// </para>
/// </remarks>
internal sealed class NatsReplyListener : IReplyListener
{
    // Long enough for a healthy JetStream API to answer, short enough that a stalled one is forgotten quickly.
    private static readonly TimeSpan _StreamCheckBound = TimeSpan.FromSeconds(5);

    private readonly NatsConnection _connection;
    private readonly string _subject;
    private readonly Func<TransportMessage, CancellationToken, ValueTask> _onReply;
    private readonly ReplyListenerBackoff _backoff;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _closing = new();
    private readonly Task _maintain;

    // Unresolved while the subject is not live on the server; resolved with the subject once it is.
    private readonly ReplyAddressGate _address = new();
    private INatsSub<ReadOnlyMemory<byte>>? _subscription;
    private int _disposed;

    public NatsReplyListener(
        NatsConnection connection,
        Func<TransportMessage, CancellationToken, ValueTask> onReply,
        TimeProvider timeProvider,
        ILogger logger
    )
    {
        _connection = connection;
        _subject = ReplyAddresses.Create();
        _onReply = onReply;
        _backoff = new ReplyListenerBackoff(timeProvider);
        _logger = logger;

        _connection.ConnectionDisconnected += _OnConnectionDisconnectedAsync;
        _connection.ConnectionOpened += _OnConnectionOpenedAsync;
        _connection.MessageDropped += _OnMessageDroppedAsync;

        _maintain = Task.Run(_MaintainAsync);
    }

    /// <inheritdoc />
    /// <remarks>The address never changes; while the connection is down this waits for it to be re-established.</remarks>
    public ValueTask<string> WaitForAddressAsync(CancellationToken cancellationToken = default)
    {
        Ensure.NotDisposed(Volatile.Read(ref _disposed) != 0, this);

        return _address.WaitAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        // The connection is shared and outlives this listener, so its events must stop reaching it.
        _connection.ConnectionDisconnected -= _OnConnectionDisconnectedAsync;
        _connection.ConnectionOpened -= _OnConnectionOpenedAsync;
        _connection.MessageDropped -= _OnMessageDroppedAsync;

        // A call still waiting for the address learns that the listener closed.
        _address.FailOnDispose(nameof(NatsReplyListener));

        await _closing.CancelAsync().ConfigureAwait(false);

        // The loop unsubscribes on its way out, which removes the subject from the server.
        await _maintain.ConfigureAwait(false);
        _closing.Dispose();
    }

    private async Task _MaintainAsync()
    {
        while (!_closing.IsCancellationRequested)
        {
            INatsSub<ReadOnlyMemory<byte>>? subscription = null;

            try
            {
                subscription = await _connection
                    .SubscribeCoreAsync(
                        _subject,
                        serializer: NatsRawSerializer<ReadOnlyMemory<byte>>.Default,
                        // The client ends the subscription and completes its channel when this token is cancelled.
                        cancellationToken: _closing.Token
                    )
                    .ConfigureAwait(false);
                Volatile.Write(ref _subscription, subscription);

                // The server handles a connection's protocol in order, so its PONG proves it registered the SUB above:
                // a reply published after the address is handed out cannot miss this process.
                await _connection.PingAsync(_closing.Token).ConfigureAwait(false);
                _PublishAddressIfConnected();
                _backoff.Reset();
                _logger.ReplyListenerReady(_subject);

                // Runs beside the receive loop, after the address is out, so a slow or absent JetStream API never
                // delays a call.
                _ = _WarnWhenAStreamCapturesRepliesAsync();

                await foreach (var msg in subscription.Msgs.ReadAllAsync(_closing.Token).ConfigureAwait(false))
                {
                    await _DeliverAsync(msg).ConfigureAwait(false);
                }

                if (_closing.IsCancellationRequested)
                {
                    return;
                }

                // The client completes a subscription's channel only when the subscription or its connection ends,
                // which a reconnect does not do; subscribe again on the same subject.
                _address.Retract();
                _logger.ReplyListenerLost(_subject);
            }
            catch (OperationCanceledException) when (_closing.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                _address.Retract();
                _logger.ReplyListenerOpenFailed(e, _subject, _backoff.Delay);
            }
            finally
            {
                Volatile.Write(ref _subscription, null);

                if (subscription is not null)
                {
                    await _UnsubscribeAsync(subscription).ConfigureAwait(false);
                }
            }

            // A lost subscription backs off too, not only a failed subscribe: a server that keeps accepting the
            // subscription and then ending it would otherwise drive a tight resubscribe loop. A successful subscribe
            // resets the delay.
            if (!await _backoff.WaitAsync(_closing.Token).ConfigureAwait(false))
            {
                return;
            }
        }
    }

    // A reply is a core publish, but a stream whose subject filter covers the reply subject stores a copy of every
    // reply, and an operator rarely intends that. The check is best effort: it only warns, it is bounded so an
    // unresponsive JetStream API cannot hold the listener, and any failure, including a server without JetStream, is
    // logged at debug and otherwise ignored.
    private async Task _WarnWhenAStreamCapturesRepliesAsync()
    {
        try
        {
            using var bound = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token);
            bound.CancelAfter(_StreamCheckBound);

            var streams = new List<string>();
            await foreach (
                var stream in new NatsJSContext(_connection)
                    .ListStreamNamesAsync(_subject, bound.Token)
                    .ConfigureAwait(false)
            )
            {
                streams.Add(stream);
            }

            if (streams.Count == 0)
            {
                return;
            }

            if (_logger.IsEnabled(LogLevel.Warning))
            {
                var names = string.Join(", ", streams);
                _logger.ReplySubjectCapturedByStreams(_subject, names);
            }
        }
        catch (Exception e)
        {
            // While closing, the check's outcome no longer matters to anyone.
            if (!_closing.IsCancellationRequested && _logger.IsEnabled(LogLevel.Debug))
            {
                var exceptionType = e.GetType().Name;
                _logger.ReplySubjectStreamCheckSkipped(_subject, exceptionType);
            }
        }
    }

    // The client sets the connection state before it raises the disconnect event, and that event retracts under the
    // same lock, so an address published here is never left standing for a connection that is down.
    private void _PublishAddressIfConnected()
    {
        _address.Publish(_subject, () => _connection.ConnectionState is NatsConnectionState.Open);
    }

    // Makes callers wait for the subject to be live again instead of sending a request whose reply reaches nobody.
    private ValueTask _OnConnectionDisconnectedAsync(object? sender, NatsEventArgs args)
    {
        _address.Retract();
        return ValueTask.CompletedTask;
    }

    // The client re-sends every SUB before it raises this event, so the subject is live again once the server answers
    // a PING sent after them.
    private async ValueTask _OnConnectionOpenedAsync(object? sender, NatsEventArgs args)
    {
        if (Volatile.Read(ref _subscription) is null || Volatile.Read(ref _disposed) != 0)
        {
            // Not subscribed yet: the maintenance loop hands out the address once it is.
            return;
        }

        try
        {
            await _connection.PingAsync(_closing.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
            // Closing: nobody waits for the address any more.
            return;
        }
        catch (Exception e)
        {
            // A throwing event handler would reach the client's event loop. The SUB was already re-sent, so the
            // address is handed out unconfirmed rather than withheld until a reconnect that may never come.
            _logger.ReplyListenerResubscribeCheckFailed(e, _subject);
        }

        _PublishAddressIfConnected();
        _logger.ReplyListenerResubscribed(_subject);
    }

    // A subscription buffers deliveries in a bounded channel that drops the newest message once it is full. A dropped
    // reply leaves its call to time out, so name the cause where an operator can see it.
    private ValueTask _OnMessageDroppedAsync(object? sender, NatsMessageDroppedEventArgs args)
    {
        if (ReferenceEquals(args.Subscription, Volatile.Read(ref _subscription)))
        {
            _logger.ReplyDropped(_subject, args.Pending);
        }

        return ValueTask.CompletedTask;
    }

    private async Task _DeliverAsync(NatsMsg<ReadOnlyMemory<byte>> msg)
    {
        TransportMessage reply;
        try
        {
            reply = new TransportMessage(NatsTransport.ReadHeaders(msg.Headers), msg.Data);
        }
        catch (Exception e)
        {
            _logger.ReplyUnreadable(e, _subject);
            return;
        }

        await ReplyHandlerInvoker
            .InvokeAsync(
                _onReply,
                reply,
                (Logger: _logger, Address: _subject),
                static (state, e) => state.Logger.ReplyHandlerFailed(e, state.Address),
                _closing.Token
            )
            .ConfigureAwait(false);
    }

    private async Task _UnsubscribeAsync(INatsSub<ReadOnlyMemory<byte>> subscription)
    {
        try
        {
            await subscription.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            // A connection that is gone or closing already dropped the subscription on the server.
            _logger.ReplyListenerUnsubscribeFailed(e, _subject);
        }
    }
}

internal static partial class NatsReplyListenerLog
{
    [LoggerMessage(
        EventId = 4,
        EventName = "NatsReplyListenerReady",
        Level = LogLevel.Debug,
        Message = "NATS reply listener is subscribed to reply subject '{ReplyAddress}'."
    )]
    public static partial void ReplyListenerReady(this ILogger logger, string replyAddress);

    [LoggerMessage(
        EventId = 5,
        EventName = "NatsReplyListenerLost",
        Level = LogLevel.Warning,
        Message = "The NATS subscription to reply subject '{ReplyAddress}' ended; the listener subscribes again under the same address."
    )]
    public static partial void ReplyListenerLost(this ILogger logger, string replyAddress);

    [LoggerMessage(
        EventId = 6,
        EventName = "NatsReplyListenerOpenFailed",
        Level = LogLevel.Error,
        Message = "NATS reply listener failed to subscribe to reply subject '{ReplyAddress}'; retrying in {RetryDelay}."
    )]
    public static partial void ReplyListenerOpenFailed(
        this ILogger logger,
        Exception exception,
        string replyAddress,
        TimeSpan retryDelay
    );

    [LoggerMessage(
        EventId = 7,
        EventName = "NatsReplyListenerResubscribed",
        Level = LogLevel.Information,
        Message = "NATS reply subject '{ReplyAddress}' is live again after a reconnect; replies published while the connection was down were lost."
    )]
    public static partial void ReplyListenerResubscribed(this ILogger logger, string replyAddress);

    [LoggerMessage(
        EventId = 8,
        EventName = "NatsReplyListenerResubscribeCheckFailed",
        Level = LogLevel.Warning,
        Message = "Confirming NATS reply subject '{ReplyAddress}' after a reconnect failed; the address is handed out without that confirmation."
    )]
    public static partial void ReplyListenerResubscribeCheckFailed(
        this ILogger logger,
        Exception exception,
        string replyAddress
    );

    [LoggerMessage(
        EventId = 9,
        EventName = "NatsReplyDropped",
        Level = LogLevel.Warning,
        Message = "NATS reply subject '{ReplyAddress}' dropped a reply because {Pending} replies were already waiting; the call it answered times out."
    )]
    public static partial void ReplyDropped(this ILogger logger, string replyAddress, int pending);

    [LoggerMessage(
        EventId = 10,
        EventName = "NatsReplyUnreadable",
        Level = LogLevel.Warning,
        Message = "A reply on NATS reply subject '{ReplyAddress}' could not be read and was dropped."
    )]
    public static partial void ReplyUnreadable(this ILogger logger, Exception exception, string replyAddress);

    [LoggerMessage(
        EventId = 11,
        EventName = "NatsReplyHandlerFailed",
        Level = LogLevel.Error,
        Message = "The reply handler of NATS reply subject '{ReplyAddress}' threw; the listener keeps receiving."
    )]
    public static partial void ReplyHandlerFailed(this ILogger logger, Exception exception, string replyAddress);

    [LoggerMessage(
        EventId = 12,
        EventName = "NatsReplyListenerUnsubscribeFailed",
        Level = LogLevel.Debug,
        Message = "Unsubscribing NATS reply subject '{ReplyAddress}' failed."
    )]
    public static partial void ReplyListenerUnsubscribeFailed(
        this ILogger logger,
        Exception exception,
        string replyAddress
    );

    [LoggerMessage(
        EventId = 13,
        EventName = "NatsReplySubjectCapturedByStreams",
        Level = LogLevel.Warning,
        Message = "NATS reply subject '{ReplyAddress}' is captured by JetStream stream(s) {Streams}; every reply to this host is stored there. Narrow the stream's subjects so they exclude 'headless.reply.>'."
    )]
    public static partial void ReplySubjectCapturedByStreams(this ILogger logger, string replyAddress, string streams);

    [LoggerMessage(
        EventId = 14,
        EventName = "NatsReplySubjectStreamCheckSkipped",
        Level = LogLevel.Debug,
        Message = "Could not check whether a JetStream stream captures NATS reply subject '{ReplyAddress}' ({ExceptionType}); the check is best effort and the listener is unaffected."
    )]
    public static partial void ReplySubjectStreamCheckSkipped(
        this ILogger logger,
        string replyAddress,
        string exceptionType
    );
}
