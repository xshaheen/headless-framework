// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;

namespace Headless.Messaging.Transport;

[PublicAPI]
public interface IDispatcher : IProcessingServer
{
    /// <summary>Stops the dispatcher using the supplied remaining shutdown budget.</summary>
    /// <param name="timeout">The remaining end-to-end messaging shutdown budget.</param>
    /// <param name="cancellationToken">
    /// Reserved for API consistency. Shutdown cleanup remains governed by <paramref name="timeout"/>.
    /// </param>
    ValueTask DisposeAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        return DisposeAsync();
    }

    ValueTask EnqueueToPublish(MediumMessage message, CancellationToken cancellationToken = default);

    ValueTask EnqueueToExecute(
        MediumMessage message,
        ConsumerExecutorDescriptor? descriptor = null,
        CancellationToken cancellationToken = default
    );

    Task EnqueueToScheduler(
        MediumMessage message,
        DateTimeOffset publishTime,
        DbTransaction? transaction = null,
        CancellationToken cancellationToken = default
    );
}

/// <summary>
/// Internal queue-only path for messages whose durable delayed-state transition already committed.
/// Keeping this separate from <see cref="IDispatcher"/> prevents consumers from bypassing storage authority.
/// </summary>
internal interface ICommittedDelayedMessageDispatcher
{
    /// <summary>Whether the scheduler queue has no room, so it would refuse every message offered now.</summary>
    bool IsSchedulerFull { get; }

    /// <summary>Offers a committed delayed message to the in-memory scheduler without waiting for capacity.</summary>
    /// <returns>
    /// <see langword="false"/> when the scheduler refused it (full or stopping). An unclaimed row stays durable as
    /// Delayed work, but a row the caller claimed is Queued and leased to this node, so the caller must hand it back.
    /// </returns>
    bool EnqueueCommittedDelayedMessage(MediumMessage message);
}

/// <summary>
/// Internal acceleration path for an immediate message whose durable row already committed.
/// A dropped enqueue is safe because durable retry pickup remains the recovery authority.
/// </summary>
internal interface ICommittedMessageDispatcher
{
    /// <summary>
    /// Hands a committed message to the sender, waiting for channel capacity only within a short budget shared by
    /// every message of one committing operation. Never throws: the publish already succeeded when this runs.
    /// </summary>
    /// <param name="message">The committed message.</param>
    /// <param name="waitStartedAt">
    /// A <see cref="System.Diagnostics.Stopwatch.GetTimestamp"/> value taken once per committing operation. Every
    /// message passed with the same value shares one budget measured from it, so a flush of many messages against a
    /// full channel waits the budget once in total, not once per message.
    /// </param>
    ValueTask EnqueueCommittedMessageAsync(MediumMessage message, long waitStartedAt);
}
