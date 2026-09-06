// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;
using Headless.Messaging.Configuration;
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
    TimeSpan? transportPublishTimeout = null,
    DeliveryMode defaultDeliveryMode = DeliveryMode.Auto
)
{
    private readonly MessagingTelemetry _telemetry = telemetry ?? MessagingTelemetry.Default;
    private readonly TimeSpan _transportPublishTimeout = transportPublishTimeout ?? TimeSpan.FromSeconds(10);

    // Cached once: passing a method group as Func<long> allocates a fresh delegate on every publish,
    // because the compiler only caches method-group conversions for static methods.
    private readonly Func<long> _nowUnixTimeMilliseconds = () => timeProvider.GetUtcNow().ToUnixTimeMilliseconds();

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
            options?.DeliveryMode ?? defaultDeliveryMode,
            options?.Delay,
            coordination,
            timeProvider.GetUtcNow(),
            scheduledAt: options?.ScheduledAt
        );

        if (decision.Path is DeliveryPath.Direct)
        {
            capabilities.EnsureDirectSupported(lane);
        }
        else
        {
            // PublishAt, not Delay: an absolute schedule is equally a scheduled send, and gating on Delay
            // alone would let an absolute instant past a provider that cannot schedule.
            capabilities.EnsureOutboxSupported(lane, scheduled: decision.PublishAt is not null);
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
                        // Nullable: an absolute schedule resolves a publishAt with no relative delay, so
                        // dereferencing Delay here threw before the message ever reached storage.
                        decision.Delay,
                        publishAt,
                        lane
                    )
                    : publishRequestFactory.Create(content, declaredMessageType, middlewareOptions, lane: lane);

                if (decision.Path is DeliveryPath.Direct)
                {
                    DeliveryMetadata.Stamp(request.Message.Headers, decision);
                    var transport = transportResolver(lane);
                    return DirectPublisherCore.SendAsync(
                        request.Message,
                        request.Lane,
                        serializer,
                        transport.BrokerAddress,
                        transport.SendAsync,
                        _nowUnixTimeMilliseconds,
                        _telemetry,
                        _transportPublishTimeout,
                        timeProvider,
                        ct
                    );
                }

                var writer =
                    outboxWriterResolver()
                    ?? throw new InvalidOperationException(
                        "Durable delivery requires a configured messaging storage provider."
                    );
                if (
                    decision.Path is DeliveryPath.DurableCoordinated
                    && options?.IsRetainedForTransactionReplay is not true
                )
                {
                    // A completed domain occurrence is not rerun after rollback. Its direct outbox writes
                    // cannot be recovered from EF's retained state, so mark before attempting storage.
                    decision.Coordination.Coordinator!.GetOrAdd(static _ => new CommitRetryGuard()).PreventRetry();
                }

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
}
