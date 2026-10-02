// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Headless.Messaging.Redis;

/// <summary>
/// A process's Redis reply channel: a pub/sub subscription to one literal channel under
/// <see cref="ReplyAddresses.Prefix"/>, held on a multiplexer of the shared connection pool.
/// </summary>
/// <remarks>
/// <para>
/// A subscription keeps nothing on the server once it ends: unsubscribing, closing the connection, or the process dying
/// removes it, and a subscription never creates a key, so no reply object is left behind.
/// </para>
/// <para>
/// The address stays the same for the listener's whole life. The multiplexer re-subscribes every channel after it
/// reconnects, so the channel is live again under the same name, and nothing else can hold it: a channel has no owner
/// to wait out, unlike an exclusive queue. While the subscription connection is down, <see cref="WaitForAddressAsync"/>
/// waits for it to come back, because a reply published during the outage reaches nobody. A call already waiting keeps
/// waiting and completes if its reply arrives after the reconnect; one whose reply was published during the outage
/// times out.
/// </para>
/// <para>
/// Connecting and subscribing happen in the background, so a server that is unreachable at startup delays calls, which
/// wait for the address inside their own timeout, instead of failing the host.
/// </para>
/// </remarks>
internal sealed class RedisReplyListener : IReplyListener
{
    private readonly IRedisConnectionPool _connectionPool;
    private readonly string _replyAddress;
    private readonly RedisChannel _channel;
    private readonly Func<TransportMessage, CancellationToken, ValueTask> _onReply;
    private readonly ReplyListenerBackoff _backoff;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _closing = new();
    private readonly Task _maintain;

    // Unresolved while the channel is not live on the server; resolved with the address once it is.
    private readonly ReplyAddressGate _address = new();

    // Guards which multiplexer the listener follows, so closing cannot race the loop attaching to a new one.
    private readonly Lock _followLock = new();

    // The multiplexer whose connection events this listener follows, and the subscriber of the live subscription.
    private IConnectionMultiplexer? _connection;
    private ISubscriber? _subscriber;
    private int _disposed;

    public RedisReplyListener(
        IRedisConnectionPool connectionPool,
        Func<TransportMessage, CancellationToken, ValueTask> onReply,
        TimeProvider timeProvider,
        ILogger logger
    )
    {
        _connectionPool = connectionPool;
        _replyAddress = ReplyAddresses.Create();
        // Literal, so the address is matched exactly and never read as a pattern.
        _channel = RedisChannel.Literal(_replyAddress);
        _onReply = onReply;
        _backoff = new ReplyListenerBackoff(timeProvider);
        _logger = logger;

        _maintain = Task.Run(_MaintainAsync);
    }

    /// <inheritdoc />
    /// <remarks>The address never changes; while the subscription is down this waits for it to be re-established.</remarks>
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

        lock (_followLock)
        {
            // The multiplexer is shared and outlives this listener, so its events must stop reaching it.
            _Follow(connection: null);
        }

        // A call still waiting for the address learns that the listener closed.
        _address.FailOnDispose(nameof(RedisReplyListener));

        await _closing.CancelAsync().ConfigureAwait(false);

