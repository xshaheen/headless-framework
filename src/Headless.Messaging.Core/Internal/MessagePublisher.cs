// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;
using Headless.Messaging.Configuration;
using Headless.Messaging.Messages;
using Headless.Messaging.Serialization;

namespace Headless.Messaging.Internal;

internal sealed class MessagePublisher(
    ISerializer serializer,
    Func<MessageLane, ITransport> transportResolver,
    IMessagePublishRequestFactory publishRequestFactory,
    IPublishMiddlewarePipeline publishPipeline,
    TimeProvider timeProvider,
    IMessageCapabilityGate capabilities,
    ICurrentCommitCoordinator currentCommitCoordinator,
    Func<IDeliveryCoordinationResolver?> coordinationResolver,
    Func<OutboxMessageWriter?> outboxWriterResolver,
    MessagingTelemetry? telemetry = null,
    TimeSpan? transportPublishTimeout = null
)
{
    private readonly MessagingTelemetry _telemetry = telemetry ?? MessagingTelemetry.Default;
    private readonly TimeSpan _transportPublishTimeout = transportPublishTimeout ?? TimeSpan.FromSeconds(10);

    internal Task PublishAsync<T>(
        MessageLane lane,
        T? content,
        MessageOptions? options,
        CancellationToken cancellationToken
    )
    {
        // AsyncLocal state must be captured in the caller's execution context, before any middleware await.
        var coordinator = currentCommitCoordinator.Current;
        var coordination = _ResolveCoordination(coordinator);
        var decision = DeliveryDecisionResolver.Resolve(
            lane,
            options?.DeliveryMode ?? DeliveryMode.Auto,
            options?.Delay,
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
            lane,
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
                        lane
                    )
                    : publishRequestFactory.Create(content, declaredMessageType, middlewareOptions, lane: lane);

                if (decision.Path is DeliveryPath.TransportDirect)
                {
                    DeliveryMetadata.Stamp(request.Message.Headers, decision);
                    var transport = transportResolver(lane);
                    return _SendDirectAsync(request.Message, request.Lane, transport, ct);
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

    private async Task _SendDirectAsync(
        Message message,
        MessageLane lane,
        ITransport transport,
        CancellationToken cancellationToken
    )
    {
        using var timeoutCts = new CancellationTokenSource(_transportPublishTimeout, timeProvider);
        using var publishCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            await DirectPublisherCore
                .SendAsync(
                    message,
                    lane,
                    serializer,
                    transport.BrokerAddress,
                    transport.SendAsync,
                    _NowUnixTimeMilliseconds,
                    _telemetry,
                    publishCts.Token
                )
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
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
