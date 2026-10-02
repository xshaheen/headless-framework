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
    private readonly IConnectionChannelPool _connectionChannelPool;
    private readonly Func<TransportMessage, CancellationToken, ValueTask> _onReply;
    private readonly ReplyListenerBackoff _backoff;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _closing = new();
    private readonly Task _maintain;

    // Unresolved while the listener connects; resolved with the address its current queue is consumed under.
    private readonly ReplyAddressGate _address = new();
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
        _backoff = new ReplyListenerBackoff(timeProvider);
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

        return _address.WaitAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        // A call still waiting for the first address learns that the listener closed.
        _address.FailOnDispose(nameof(RabbitMqReplyListener));

        await _closing.CancelAsync().ConfigureAwait(false);

        // The loop closes the connection on its way out, which deletes the exclusive queue.
        await _maintain.ConfigureAwait(false);
        _closing.Dispose();
    }

    private async Task _MaintainAsync()
    {
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

                _address.Publish(address);
                _backoff.Reset();
                _logger.ReplyListenerReady(address);

                var reason = await lost.Task.WaitAsync(_closing.Token).ConfigureAwait(false);
                // Makes callers wait for the next address instead of stamping one whose queue is gone.
                _address.Retract();
                _logger.ReplyListenerLost(address, reason);
            }
            catch (OperationCanceledException) when (_closing.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                _address.Retract();
                failed = true;
                _logger.ReplyListenerOpenFailed(e, _backoff.Delay);
            }
            finally
            {
                await _CloseAsync(channel, connection, address).ConfigureAwait(false);
            }

            // A lost listener reconnects at once; only repeated failures to reconnect back off.
            if (failed && !await _backoff.WaitAsync(_closing.Token).ConfigureAwait(false))
            {
                return;
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

        await ReplyHandlerInvoker
            .InvokeAsync(
                _onReply,
                reply,
                (Logger: _logger, Address: address),
                static (state, e) => state.Logger.ReplyHandlerFailed(e, state.Address),
                _closing.Token
            )
            .ConfigureAwait(false);
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
