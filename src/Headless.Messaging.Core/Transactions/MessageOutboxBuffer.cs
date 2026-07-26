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
        coordinator.OnCommit(_FlushAsync);
    }

    private ValueTask _FlushAsync(CommitContext context, CancellationToken cancellationToken)
    {
        // The transaction is already committed. These best-effort signals must never add broker or scheduler I/O
        // to the commit path; the durable relay remains the correctness mechanism when a signal is dropped.
        foreach (var message in Drain())
        {
            if (message.ExpiresAt is not null)
            {
                (_dispatcher as ICommittedDelayedMessageDispatcher)?.EnqueueCommittedDelayedMessage(message);
                continue;
            }

            (_dispatcher as ICommittedMessageDispatcher)?.EnqueueCommittedMessage(message);
        }

        return ValueTask.CompletedTask;
    }
}
