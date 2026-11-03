// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
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

    private async ValueTask _FlushAsync()
    {
        // The transaction is already committed. These best-effort signals never add broker or scheduler I/O to the
        // commit path; the durable relay remains the correctness mechanism when a signal is dropped. The only wait is
        // for room in a full publish channel, and the whole flush shares one short budget started here, so a
        // saturated host delays the commit by that budget once, not once per buffered message.
        var waitStartedAt = Stopwatch.GetTimestamp();

        foreach (var message in Drain())
        {
            if (message.ExpiresAt is not null)
            {
                // Unclaimed Delayed work: a refusal leaves it for the delayed processor, so there is nothing to hand back.
                _ = (dispatcher as ICommittedDelayedMessageDispatcher)?.HandOffCommittedDelayedMessage(message);
                continue;
            }

            if (dispatcher is ICommittedMessageDispatcher committed)
            {
                await committed.EnqueueCommittedMessageAsync(message, waitStartedAt).ConfigureAwait(false);
            }
        }
    }
}
