// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;
using Headless.Messaging.Configuration;
using Headless.Messaging.Serialization;

namespace Headless.Messaging.Internal;

internal sealed class MessagePublisher(
    ISerializer serializer,
    Func<MessageLane, BrokerAddress> brokerAddressResolver,
    Func<MessageLane, TransportMessage, CancellationToken, Task<OperateResult>> transportSender,
    IMessagePublishRequestFactory publishRequestFactory,
    IPublishMiddlewarePipeline publishPipeline,
    TimeProvider timeProvider,
    IMessageCapabilityGate capabilities,
    ICurrentCommitCoordinator currentCommitCoordinator,
    Func<IDeliveryCoordinationResolver?> coordinationResolver,
    Func<OutboxMessageWriter?> outboxWriterResolver,
    MessagingTelemetry? telemetry = null
)
{
    private readonly MessagingTelemetry _telemetry = telemetry ?? MessagingTelemetry.Default;

    internal Task PublishAsync<T>(
        MessageLane lane,
        T? content,
        MessageOptions? options,
        TimeSpan? delay,
        CancellationToken cancellationToken
    )
    {
        // AsyncLocal state must be captured in the caller's execution context, before any middleware await.
        var coordinator = currentCommitCoordinator.Current;
        var coordination = _ResolveCoordination(coordinator);
        var decision = DeliveryDecisionResolver.Resolve(
            lane,
            options?.DeliveryMode ?? DeliveryMode.Auto,
            delay,
            coordination,
            timeProvider.GetUtcNow()
        );

        if (decision.Path is DeliveryPath.TransportDirect)
        {
            capabilities.EnsureDirectSupported(lane);
        }
        else
        {
            capabilities.EnsureOutboxSupported(lane, scheduled: decision.Delay is not null);
        }

        var declaredMessageType = options?.MessageType ?? typeof(T);
        return publishPipeline.ExecuteAsync(
            content,
            MessageLaneCompatibility.ToIntentType(lane),
            options,
            decision,
            innerPublish: (middlewareOptions, ct) =>
            {
                var request = decision.PublishAt is { } publishAt
                    ? publishRequestFactory.Create(
                        content,
                        declaredMessageType,
                        middlewareOptions,
                        decision.Delay!.Value,
                        publishAt,
                        MessageLaneCompatibility.ToIntentType(lane)
                    )
                    : publishRequestFactory.Create(
                        content,
                        declaredMessageType,
                        middlewareOptions,
                        intentType: MessageLaneCompatibility.ToIntentType(lane)
                    );

                if (decision.Path is DeliveryPath.TransportDirect)
                {
                    return DirectPublisherCore.SendAsync(
                        request.Message,
                        request.IntentType,
                        serializer,
                        brokerAddressResolver(lane),
                        (message, token) => transportSender(lane, message, token),
                        _NowUnixTimeMilliseconds,
                        _telemetry,
                        ct
                    );
                }

                var writer =
                    outboxWriterResolver()
                    ?? throw new InvalidOperationException(
                        "Durable delivery requires a configured messaging storage provider."
                    );
                return writer.WriteAsync(request, decision, ct);
            },
            cancellationToken
        );
    }

    private DeliveryCoordination _ResolveCoordination(ICommitCoordinator? coordinator)
    {
        if (coordinator is null)
        {
            return DeliveryCoordination.None;
        }

        var resolver = coordinationResolver();
        return resolver?.Resolve(coordinator)
            ?? DeliveryCoordination.Incompatible(DeliveryCoordinationMismatch.MissingRelationalCapability);
    }

    private long _NowUnixTimeMilliseconds()
    {
        return timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
    }
}
