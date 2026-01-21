// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Internal;
using Headless.Messaging.Messages;

namespace Headless.Messaging.Transport;

public interface IDispatcher : IProcessingServer
{
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
