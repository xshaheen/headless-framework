// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

namespace Headless.Messaging.Nats;

/// <summary>
/// NATS reply channel on core NATS, outside JetStream. Each listener subscribes to its own subject under
/// <see cref="ReplyAddresses.Prefix"/>, and a reply is a core publish to that subject.
/// </summary>
/// <remarks>
/// <para>
/// Lane subjects are <c>headless.bus.*</c> and <c>headless.queue.*</c>, and every stream the provider provisions
/// captures only those, so no provisioned stream stores a reply and no lane consumer receives one. A stream an operator
/// provisions with a subject that covers <c>headless.reply.&gt;</c>, such as <c>headless.&gt;</c>, would store every
/// reply; keep operator streams on the lane prefixes.
/// </para>
/// <para>
/// A send is limited to the reserved reply namespace, so a forged reply address cannot make a responder publish to a
/// lane subject, where a stream would persist it and a consumer would run it. A core publish with nobody subscribed is
/// discarded by the server without an error, which is what a reply to a caller that has gone needs.
/// </para>
/// </remarks>
internal sealed class NatsReplyTransport(
    INatsConnectionPool connectionPool,
    TimeProvider timeProvider,
    ILogger<NatsReplyTransport> logger
) : IReplyTransport
{
    /// <inheritdoc />
    /// <remarks>
    /// Also rejects an empty subject token, as in <c>headless.reply..x</c> or a trailing <c>'.'</c>: the server refuses
    /// such a subject, so a reply to it could never arrive.
    /// </remarks>
    public bool IsReplyAddress(string address)
    {
        return ReplyAddresses.IsInReplyNamespace(address)
            && !address.Contains("..", StringComparison.Ordinal)
            && !address.EndsWith('.');
    }

    public ValueTask<IReplyListener> OpenListenerAsync(
        Func<TransportMessage, CancellationToken, ValueTask> onReply,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(onReply);
        cancellationToken.ThrowIfCancellationRequested();

#pragma warning disable CA2000 // False positive: the returned listener is owned and disposed by the caller.
        return ValueTask.FromResult<IReplyListener>(
            new NatsReplyListener(connectionPool.GetConnection(), onReply, timeProvider, logger)
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
            IsReplyAddress(address),
            "A reply can only be sent to an address inside the reserved reply namespace.",
            nameof(address)
        );
        cancellationToken.ThrowIfCancellationRequested();

        await connectionPool
            .GetConnection()
            .PublishAsync(
                address,
                reply.Body,
                headers: NatsTransport.CreatePublishHeaders(reply),
                serializer: NatsRawSerializer<ReadOnlyMemory<byte>>.Default,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
    }
}
