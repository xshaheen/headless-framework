// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;
using Headless.Messaging.Transport;
using Headless.UnitOfWork;

namespace Headless.Messaging.Transactions;

/// <summary>
/// The per-unit buffer of coordinated outbox rows, handed to the dispatcher once the unit commits.
/// </summary>
/// <remarks>
/// The drain is registered when the first row arrives, not when the buffer is created. The writer obtains the
/// buffer before it stores a row — that registration is what refuses a handle that can no longer carry work,
/// before any storage effect — but a storage that captures rows on the unit registers its own promotion callback
/// during the store, and callbacks drain in registration order, so the dispatch hand-off must be registered after
/// the store to find the row visible.
/// </remarks>
internal sealed class MessageOutboxBuffer(IDispatcher dispatcher) : InMemoryWorkBuffer<MediumMessage>
{
    private readonly Lock _gate = new();
    private bool _drainRegistered;

    /// <summary>Buffers a stored row and, on the first row, registers the post-commit drain on <paramref name="unitOfWork" />.</summary>
    public void Add(IUnitOfWork unitOfWork, MediumMessage message)
    {
        lock (_gate)
        {
            if (!_drainRegistered)
            {
                unitOfWork.OnCompleted(_FlushAsync);
                _drainRegistered = true;
            }
        }

        Add(message);
    }

    private ValueTask _FlushAsync()
    {
        // The transaction is already committed. These best-effort signals must never add broker or scheduler I/O
        // to the commit path; the durable relay remains the correctness mechanism when a signal is dropped.
        foreach (var message in Drain())
        {
            if (message.ExpiresAt is not null)
            {
                (dispatcher as ICommittedDelayedMessageDispatcher)?.EnqueueCommittedDelayedMessage(message);
                continue;
            }

            (dispatcher as ICommittedMessageDispatcher)?.EnqueueCommittedMessage(message);
        }

        return ValueTask.CompletedTask;
    }
}
