// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.InMemory;

/// <summary>
/// In-process reply transport. Reply addresses are registered in the shared <see cref="MemoryQueue"/>, so every host
/// built on the same queue can reply to every other one.
/// </summary>
internal sealed class InMemoryReplyTransport(MemoryQueue queue, ILogger<InMemoryReplyTransport> logger)
    : IReplyTransport
{
    public ValueTask<IReplyListener> OpenListenerAsync(
        Func<TransportMessage, CancellationToken, ValueTask> onReply,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(onReply);
        cancellationToken.ThrowIfCancellationRequested();

#pragma warning disable CA2000 // False positive: the returned listener is owned and disposed by the caller.
        return ValueTask.FromResult<IReplyListener>(new InMemoryReplyListener(queue, onReply, logger));
#pragma warning restore CA2000
    }

    public ValueTask SendAsync(string address, TransportMessage reply, CancellationToken cancellationToken = default)
    {
        Argument.IsTrue(
            ((IReplyTransport)this).IsReplyAddress(address),
            "A reply can only be sent to an address inside the reserved reply namespace.",
            nameof(address)
        );
        cancellationToken.ThrowIfCancellationRequested();

        queue.SendReply(address, reply);
        return ValueTask.CompletedTask;
    }
}
