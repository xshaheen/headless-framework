// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.ObjectModel;
using System.Diagnostics;
using Headless.MultiTenancy;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace Headless.Messaging.Internal;

/// <summary>
/// Native <see cref="Activity"/>/<see cref="MessagingMetrics"/> emitter for the messaging pipeline. Each
/// emission site (publish, persist, consume, subscriber-invoke) calls the matching start/stop/error method; the
/// enricher pipeline runs synchronously at span start and W3C trace context + baggage are propagated through
/// message headers. Registered as a singleton by <c>AddHeadlessMessaging</c> with the enricher snapshot built
/// from <see cref="MessagingInstrumentationOptions"/>.
/// </summary>
internal sealed class MessagingTelemetry(
    IActivityTagEnricher[] enrichers,
    ILogger<MessagingTelemetry>? logger = null,
    TenantTelemetryOptions? tenantTelemetry = null
)
{
    private readonly TenantTelemetryOptions _tenantTelemetry = tenantTelemetry ?? new TenantTelemetryOptions();

    // The tenant attribute is written here rather than by an enricher because its name comes from
    // TenantTelemetryOptions, which is only resolvable from the container, while the enricher snapshot is built at
    // registration time.
    private readonly bool _hasEnrichers = enrichers.Length > 0 || (tenantTelemetry?.EnrichTraces ?? true);

    /// <summary>
    /// Shared fallback instance carrying the default tenant attribute and built-in enrichers (lane, delivery mode,
    /// retry-count). Used
    /// when an emission site is constructed outside the DI container (for example in unit tests) and no configured
    /// <see cref="MessagingTelemetry"/> is available. Production wiring always injects the DI singleton built from
    /// <see cref="MessagingInstrumentationOptions"/>.
    /// </summary>
    public static MessagingTelemetry Default { get; } = new(new MessagingInstrumentationOptions().BuildEnrichers());

    // --- Persist (message.persist) --------------------------------------------------------------------------

    public Activity? PersistStart(Message message, string operation, MessageLane lane, long startTimestampMs)
    {
        var extracted = _Extract(message.Headers);
        var parentContext = extracted.ActivityContext;

        if (parentContext == default)
        {
            parentContext = Activity.Current?.Context ?? default;
        }

        var activity = MessagingDiagnostics.Start("message.persist", ActivityKind.Internal, parentContext);
        if (activity is null)
        {
            // Pass-through relay (K4): the caller gate guarantees telemetry is enabled, but no span was started —
            // metrics-only config, or the sampler dropped it. Still stamp the stored message headers with the
            // upstream/ambient parent so the durable row carries traceparent for a later publish/consume to
            // continue the trace. Never fabricate a root: relay only when a parent actually exists.
            _RelayParentContext(extracted, message.Headers);

            return null;
        }

        activity.SetTag("messaging.destination.name", operation);
        activity.AddEvent(
            new ActivityEvent("message.persist.start", DateTimeOffset.FromUnixTimeMilliseconds(startTimestampMs))
        );

        if (_hasEnrichers)
        {
            _CallEnrichers(
                activity,
                _BuildEnrichmentContext(
                    MessagingEventKind.Persist,
                    message.Id,
                    operation,
                    lane,
                    message.Headers,
                    retryCount: 0
                )
            );
        }

        // Propagate the current context back into the stored message headers so the later publish/consume can
        // continue the trace — but only when there was an ambient/upstream parent, matching the bridge.
        if (parentContext != default && Activity.Current is not null)
        {
            _Inject(Activity.Current.Context, message.Headers);
        }

        return activity;
    }

    public static void PersistStop(
        Activity? activity,
        string operation,
        long startTimestampMs,
        long endTimestampMs,
        MessageLane? lane = null,
        DeliveryMetadataValues delivery = default
    )
    {
        var elapsedMs = endTimestampMs - startTimestampMs;

        activity?.AddEvent(
            new ActivityEvent(
                "message.persist.success",
                DateTimeOffset.FromUnixTimeMilliseconds(endTimestampMs),
                [new(MessagingTags.PersistenceDurationMs, elapsedMs)]
            )
        );

        MessagingMetrics.RecordPersistence(operation, elapsedMs, isPublish: true, lane, delivery);

        activity?.Stop();
    }

    public static void PersistError(Activity? activity, Exception exception)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity.AddException(exception);
        activity.Stop();
    }

    // --- Publish (message.publish) --------------------------------------------------------------------------

    public Activity? PublishStart(
        TransportMessage message,
        MessageLane lane,
        BrokerAddress broker,
        long startTimestampMs
    )
    {
        var extracted = _Extract(message.Headers);

        // Message size is a metric regardless of whether a span is sampled (subscribing to the meter is the toggle).
        MessagingMetrics.RecordMessageBodySize(message.Body.Length, message.Name, broker.Name);

        var activity = MessagingDiagnostics.Start("message.publish", ActivityKind.Producer, extracted.ActivityContext);
        if (activity is null)
        {
            // Pass-through relay (K4): the caller gate guarantees telemetry is enabled, but no span was started —
            // metrics-only config, or the sampler dropped it. Forward the incoming/ambient parent context verbatim
            // so a non-tracing service still continues the trace instead of silently dropping traceparent/baggage.
            _RelayParentContext(extracted, message.Headers);

            return null;
        }

        _SetOperationTags(
            activity,
            MessagingMetrics.OperationNamePublish,
            MessagingMetrics.OperationTypeSend,
            message.Name,
            broker.Name
        );
        activity.SetTag("messaging.message.id", message.Id);
        activity.SetTag("messaging.message.body.size", message.Body.Length);
        activity.SetTag("messaging.message.conversation_id", message.GetCorrelationId());
        _SetServerTags(activity, broker);
        activity.AddEvent(
            new ActivityEvent("message.publish.start", DateTimeOffset.FromUnixTimeMilliseconds(startTimestampMs))
        );

        if (_hasEnrichers)
        {
            _CallEnrichers(
                activity,
                _BuildEnrichmentContext(
                    MessagingEventKind.Publish,
                    message.Id,
                    message.Name,
                    lane,
                    message.Headers,
                    retryCount: 0
                )
            );
        }

        _Inject(activity.Context, message.Headers);

        return activity;
    }

    public static void PublishStop(
        Activity? activity,
        TransportMessage message,
        BrokerAddress broker,
        long startTimestampMs,
        long endTimestampMs,
        MessageLane lane = MessageLane.Bus
    )
    {
        var elapsedMs = endTimestampMs - startTimestampMs;

        activity?.AddEvent(
            new ActivityEvent(
                "message.publish.success",
                DateTimeOffset.FromUnixTimeMilliseconds(endTimestampMs),
                [new(MessagingTags.SendDurationMs, elapsedMs)]
            )
        );

        var delivery = DeliveryMetadata.Read(message.Headers);
        MessagingMetrics.RecordPublish(message.Name, broker, lane, delivery, elapsedMs);

        activity?.Stop();
    }

    public static void PublishError(
        Activity? activity,
        TransportMessage message,
        BrokerAddress broker,
        Exception exception,
        long? elapsedMs,
        MessageLane lane = MessageLane.Bus
    )
    {
        var errorType = ErrorTypeOf(exception);
        var delivery = DeliveryMetadata.Read(message.Headers);
        MessagingMetrics.RecordPublish(message.Name, broker, lane, delivery, elapsedMs, errorType);

        if (activity is null)
        {
            return;
        }

        activity.SetTag(MessagingMetrics.TagErrorType, errorType);
        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity.AddException(exception);
        activity.Stop();
    }

    public static void PublishAmbiguous(
        Activity? activity,
        TransportMessage message,
        BrokerAddress broker,
        Exception exception,
        long? elapsedMs,
        MessageLane lane = MessageLane.Bus
    )
    {
        var delivery = DeliveryMetadata.Read(message.Headers);
        MessagingMetrics.RecordPublish(
            message.Name,
            broker,
            lane,
            delivery,
            elapsedMs,
            MessagingMetrics.ErrorTypeAmbiguousDelivery
        );

        if (activity is null)
        {
            return;
        }

        activity.SetTag(MessagingTags.DeliveryOutcome, "ambiguous");
        activity.SetTag(MessagingMetrics.TagErrorType, MessagingMetrics.ErrorTypeAmbiguousDelivery);
        activity.SetStatus(ActivityStatusCode.Error, "Transport acceptance is ambiguous.");
        activity.AddException(exception);
        activity.AddEvent(new ActivityEvent("message.publish.ambiguous"));
        activity.Stop();
    }

    // --- Consume (message.consume) --------------------------------------------------------------------------

    public Activity? ConsumeStart(
        TransportMessage message,
        MessageLane lane,
        BrokerAddress broker,
        long startTimestampMs
    )
    {
        var parentContext = _Extract(message.Headers);
        Baggage.Current = parentContext.Baggage;

        // Stash the incoming context so a handler's outgoing publish can relay it when no span is started
        // (metrics-only, or sampled out). The AsyncLocal flows down into the subscriber handler; it is never
        // cleared — callee-side writes do not flow back to the dispatch loop, so it cannot leak across messages.
        if (parentContext != default)
        {
            MessagingAmbientContext.Current = parentContext;
        }

        var activity = MessagingDiagnostics.Start(
            "message.consume",
            ActivityKind.Consumer,
            parentContext.ActivityContext
        );
        if (activity is null)
        {
            return null;
        }

        _SetOperationTags(
            activity,
            MessagingMetrics.OperationNameReceive,
            MessagingMetrics.OperationTypeReceive,
            message.Name,
            broker.Name
        );
        activity.SetTag("messaging.message.id", message.Id);
        activity.SetTag("messaging.message.body.size", message.Body.Length);
        activity.SetTag("messaging.client.id", message.GetExecutionInstanceId());
        activity.SetTag(MessagingMetrics.TagConsumerGroupName, message.GetConsumerIdentity());
        _SetServerTags(activity, broker);
        activity.AddEvent(
            new ActivityEvent("message.consume.start", DateTimeOffset.FromUnixTimeMilliseconds(startTimestampMs))
        );

        if (_hasEnrichers)
        {
            _CallEnrichers(
                activity,
                _BuildEnrichmentContext(
                    MessagingEventKind.Consume,
                    message.Id,
                    message.Name,
                    lane,
                    message.Headers,
                    retryCount: 0
                )
            );
        }

        return activity;
    }

    public static void ConsumeStop(
        Activity? activity,
        TransportMessage message,
        BrokerAddress broker,
        long startTimestampMs,
        long endTimestampMs
    )
    {
        var elapsedMs = endTimestampMs - startTimestampMs;

        activity?.AddEvent(
            new ActivityEvent(
                "message.consume.success",
                DateTimeOffset.FromUnixTimeMilliseconds(endTimestampMs),
                [new(MessagingTags.ReceiveDurationMs, elapsedMs)]
            )
        );

        MessagingMetrics.RecordReceive(message.Name, broker, message.GetConsumerIdentity(), elapsedMs);
        MessagingMetrics.RecordPersistence(message.Name, elapsedMs, isPublish: false);

        activity?.Stop();
    }

    public static void ConsumeError(
        Activity? activity,
        TransportMessage message,
        BrokerAddress broker,
        Exception exception,
        long? elapsedMs
    )
    {
        var errorType = ErrorTypeOf(exception);
        MessagingMetrics.RecordReceive(message.Name, broker, message.GetConsumerIdentity(), elapsedMs, errorType);

        if (activity is null)
        {
            return;
        }

        activity.SetTag(MessagingMetrics.TagErrorType, errorType);
        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity.AddException(exception);
        activity.Stop();
    }

    // --- Subscriber invoke (subscriber.invoke) --------------------------------------------------------------

    public Activity? SubscriberInvokeStart(
        Message message,
        string operation,
        MessageLane lane,
        string method,
        int retryCount,
        long startTimestampMs,
        string? messagingSystem = null
    )
    {
        var context = default(ActivityContext);
        var propagatedContext = _Extract(message.Headers);

        if (propagatedContext != default)
        {
            context = propagatedContext.ActivityContext;
            Baggage.Current = propagatedContext.Baggage;

            // Stash the incoming context so this handler's outgoing publish can relay it when no span is started
            // (metrics-only, or sampled out). Flows down into the handler via AsyncLocal; never cleared.
            MessagingAmbientContext.Current = propagatedContext;
        }

        // A push-delivered message is processed in a consumer span, as the messaging conventions specify for the
        // process operation; it continues the producer's trace through the context the message carried.
        var activity = MessagingDiagnostics.Start("subscriber.invoke", ActivityKind.Consumer, context);
        if (activity is null)
        {
            return null;
        }

        _SetOperationTags(
            activity,
            MessagingMetrics.OperationNameProcess,
            MessagingMetrics.OperationTypeProcess,
            operation,
            messagingSystem
        );
        activity.SetTag(MessagingMetrics.TagConsumerGroupName, message.GetConsumerIdentity());
        activity.SetTag("code.function.name", method);
        activity.AddEvent(
            new ActivityEvent("subscriber.invoke.start", DateTimeOffset.FromUnixTimeMilliseconds(startTimestampMs))
        );

        if (_hasEnrichers)
        {
            _CallEnrichers(
                activity,
                _BuildEnrichmentContext(
                    MessagingEventKind.SubscriberInvoke,
                    message.Id,
                    operation,
                    lane,
                    message.Headers,
                    retryCount
                )
            );
        }

        return activity;
    }

    public static void SubscriberInvokeStop(
        Activity? activity,
        Message message,
        string method,
        string? messagingSystem,
        long startTimestampMs,
        long endTimestampMs
    )
    {
        var elapsedMs = endTimestampMs - startTimestampMs;

        activity?.AddEvent(
            new ActivityEvent(
                "subscriber.invoke.success",
                DateTimeOffset.FromUnixTimeMilliseconds(endTimestampMs),
                [new(MessagingTags.InvokeDurationMs, elapsedMs)]
            )
        );

        MessagingMetrics.RecordProcess(message.Name, messagingSystem, message.GetConsumerIdentity(), method, elapsedMs);

        activity?.Stop();
    }

    public static void SubscriberInvokeError(
        Activity? activity,
        Message message,
        string method,
        string? messagingSystem,
        Exception exception,
        long elapsedMs
    )
    {
        var errorType = ErrorTypeOf(exception);
        MessagingMetrics.RecordProcess(
            message.Name,
            messagingSystem,
            message.GetConsumerIdentity(),
            method,
            elapsedMs,
            errorType
        );

        if (activity is null)
        {
            return;
        }

        activity.SetTag(MessagingMetrics.TagErrorType, errorType);
        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity.AddException(exception);
        activity.Stop();
    }

    // --- Helpers --------------------------------------------------------------------------------------------

    /// <summary>
    /// The <c>error.type</c> value for a failed operation: the fully qualified type of the exception that caused it.
    /// The framework's own wrappers are skipped, so the value names the transport or handler failure rather than the
    /// wrapper every failure of that phase shares.
    /// </summary>
    internal static string ErrorTypeOf(Exception exception)
    {
        var failure = exception switch
        {
            SubscriberExecutionFailedException { InnerException: { } inner } => inner,
            PublisherSentFailedException { InnerException: { } inner } => inner,
            _ => exception,
        };
        var type = failure.GetType();

        return type.FullName ?? type.Name;
    }

    private static void _SetOperationTags(
        Activity activity,
        string operationName,
        string operationType,
        string destinationName,
        string? system
    )
    {
        // The conventions name a messaging span "{operation name} {destination}". The destination is the message
        // name, a value fixed by the application's message contracts, so the span name stays low-cardinality.
        activity.DisplayName = operationName + " " + destinationName;
        activity.SetTag(MessagingMetrics.TagOperationName, operationName);
        activity.SetTag(MessagingMetrics.TagOperationType, operationType);
        activity.SetTag(MessagingMetrics.TagDestinationName, destinationName);

        if (!string.IsNullOrEmpty(system))
        {
            activity.SetTag(MessagingMetrics.TagSystem, system);
        }
    }

    private static void _SetServerTags(Activity activity, BrokerAddress broker)
    {
        var server = MessagingServerEndpoint.From(broker);

        if (server.Address is { } address)
        {
            activity.SetTag(MessagingMetrics.TagServerAddress, address);
        }

        if (server.Port is { } port)
        {
            activity.SetTag(MessagingMetrics.TagServerPort, port);
        }
    }

    private static PropagationContext _Extract(IDictionary<string, string?> headers)
    {
        // Read the configured propagator dynamically: the app's OpenTelemetry setup assigns
        // Propagators.DefaultTextMapPropagator (W3C TraceContext + Baggage), and that may happen after this
        // type is first touched. Caching it in a static field would freeze a no-op propagator.
        return Propagators.DefaultTextMapPropagator.Extract(
            default,
            headers,
            static (carrier, key) =>
                carrier.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value) ? [value] : []
        );
    }

    /// <summary>
    /// Writes <paramref name="context"/> into <paramref name="headers"/>, for an envelope that leaves the process outside
    /// the publish pipeline, such as a reply to a request.
    /// </summary>
    internal static void InjectTraceContext(ActivityContext context, IDictionary<string, string?> headers)
    {
        _Inject(context, headers);
    }

    private static void _Inject(ActivityContext context, IDictionary<string, string?> headers)
    {
        _Inject(new PropagationContext(context, Baggage.Current), headers);
    }

    private static void _Inject(PropagationContext context, IDictionary<string, string?> headers)
    {
        Propagators.DefaultTextMapPropagator.Inject(
            context,
            headers,
            static (carrier, key, value) => carrier[key] = value
        );
    }

    /// <summary>
    /// Forwards the resolved upstream/ambient parent context (and its baggage) into <paramref name="headers"/>
    /// when a usable parent exists — never fabricating a root. Used by the publish/persist relay when no span was
    /// started (K4 pass-through) so a non-tracing service still continues the incoming trace.
    /// </summary>
    private static void _RelayParentContext(PropagationContext extracted, IDictionary<string, string?> headers)
    {
        var relay = _ResolveRelayContext(extracted);

        if (relay.ActivityContext != default)
        {
            _Inject(relay, headers);
        }
    }

    /// <summary>
    /// Resolves the parent context to relay when no span is started. Preference order: the context extracted from
    /// the message headers, then the ambient <see cref="Activity.Current"/>, then the consume-scope context stashed
    /// by <see cref="ConsumeStart"/> / <see cref="SubscriberInvokeStart"/> (which flows down into a subscriber
    /// handler). Baggage is relayed verbatim from whichever source wins.
    /// </summary>
    private static PropagationContext _ResolveRelayContext(PropagationContext extracted)
    {
        if (extracted.ActivityContext != default)
        {
            return extracted;
        }

        if (Activity.Current is { } current)
        {
            return new PropagationContext(current.Context, Baggage.Current);
        }

        return MessagingAmbientContext.Current;
    }

    private static MessagingEnrichmentContext _BuildEnrichmentContext(
        MessagingEventKind kind,
        string messageId,
        string operation,
        MessageLane lane,
        IDictionary<string, string?> headers,
        int retryCount
    )
    {
        var delivery = DeliveryMetadata.Read(headers);
        return new MessagingEnrichmentContext
        {
            Kind = kind,
            MessageId = messageId,
            MessageName = operation,
            Lane = lane,
            RequestedDeliveryMode = delivery.RequestedDeliveryMode,
            ResolvedDeliveryMode = delivery.ResolvedDeliveryMode,
            TenantId = headers.TryGetValue(Headers.TenantId, out var tid) ? tid : null,
            CorrelationId = headers.TryGetValue(Headers.CorrelationId, out var cid) ? cid : null,
            RetryCount = retryCount,
            Headers =
                headers as IReadOnlyDictionary<string, string?> ?? new ReadOnlyDictionary<string, string?>(headers),
        };
    }

    private void _CallEnrichers(Activity activity, in MessagingEnrichmentContext context)
    {
        // Runs before every enricher so a custom enricher can still read or override the attribute.
        if (!string.IsNullOrWhiteSpace(context.TenantId))
        {
            TenantTelemetry.TagActivity(activity, _tenantTelemetry, context.TenantId);
        }

        for (var i = 0; i < enrichers.Length; i++)
        {
            var enricher = enrichers[i];

            try
            {
                enricher.Enrich(activity, context);
            }
            catch (Exception ex)
            {
                if (logger is not null)
                {
                    MessagingTelemetryLog.EnricherFailed(
                        logger,
                        ex,
                        enricher.GetType().FullName ?? enricher.GetType().Name
                    );
                }
            }
        }
    }
}

internal static partial class MessagingTelemetryLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Enricher {EnricherType} threw an exception and was skipped")]
    internal static partial void EnricherFailed(ILogger logger, Exception ex, string enricherType);
}
