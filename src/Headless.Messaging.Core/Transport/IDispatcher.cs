// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Messaging.Messages;
using Headless.Messaging.Runtime;

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
    void EnqueueCommittedDelayedMessage(MediumMessage message);
}
