// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Configuration;
using Headless.Messaging.Serialization;
using Headless.UnitOfWork;

namespace Headless.Messaging.Internal;

internal sealed class Bus : IBus
{
    private static readonly IMessageCapabilityGate _DirectConstructionCapabilities = MessagingCapabilityModel.Compose([
        MessagingProviderCapabilities.Transport("Direct", [MessageLane.Bus], supportsIndependentLaneTopology: true),
    ]);

    private readonly MessagePublisher _publisher;
    private readonly IUnitOfWorkManager? _unitOfWorkManager;

    /// <summary>
    /// The scoped DI-registered constructor: reads this scope's active unit of work at publish time only so a
    /// publish against a unit the storage cannot join is refused rather than silently written standalone. This
    /// surface never coordinates.
    /// </summary>
    internal Bus(MessagePublisher publisher, IUnitOfWorkManager unitOfWorkManager)
    {
        _publisher = publisher;
        _unitOfWorkManager = unitOfWorkManager;
    }

    /// <summary>
    /// Unit-less construction for framework-internal singletons (<c>HybridCache</c>, the distributed lock
    /// primitives) that must publish without participating in any caller's unit of work. Every publish from
    /// this instance sees no active unit; these callers always request <see cref="DeliveryMode.Direct"/>
    /// explicitly, which bypasses coordination entirely.
    /// </summary>
    internal Bus(MessagePublisher publisher)
    {
        _publisher = publisher;
        _unitOfWorkManager = null;
    }

    internal Bus(
        ISerializer serializer,
        IBusTransport transport,
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
            // default would reject every publish; this constructor is the explicit fire-and-forget bus.
            defaultDeliveryMode: DeliveryMode.Direct
        );
    }

    public Task<PublishReceipt> PublishAsync<T>(T? contentObj, CancellationToken cancellationToken = default)
    {
        return PublishAsync(contentObj, options: null, cancellationToken);
    }

    public Task<PublishReceipt> PublishAsync<T>(
        T? contentObj,
        PublishOptions? options,
        CancellationToken cancellationToken = default
    )
    {
        return _publisher.PublishAsync(
            MessageLane.Bus,
            contentObj,
            options,
            _unitOfWorkManager?.Current,
            requireCoordination: false,
            cancellationToken
        );
    }
}
