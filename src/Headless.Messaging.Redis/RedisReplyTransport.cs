// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Headless.Messaging.Redis;

/// <summary>
/// Redis reply channel on pub/sub. Each listener subscribes to its own channel under
/// <see cref="ReplyAddresses.Prefix"/>, and a reply is a <c>PUBLISH</c> to that channel.
/// </summary>
/// <remarks>
/// <para>
/// Pub/sub channels and keys are separate namespaces in Redis, and <c>PUBLISH</c> writes no key, so a reply is never
/// stored and never lands on a lane stream. Lane streams are keys (<c>headless:messaging:bus:*</c> and
/// <c>headless:messaging:queue:*</c>), so no message name can map to a reply channel. A send is still limited to the
/// reserved reply namespace, so a forged reply address cannot make a responder publish to a channel another
/// application on the same server listens to. Channels are always literal, so an address is never read as a pattern.
/// </para>
/// <para>
/// A <c>PUBLISH</c> with nobody subscribed is discarded by the server without an error, which is what a reply to a
/// caller that has gone needs. Delivery is at most once: a reply published while its caller's subscription is down is
/// lost, and the call it answered times out.
/// </para>
/// <para>
/// On a cluster, a classic <c>PUBLISH</c> is forwarded to every node, so a reply reaches its caller whichever node
/// either side is connected to; each reply then crosses the cluster bus once per node.
/// </para>
/// </remarks>
internal sealed class RedisReplyTransport(
    IRedisConnectionPool connectionPool,
    TimeProvider timeProvider,
    ILogger<RedisReplyTransport> logger
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
        return ValueTask.FromResult<IReplyListener>(
            new RedisReplyListener(connectionPool, onReply, timeProvider, logger)
        );
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

        var connection = await connectionPool.ConnectAsync(cancellationToken).ConfigureAwait(false);

        await connection
            .GetSubscriber()
            .PublishAsync(RedisChannel.Literal(address), reply.AsReplyPayload())
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
