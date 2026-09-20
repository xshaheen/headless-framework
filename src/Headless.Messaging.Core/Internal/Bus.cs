// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Configuration;
using Headless.Messaging.Serialization;

namespace Headless.Messaging.Internal;

internal sealed class Bus : IBus
{
    private static readonly IMessageCapabilityGate _DirectConstructionCapabilities = MessagingCapabilityModel.Compose([
        MessagingProviderCapabilities.Transport("Direct", [MessageLane.Bus], supportsIndependentLaneTopology: true),
    ]);

    private readonly MessagePublisher _publisher;

    /// <summary>
    /// The DI-registered constructor. This surface is autonomous: it never reads a caller's unit of work, so
    /// it is a singleton that framework singletons can depend on, and a publish made while a unit is active
    /// writes a standalone durable row. Callers that want the row inside their unit's transaction publish
    /// through the unit-of-work outbox instead.
    /// </summary>
    internal Bus(MessagePublisher publisher)
    {
        _publisher = publisher;
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
            unitOfWork: null,
            requireCoordination: false,
            cancellationToken
        );
    }
}