        // The loop unsubscribes on its way out, which removes the channel from the server.
        await _maintain.ConfigureAwait(false);
        _closing.Dispose();
    }

    private async Task _MaintainAsync()
    {
        while (!_closing.IsCancellationRequested)
        {
            ISubscriber? subscriber = null;

            try
            {
                var connection = await _connectionPool.ConnectAsync(_closing.Token).ConfigureAwait(false);
                lock (_followLock)
                {
                    if (Volatile.Read(ref _disposed) != 0)
                    {
                        return;
                    }

                    _Follow(connection);
                }

                subscriber = connection.GetSubscriber();
                var queue = await subscriber.SubscribeAsync(_channel).WaitAsync(_closing.Token).ConfigureAwait(false);

                // The server answers a connection's commands in order, so this reply proves it registered the
                // subscription above: a reply published after the address is handed out cannot miss this process.
                await subscriber.PingAsync().WaitAsync(_closing.Token).ConfigureAwait(false);
                Volatile.Write(ref _subscriber, subscriber);
                _PublishAddressIfConnected();
                _backoff.Reset();
                _logger.ReplyListenerReady(_replyAddress);

                await foreach (var message in queue.WithCancellation(_closing.Token).ConfigureAwait(false))
                {
                    await _DeliverAsync(message.Message).ConfigureAwait(false);
                }

                if (_closing.IsCancellationRequested)
                {
                    return;
                }

                // The queue completes only when the channel is unsubscribed or its multiplexer is disposed, which a
                // reconnect does not do; subscribe again on the same channel, through whichever multiplexer the pool
                // hands out.
                _address.Retract();
                _logger.ReplyListenerLost(_replyAddress, _backoff.Delay);
            }
            catch (OperationCanceledException) when (_closing.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                _address.Retract();
                _logger.ReplyListenerOpenFailed(e, _replyAddress, _backoff.Delay);
            }
            finally
            {
                Volatile.Write(ref _subscriber, null);

                if (subscriber is not null)
                {
                    await _UnsubscribeAsync(subscriber).ConfigureAwait(false);
                }
            }

            if (!await _backoff.WaitAsync(_closing.Token).ConfigureAwait(false))
            {
                return;
            }
        }
    }

    // Moves the connection-event handlers to the multiplexer the subscription lives on. Called under the follow lock.
    private void _Follow(IConnectionMultiplexer? connection)
    {
        if (ReferenceEquals(_connection, connection))
        {
            return;
        }

        if (_connection is not null)
        {
            _connection.ConnectionFailed -= _OnConnectionFailed;
            _connection.ConnectionRestored -= _OnConnectionRestored;
        }

        _connection = connection;

        if (connection is not null)
        {
            connection.ConnectionFailed += _OnConnectionFailed;
            connection.ConnectionRestored += _OnConnectionRestored;
        }
    }

    // The multiplexer marks a connection down before it raises the failure event, and the retraction below checks the
    // subscription's state under the same lock, so an address published here is never left standing for a
    // subscription that is down.
    private void _PublishAddressIfConnected()
    {
        _address.Publish(_replyAddress, _IsSubscriptionLive);
    }

    // Makes callers wait for the channel to be live again instead of sending a request whose reply reaches nobody.
    // A failure event can arrive after the multiplexer already reconnected, or concern only the interactive connection;
    // retracting then would withhold the address until a reconnect that never comes, so an event retracts only while
    // the subscription is actually down.
    private void _OnConnectionFailed(object? sender, ConnectionFailedEventArgs args)
    {
        _address.Retract(isStillLive: _IsSubscriptionLive);
    }

    private bool _IsSubscriptionLive()
    {
        return Volatile.Read(ref _subscriber) is { } subscriber && subscriber.IsConnected(_channel);
    }

    // The multiplexer re-sends every subscription once a connection is back, so the channel is live again once the
    // server answers a ping sent after them on the subscription connection.
    private void _OnConnectionRestored(object? sender, ConnectionFailedEventArgs args)
    {
        if (Volatile.Read(ref _subscriber) is not { } subscriber || Volatile.Read(ref _disposed) != 0)
        {
            // Not subscribed yet: the maintenance loop hands out the address once it is.
            return;
        }

        _ = _ConfirmResubscribedAsync(subscriber);
    }

    private async Task _ConfirmResubscribedAsync(ISubscriber subscriber)
    {
        try
        {
            await subscriber.PingAsync().WaitAsync(_closing.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Closing: nobody waits for the address any more.
            return;
        }
        catch (ObjectDisposedException)
        {
            // Closing: the listener's token source is gone.
            return;
        }
        catch (Exception e)
        {
            // The subscription was already re-sent, so the address is handed out unconfirmed rather than withheld
            // until a reconnect that may never come.
            _logger.ReplyListenerResubscribeCheckFailed(e, _replyAddress);
        }

        _PublishAddressIfConnected();
        _logger.ReplyListenerResubscribed(_replyAddress);
    }

    private async Task _DeliverAsync(RedisValue payload)
    {
        TransportMessage reply;
        try
        {
            reply = RedisMessage.CreateReply(((ReadOnlyMemory<byte>)payload).Span);
        }
        catch (Exception e)
        {
            _logger.ReplyUnreadable(e, _replyAddress);
            return;
        }

        await ReplyHandlerInvoker
            .InvokeAsync(
                _onReply,
                reply,
                (Logger: _logger, Address: _replyAddress),
                static (state, e) => state.Logger.ReplyHandlerFailed(e, state.Address),
                _closing.Token
            )
            .ConfigureAwait(false);
    }

    // Unsubscribing by channel also drops the local registration of a subscribe that failed half-way; the channel is
    // this listener's alone, so nothing else is unsubscribed.
    private async Task _UnsubscribeAsync(ISubscriber subscriber)
    {
        try
        {
            await subscriber.UnsubscribeAsync(_channel).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            // A connection that is gone already dropped the subscription on the server.
            _logger.ReplyListenerUnsubscribeFailed(e, _replyAddress);
        }
    }
}

