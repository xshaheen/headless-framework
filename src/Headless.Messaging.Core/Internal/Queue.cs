// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Configuration;
using Headless.Messaging.Serialization;
using Headless.UnitOfWork;

namespace Headless.Messaging.Internal;

internal sealed class Queue : IQueue
{
    private static readonly IMessageCapabilityGate _DirectConstructionCapabilities = MessagingCapabilityModel.Compose([
        MessagingProviderCapabilities.Transport("Direct", [MessageLane.Queue], supportsIndependentLaneTopology: true),
    ]);

    private readonly MessagePublisher _publisher;
    private readonly IUnitOfWorkManager? _unitOfWorkManager;

    /// <summary>
    /// The scoped DI-registered constructor: reads this scope's active unit of work at publish time so a
    /// durable enqueue enlists in it when <see cref="TransactionEnlistment"/> allows.
    /// </summary>
    internal Queue(MessagePublisher publisher, IUnitOfWorkManager unitOfWorkManager)
    {
        _publisher = publisher;
        _unitOfWorkManager = unitOfWorkManager;
    }

    /// <summary>
    /// Unit-less construction for framework-internal singletons that must publish without participating in
    /// any caller's unit of work.
    /// </summary>
    internal Queue(MessagePublisher publisher)
    {
        _publisher = publisher;
        _unitOfWorkManager = null;
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
        _unitOfWorkManager = null;
        _publisher = new MessagePublisher(
            serializer,
            _ => transport,
            publishRequestFactory,
            publishPipeline,
            timeProvider,
            _DirectConstructionCapabilities,
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
        return _publisher.PublishAsync(
            MessageLane.Queue,
            contentObj,
            options,
            _unitOfWorkManager?.Current,
            cancellationToken
        );
    }
}
