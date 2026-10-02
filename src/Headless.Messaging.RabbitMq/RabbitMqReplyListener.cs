// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Headless.Messaging.RabbitMq;

/// <summary>
/// A process's RabbitMQ reply channel: an exclusive queue named under <see cref="ReplyAddresses.Prefix"/>, consumed with
/// automatic acknowledgement on a connection the listener owns.
/// </summary>
/// <remarks>
/// <para>
/// The queue is exclusive to the listener's connection, so the broker deletes it when the listener closes the connection
/// or the process dies; nothing is left behind for an operator to clean up. Replies are acknowledged on delivery, as a
/// reply is delivered at most once: a lost reply ends its call in a timeout, never in a redelivery.
/// </para>
/// <para>
/// The connection does not recover on its own. When the connection, the channel, or the consumer is lost, the listener
/// opens a new connection and declares a new queue under a <b>new</b> address, and <see cref="WaitForAddressAsync"/>
/// returns the new address once it is consumed. A call sent with the old address times out: its queue died with the
/// old connection, and a reply to it is discarded by the broker. A fresh address, rather than the old name re-declared,
/// lets the listener recover at once: until the broker notices the old connection is dead, which can take a heartbeat
/// timeout after a network failure, it still holds the old exclusive queue and refuses to declare that name again.
/// </para>
/// </remarks>
internal sealed class RabbitMqReplyListener : IReplyListener
{
    private static readonly TimeSpan _FirstReconnectDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan _MaxReconnectDelay = TimeSpan.FromSeconds(30);

    private readonly IConnectionChannelPool _connectionChannelPool;
    private readonly Func<TransportMessage, CancellationToken, ValueTask> _onReply;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _closing = new();
    private readonly Lock _addressLock = new();
    private readonly Task _maintain;

    // Unresolved while the listener connects; resolved with the address its current queue is consumed under.
    private TaskCompletionSource<string> _address = _NewAddressSource();
    private int _disposed;

    public RabbitMqReplyListener(
        IConnectionChannelPool connectionChannelPool,
        Func<TransportMessage, CancellationToken, ValueTask> onReply,
        TimeProvider timeProvider,
        ILogger logger
    )
    {
        _connectionChannelPool = connectionChannelPool;
        _onReply = onReply;
        _timeProvider = timeProvider;
        _logger = logger;

        // Connecting runs in the background so a broker that is briefly unreachable at startup delays calls, which
        // wait for the address inside their own timeout, instead of failing the host.
        _maintain = Task.Run(_MaintainAsync);
    }

    /// <inheritdoc />
    /// <remarks>
    /// After a connection loss this waits for the re-declared queue, whose address differs from the previous one;
    /// calls already sent with the previous address time out.
    /// </remarks>
    public ValueTask<string> WaitForAddressAsync(CancellationToken cancellationToken = default)
    {
        Ensure.NotDisposed(Volatile.Read(ref _disposed) != 0, this);

        TaskCompletionSource<string> address;
        lock (_addressLock)
        {
            address = _address;
        }

        return new ValueTask<string>(address.Task.WaitAsync(cancellationToken));
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        lock (_addressLock)
        {
            // A call still waiting for the first address learns that the listener closed.
            if (_address.TrySetException(new ObjectDisposedException(nameof(RabbitMqReplyListener))))
            {
                _ = _address.Task.Exception;
            }
        }

        await _closing.CancelAsync().ConfigureAwait(false);

        // The loop closes the connection on its way out, which deletes the exclusive queue.
        await _maintain.ConfigureAwait(false);
        _closing.Dispose();
    }

