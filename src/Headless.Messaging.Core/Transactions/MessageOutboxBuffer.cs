// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;
using Headless.Messaging.Transport;
using Headless.UnitOfWork;

namespace Headless.Messaging.Transactions;

internal sealed class MessageOutboxBuffer : InMemoryWorkBuffer<MediumMessage>
{
    private readonly IDispatcher _dispatcher;

    public MessageOutboxBuffer(IUnitOfWork unitOfWork, IDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        unitOfWork.OnCompleted(_FlushAsync);
    }

    private ValueTask _FlushAsync()
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
