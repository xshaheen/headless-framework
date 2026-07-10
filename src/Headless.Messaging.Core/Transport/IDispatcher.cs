// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;
using Headless.Messaging.Runtime;

namespace Headless.Messaging.Transport;

[PublicAPI]
public interface IDispatcher : IProcessingServer
{
    /// <summary>Stops the dispatcher using the supplied remaining shutdown budget.</summary>
    /// <param name="timeout">The remaining end-to-end messaging shutdown budget.</param>
    ValueTask DisposeAsync(TimeSpan timeout);

    ValueTask EnqueueToPublish(MediumMessage message, CancellationToken cancellationToken = default);

    ValueTask EnqueueToExecute(
        MediumMessage message,
        ConsumerExecutorDescriptor? descriptor = null,
        CancellationToken cancellationToken = default
    );

    Task EnqueueToScheduler(
        MediumMessage message,
        DateTime publishTime,
        object? transaction = null,
        CancellationToken cancellationToken = default
    );
}
