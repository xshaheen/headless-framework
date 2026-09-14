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
            _ => transport,
            publishRequestFactory,
            publishPipeline,
            timeProvider,
            _DirectConstructionCapabilities,
            new MessagingNullCommitCoordinator(),
            static () => null,
            static () => null,
            telemetry,
            // Direct construction carries a transport-only capability model and no storage, so the durable host
            // default would reject every enqueue; this constructor is the explicit fire-and-forget queue.
            defaultDeliveryMode: DeliveryMode.Direct
        );
    }

    public Task<PublishReceipt> EnqueueAsync<T>(T? contentObj, CancellationToken cancellationToken = default)
    {
        return EnqueueAsync(contentObj, options: null, cancellationToken);
    }

    public Task<PublishReceipt> EnqueueAsync<T>(
        T? contentObj,
        QueueOptions? options,
        CancellationToken cancellationToken = default
    )
    {
        return _publisher.PublishAsync(MessageLane.Queue, contentObj, options, cancellationToken);
    }
}
