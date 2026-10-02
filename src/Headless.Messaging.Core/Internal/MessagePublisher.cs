// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Configuration;
using Headless.Messaging.Registration;
using Headless.Messaging.RequestReply;
using Headless.Messaging.Serialization;
using Headless.UnitOfWork;

namespace Headless.Messaging.Internal;

internal sealed class MessagePublisher(
    ISerializer serializer,
    Func<MessageLane, ITransport> transportResolver,
    IMessagePublishRequestFactory publishRequestFactory,
    IPublishMiddlewarePipeline publishPipeline,
    TimeProvider timeProvider,
    IMessageCapabilityGate capabilities,
    Func<IDeliveryCoordinationResolver?> coordinationResolver,
    Func<OutboxMessageWriter?> outboxWriterResolver,
    MessagingTelemetry? telemetry = null,
    TimeSpan? transportPublishTimeout = null,
    DeliveryMode defaultDeliveryMode = DeliveryMode.Durable,
    IEnumerable<MessageRegistration>? registrations = null
)
{
    private readonly MessagingTelemetry _telemetry = telemetry ?? MessagingTelemetry.Default;
    private readonly TimeSpan _transportPublishTimeout = transportPublishTimeout ?? TimeSpan.FromSeconds(10);

    // Frozen at construction from the message contracts only: consumer registrations never carry a policy, and several
    // of them can share a (type, lane) key, so indexing every registration would collide while adding nothing.
    private readonly FrozenDictionary<(Type MessageType, MessageLane Lane), DeliveryMode> _deliveryPolicies = (
        registrations ?? []
    )
        .Where(static registration => registration.DeliveryMode is not null)
        .ToFrozenDictionary(
            static registration => (registration.MessageType, registration.Lane),
            static registration => registration.DeliveryMode!.Value
        );

    // Cached once: passing a method group as Func<long> allocates a fresh delegate on every publish,
    // because the compiler only caches method-group conversions for static methods.
    private readonly Func<long> _nowUnixTimeMilliseconds = () => timeProvider.GetUtcNow().ToUnixTimeMilliseconds();

    internal Task<PublishReceipt> PublishAsync<T>(
        MessageLane lane,
        T? content,
        MessageOptions? options,
        IUnitOfWork? unitOfWork,
        bool requireCoordination,
        CancellationToken cancellationToken
    )
    {
        return _PublishAsync(lane, content, options, unitOfWork, requireCoordination, request: null, cancellationToken);
    }

    /// <summary>
    /// Sends a request on the Queue lane through the normal Queue publish middleware. It is always sent directly, so the
    /// call's options, the contract's delivery policy, and the host default cannot route it through the outbox. The
    /// request's protocol headers and message id are stamped by the factory from <paramref name="request"/>, after
    /// middleware ran, and <see cref="RequestStamp.OnPrepared"/> receives the final envelope's message id and tenant
    /// before the transport send.
    /// </summary>
    /// <returns>
    /// The receipt of the sent request, or an empty receipt when publish middleware suppressed it.
    /// </returns>
    internal Task<PublishReceipt> PublishRequestAsync<T>(
        T content,
        QueueOptions options,
        RequestStamp request,
        CancellationToken cancellationToken
    )
    {
        return _PublishAsync(
            MessageLane.Queue,
            content,
            options,
            unitOfWork: null,
            requireCoordination: false,
            request,
            cancellationToken
        );
    }

    private async Task<PublishReceipt> _PublishAsync<T>(
        MessageLane lane,
        T? content,
        MessageOptions? options,
        IUnitOfWork? unitOfWork,
        bool requireCoordination,
        RequestStamp? request,
        CancellationToken cancellationToken
    )
    {
        var coordination = _ResolveCoordination(unitOfWork);
        var declaredMessageType = options?.MessageType ?? typeof(T);
        // An enlisted publish has no mode to resolve: durable capture is the mechanism it enlists through, so
        // the mode is Durable by construction and none of the three autonomous inputs may reach this path. A
        // type pinned Direct would otherwise turn every coordinated publish of that type into a refusal.
        //
        // Otherwise precedence is per call, then the policy registered for the declared type on this lane, then
        // the host default. The declared type (not the runtime content type) is the key so a callback response
        // that names its MessageType resolves the same policy the registration declared.
        // A request is always Direct: a durable request adds outbox latency and can run the command after its caller
        // has stopped waiting.
        var requestedMode =
            request is not null ? DeliveryMode.Direct
            : requireCoordination ? DeliveryMode.Durable
            : MessageOptionsDelivery.GetDeliveryMode(options)
                ?? (
                    _deliveryPolicies.TryGetValue((declaredMessageType, lane), out var typePolicy)
                        ? typePolicy
                        : defaultDeliveryMode
                );
        // Storage support is a resolver input, not a pipeline probe: the outbox writer is registered unconditionally
        // and throws when storage is missing, so a durable request on a storage-less host is refused here first.
        var decision = DeliveryDecisionResolver.Resolve(
            lane,
            requestedMode,
            requireCoordination,
            options?.Delay,
            coordination,
            timeProvider.GetUtcNow(),
            scheduledAt: options?.ScheduledAt,
            storageSupported: capabilities.Supports(lane, MessagingProviderRole.Storage),
            messageName: declaredMessageType.Name
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

        PublishReceipt receipt = default;
        await publishPipeline
            .ExecuteAsync(
                content,
                lane,
                options,
                decision,
                innerPublish: async (middlewareOptions, ct) =>
                {
                    var prepared =
                        request is not null
                            ? publishRequestFactory.CreateRequest(
                                content,
                                declaredMessageType,
                                middlewareOptions,
                                request
                            )
                        : decision.PublishAt is { } publishAt
                            ? publishRequestFactory.Create(
                                content,
                                declaredMessageType,
                                middlewareOptions,
                                // An absolute schedule has no relative delay.
                                decision.Delay,
                                publishAt,
                                lane
                            )
                        : publishRequestFactory.Create(content, declaredMessageType, middlewareOptions, lane: lane);

                    if (decision.Path is DeliveryPath.Direct)
                    {
                        DeliveryMetadata.Stamp(prepared.Message.Headers, decision);
                        if (request is not null)
                        {
                            var headers = prepared.Message.Headers;
                            headers.TryGetValue(Headers.TenantId, out var tenantId);
                            request.OnPrepared(headers[Headers.MessageId]!, tenantId);
                        }

                        var transport = transportResolver(lane);
                        await DirectPublisherCore
                            .SendAsync(
                                prepared.Message,
                                prepared.Lane,
                                serializer,
                                transport.BrokerAddress,
                                transport.SendAsync,
                                _nowUnixTimeMilliseconds,
                                _telemetry,
                                _transportPublishTimeout,
                                timeProvider,
                                ct
                            )
                            .ConfigureAwait(false);
                        receipt = new PublishReceipt(prepared.Message.Headers[Headers.MessageId], StorageId: null);
                        return;
                    }

                    var writer =
                        outboxWriterResolver()
                        ?? throw new InvalidOperationException(
                            "Durable delivery requires a configured messaging storage provider."
                        );
                    if (
                        decision.Path is DeliveryPath.DurableCoordinated
                        && options?.IsRetainedForTransactionReplay is not true
                        && decision.Coordination.UnitOfWork!.Resource is { IsOwned: false }
                    )
                    {
                        // Replay re-runs the block that owns the unit. In owned mode (BeginAsync / RunAsync) that
                        // block is the caller's, and it re-runs this publish with everything else, so the unit
                        // stays replayable. In observed mode someone else owns the commit edge — the EF save
                        // pipeline enlisting its own save — and replays it without re-running the domain-event
                        // handlers that published, so a row written here would be lost with the rolled-back
                        // attempt: mark before attempting storage. The save pipeline's own integration events are
                        // exempt because it re-publishes them on a replayed attempt. The one owned-mode case a
                        // replay cannot re-run — a handler publishing during the block's own SaveChanges, whose
                        // success clears the events — is the pipeline's to mark, since only it sees that drain.
                        decision.Coordination.UnitOfWork.PreventRetry();
                    }

                    var storageId = await writer.WriteAsync(prepared, decision, ct).ConfigureAwait(false);
                    receipt = new PublishReceipt(prepared.Message.Headers[Headers.MessageId], storageId);
                },
                cancellationToken
            )
            .ConfigureAwait(false);
        // Middleware may suppress publication before the request and its identity exist.
        return receipt;
    }

    private DeliveryCoordination _ResolveCoordination(IUnitOfWork? unitOfWork)
    {
        if (unitOfWork is null)
        {
            return DeliveryCoordination.None;
        }

        var resolver = coordinationResolver();

        if (resolver is not null)
        {
            // The storage decides: the in-memory storage joins a resource-less unit through its buffer, the
            // relational storages join only a same-database relational resource.
            return resolver.Resolve(unitOfWork);
        }

        // A storage with no resolver can join nothing. A resource-less unit then behaves like no unit at all
        // rather than as an incompatible one, so Optional still writes a standalone durable row.
        return unitOfWork.Resource is null
            ? DeliveryCoordination.None
            : DeliveryCoordination.Incompatible(DeliveryCoordinationMismatch.MissingRelationalCapability);
    }
}
