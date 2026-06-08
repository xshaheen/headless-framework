// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;
using Headless.Messaging.Messages;
using Headless.Messaging.Transport;

namespace Headless.Messaging.Transactions;

internal sealed class MessageOutboxBuffer : InMemoryWorkBuffer<MediumMessage>
{
    private readonly IDispatcher _dispatcher;

    public MessageOutboxBuffer(ICommitCoordinator coordinator, IDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        coordinator.OnCommit(FlushAsync);
    }

    private async ValueTask FlushAsync(CommitContext context, CancellationToken cancellationToken)
    {
        foreach (var message in Drain())
        {
            if (message.Origin.Headers.ContainsKey(Headers.DelayTime))
            {
                await _dispatcher
                    .EnqueueToScheduler(
                        message,
                        DateTime.Parse(message.Origin.Headers[Headers.SentTime]!, CultureInfo.InvariantCulture),
                        transaction: null,
                        cancellationToken
                    )
                    .ConfigureAwait(false);

                continue;
            }

            await _dispatcher.EnqueueToPublish(message, cancellationToken).ConfigureAwait(false);
        }
    }
}
