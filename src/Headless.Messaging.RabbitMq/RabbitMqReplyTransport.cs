// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace Headless.Messaging.RabbitMq;

/// <summary>
/// RabbitMQ reply channel. Each listener owns an exclusive, client-named queue under
/// <see cref="ReplyAddresses.Prefix"/>, and a reply reaches it through the default exchange with the queue name as the
/// routing key.
/// </summary>
/// <remarks>
/// <para>
/// Replies never touch the lane exchanges, so no lane binding can route a reply and no reply is persisted. The default
/// exchange routes by queue name alone, which is why a send is limited to the reserved reply namespace: the
/// server-generated <c>amq.gen-</c> names of every-instance queues and the <c>queue.</c> and <c>bus.</c> names of lane
/// queues all fall outside it, so a forged reply address cannot reach them.
/// </para>
/// <para>
/// A reply is published without the mandatory flag. A reply to a queue that is gone, because its caller stopped or
/// re-established its listener under a new name, is discarded by the broker without an error, and that caller has
/// already timed out or been aborted.
/// </para>
/// </remarks>
internal sealed class RabbitMqReplyTransport(
    IConnectionChannelPool connectionChannelPool,
    ILogger<RabbitMqReplyTransport> logger
) : IReplyTransport
{
    public ValueTask<IReplyListener> OpenListenerAsync(
        Func<TransportMessage, CancellationToken, ValueTask> onReply,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(onReply);
        cancellationToken.ThrowIfCancellationRequested();

#pragma warning disable CA2000 // False positive: the returned listener is owned and disposed by the caller.
        return ValueTask.FromResult<IReplyListener>(new RabbitMqReplyListener(connectionChannelPool, onReply, logger));
#pragma warning restore CA2000
    }

    public async ValueTask SendAsync(
        string address,
        TransportMessage reply,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsTrue(
            ((IReplyTransport)this).IsReplyAddress(address),
            "A reply can only be sent to an address inside the reserved reply namespace.",
            nameof(address)
        );
        cancellationToken.ThrowIfCancellationRequested();

        var channel = await connectionChannelPool.Rent(cancellationToken).ConfigureAwait(false);
        try
        {
            var props = new BasicProperties
            {
                MessageId = reply.Headers.TryGetValue(Headers.MessageId, out var messageId) ? messageId : null,
                // The reply queue is neither durable nor survives its connection, so persisting the reply buys nothing.
                DeliveryMode = DeliveryModes.Transient,
                Headers = reply.Headers.ToDictionary(x => x.Key, object? (x) => x.Value, StringComparer.Ordinal),
            };

            await channel
                .BasicPublishAsync(
                    exchange: string.Empty,
                    routingKey: address,
                    mandatory: false,
                    props,
                    reply.Body,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        catch (AlreadyClosedException) when (channel.IsOpen)
        {
            // The channel can report open while its connection is closed; dispose it so the pool does not hand it out
            // again.
            logger.ChannelStateInconsistencyDetected();
            await channel.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            connectionChannelPool.Return(channel);
        }
    }
}