    private async Task _MaintainAsync()
    {
        var reconnectDelay = _FirstReconnectDelay;

        while (!_closing.IsCancellationRequested)
        {
            var lost = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            IConnection? connection = null;
            IChannel? channel = null;
            string? address = null;
            var failed = false;

            try
            {
                connection = await _connectionChannelPool
                    .CreateNonRecoveringConnectionAsync(_closing.Token)
                    .ConfigureAwait(false);
                connection.ConnectionShutdownAsync += (_, args) =>
                {
                    lost.TrySetResult($"the connection shut down: {args.ReplyText}");
                    return Task.CompletedTask;
                };

                channel = await connection.CreateChannelAsync(cancellationToken: _closing.Token).ConfigureAwait(false);
                address = ReplyAddresses.Create();
                await channel
                    .QueueDeclareAsync(
                        address,
                        durable: false,
                        exclusive: true,
                        autoDelete: false,
                        arguments: null,
                        cancellationToken: _closing.Token
                    )
                    .ConfigureAwait(false);
                await channel
                    .BasicConsumeAsync(
                        address,
                        autoAck: true,
                        new ReplyConsumer(channel, this, address, lost),
                        _closing.Token
                    )
                    .ConfigureAwait(false);

                _PublishAddress(address);
                reconnectDelay = _FirstReconnectDelay;
                _logger.ReplyListenerReady(address);

                var reason = await lost.Task.WaitAsync(_closing.Token).ConfigureAwait(false);
                _RetractAddress();
                _logger.ReplyListenerLost(address, reason);
            }
            catch (OperationCanceledException) when (_closing.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                _RetractAddress();
                failed = true;
                _logger.ReplyListenerOpenFailed(e, reconnectDelay);
            }
            finally
            {
                await _CloseAsync(channel, connection, address).ConfigureAwait(false);
            }

            if (!failed)
            {
                // A lost listener reconnects at once; only repeated failures to reconnect back off.
                continue;
            }

            try
            {
                await Task.Delay(reconnectDelay, _timeProvider, _closing.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_closing.IsCancellationRequested)
            {
                return;
            }

            reconnectDelay = TimeSpan.FromTicks(Math.Min(reconnectDelay.Ticks * 2, _MaxReconnectDelay.Ticks));
        }
    }

    private void _PublishAddress(string address)
    {
        lock (_addressLock)
        {
            _address.TrySetResult(address);
        }
    }

    // Makes callers wait for the next address instead of stamping one whose queue is gone.
    private void _RetractAddress()
    {
        lock (_addressLock)
        {
            if (Volatile.Read(ref _disposed) == 0 && _address.Task.IsCompleted)
            {
                _address = _NewAddressSource();
            }
        }
    }

    private async Task _CloseAsync(IChannel? channel, IConnection? connection, string? address)
    {
        try
        {
            if (channel is not null)
            {
                await channel.DisposeAsync().ConfigureAwait(false);
            }

            if (connection is not null)
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            // The connection is already gone, and the broker dropped the exclusive queue with it.
            _logger.ReplyListenerCloseFailed(e, address);
        }
        finally
        {
            if (connection is not null)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task _DeliverAsync(string address, IReadOnlyBasicProperties properties, ReadOnlyMemory<byte> body)
    {
        TransportMessage reply;
        try
        {
            // The client reuses the body buffer once the delivery handler returns.
            reply = new TransportMessage(RabbitMqBasicConsumer.ReadHeaders(properties.Headers), body.ToArray());
        }
        catch (Exception e)
        {
            _logger.ReplyUnreadable(e, address);
            return;
        }

        try
        {
            await _onReply(reply, _closing.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
            // Closing: the caller's pending calls are being failed by the requester's shutdown.
        }
        catch (Exception e)
        {
            // One faulty reply must not stop the channel every other pending call depends on.
            _logger.ReplyHandlerFailed(e, address);
        }
    }

    private static TaskCompletionSource<string> _NewAddressSource()
    {
        return new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>
    /// Hands deliveries to the listener one at a time and reports the broker's cancellation or the channel's shutdown,
    /// either of which leaves the queue unconsumed.
    /// </summary>
    private sealed class ReplyConsumer(
        IChannel channel,
        RabbitMqReplyListener listener,
        string address,
        TaskCompletionSource<string> lost
    ) : AsyncDefaultBasicConsumer(channel)
    {
        public override Task HandleBasicDeliverAsync(
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
            return listener._DeliverAsync(address, properties, body);
        }

        // A basic.cancel from the broker, as when an operator deletes the queue; the channel stays open but receives
        // nothing more, so the listener must rebuild.
        public override async Task HandleBasicCancelAsync(
            string consumerTag,
            CancellationToken cancellationToken = default
        )
        {
            await base.HandleBasicCancelAsync(consumerTag, cancellationToken).ConfigureAwait(false);
            lost.TrySetResult($"the broker cancelled consumer '{consumerTag}', as it does when its queue is deleted");
        }

        public override async Task HandleChannelShutdownAsync(object channel, ShutdownEventArgs reason)
        {
            await base.HandleChannelShutdownAsync(channel, reason).ConfigureAwait(false);
            lost.TrySetResult($"the channel shut down: {reason.ReplyText}");
        }
    }
}

internal static partial class RabbitMqReplyListenerLog
{
    [LoggerMessage(
        EventId = 3009,
        Level = LogLevel.Debug,
        Message = "RabbitMQ reply listener is consuming reply queue '{ReplyAddress}'."
    )]
    public static partial void ReplyListenerReady(this ILogger logger, string replyAddress);

    [LoggerMessage(
        EventId = 3010,
        Level = LogLevel.Warning,
        Message = "RabbitMQ reply queue '{ReplyAddress}' was lost ({Reason}); the listener re-declares under a new address, and calls sent with the old one time out."
    )]
    public static partial void ReplyListenerLost(this ILogger logger, string replyAddress, string reason);

    [LoggerMessage(
        EventId = 3011,
        Level = LogLevel.Error,
        Message = "RabbitMQ reply listener failed to open its reply queue; retrying in {RetryDelay}."
    )]
    public static partial void ReplyListenerOpenFailed(this ILogger logger, Exception exception, TimeSpan retryDelay);

    [LoggerMessage(
        EventId = 3012,
        Level = LogLevel.Debug,
        Message = "Closing the connection of RabbitMQ reply queue '{ReplyAddress}' failed."
    )]
    public static partial void ReplyListenerCloseFailed(this ILogger logger, Exception exception, string? replyAddress);

    [LoggerMessage(
        EventId = 3013,
        Level = LogLevel.Warning,
        Message = "A reply on RabbitMQ reply queue '{ReplyAddress}' could not be read and was dropped."
    )]
    public static partial void ReplyUnreadable(this ILogger logger, Exception exception, string replyAddress);

    [LoggerMessage(
        EventId = 3014,
        Level = LogLevel.Error,
        Message = "The reply handler of RabbitMQ reply queue '{ReplyAddress}' threw; the listener keeps receiving."
    )]
    public static partial void ReplyHandlerFailed(this ILogger logger, Exception exception, string replyAddress);
}
