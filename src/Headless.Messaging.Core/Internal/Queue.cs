// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Configuration;
using Headless.Messaging.Serialization;
using Headless.Messaging.Transactions;

namespace Headless.Messaging.Internal;

internal sealed class Queue : IQueue
{
    private static readonly IMessageCapabilityGate _DirectConstructionCapabilities = MessagingCapabilityModel.Compose([
        MessagingProviderCapabilities.Transport("Direct", [MessageLane.Queue], supportsIndependentLaneTopology: true),
    ]);

    private readonly MessagePublisher _publisher;

    internal Queue(MessagePublisher publisher)
    {
        _publisher = publisher;
    }

    internal Queue(
        ISerializer serializer,
        IQueueTransport transport,
        IMessagePublishRequestFactory publishRequestFactory,
        IPublishMiddlewarePipeline publishPipeline,
        TimeProvider timeProvider,
        MessagingTelemetry? telemetry = null
    )
    {
        _publisher = new MessagePublisher(
            serializer,
            _ => transport.BrokerAddress,
            (_, message, cancellationToken) => transport.SendAsync(message, cancellationToken),
            publishRequestFactory,
            publishPipeline,
            timeProvider,
            _DirectConstructionCapabilities,
            new MessagingNullCommitCoordinator(),
            static () => null,
            static () => null,
            telemetry
        );
    }

    public Task EnqueueAsync<T>(
        T? contentObj,
        EnqueueOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        return _publisher.PublishAsync(MessageLane.Queue, contentObj, options, options?.Delay, cancellationToken);
    }
}