internal static partial class RedisReplyListenerLog
{
    [LoggerMessage(
        EventId = 3011,
        EventName = "RedisReplyListenerReady",
        Level = LogLevel.Debug,
        Message = "Redis reply listener is subscribed to reply channel '{ReplyAddress}'."
    )]
    public static partial void ReplyListenerReady(this ILogger logger, string replyAddress);

    [LoggerMessage(
        EventId = 3012,
        EventName = "RedisReplyListenerLost",
        Level = LogLevel.Warning,
        Message = "The Redis subscription to reply channel '{ReplyAddress}' ended; the listener subscribes again under the same address in {RetryDelay}."
    )]
    public static partial void ReplyListenerLost(this ILogger logger, string replyAddress, TimeSpan retryDelay);

    [LoggerMessage(
        EventId = 3013,
        EventName = "RedisReplyListenerOpenFailed",
        Level = LogLevel.Error,
        Message = "Redis reply listener failed to subscribe to reply channel '{ReplyAddress}'; retrying in {RetryDelay}."
    )]
    public static partial void ReplyListenerOpenFailed(
        this ILogger logger,
        Exception exception,
        string replyAddress,
        TimeSpan retryDelay
    );

    [LoggerMessage(
        EventId = 3014,
        EventName = "RedisReplyListenerResubscribed",
        Level = LogLevel.Information,
        Message = "Redis reply channel '{ReplyAddress}' is live again after a reconnect; replies published while the connection was down were lost."
    )]
    public static partial void ReplyListenerResubscribed(this ILogger logger, string replyAddress);

    [LoggerMessage(
        EventId = 3015,
        EventName = "RedisReplyListenerResubscribeCheckFailed",
        Level = LogLevel.Warning,
        Message = "Confirming Redis reply channel '{ReplyAddress}' after a reconnect failed; the address is handed out without that confirmation."
    )]
    public static partial void ReplyListenerResubscribeCheckFailed(
        this ILogger logger,
        Exception exception,
        string replyAddress
    );

    [LoggerMessage(
        EventId = 3016,
        EventName = "RedisReplyUnreadable",
        Level = LogLevel.Warning,
        Message = "A reply on Redis reply channel '{ReplyAddress}' could not be read and was dropped."
    )]
    public static partial void ReplyUnreadable(this ILogger logger, Exception exception, string replyAddress);

    [LoggerMessage(
        EventId = 3017,
        EventName = "RedisReplyHandlerFailed",
        Level = LogLevel.Error,
        Message = "The reply handler of Redis reply channel '{ReplyAddress}' threw; the listener keeps receiving."
    )]
    public static partial void ReplyHandlerFailed(this ILogger logger, Exception exception, string replyAddress);

    [LoggerMessage(
        EventId = 3018,
        EventName = "RedisReplyListenerUnsubscribeFailed",
        Level = LogLevel.Debug,
        Message = "Unsubscribing Redis reply channel '{ReplyAddress}' failed."
    )]
    public static partial void ReplyListenerUnsubscribeFailed(
        this ILogger logger,
        Exception exception,
        string replyAddress
    );
}
