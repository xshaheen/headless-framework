// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using Headless.Messaging.Messages;
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
internal sealed class MessagingTelemetry(IActivityTagEnricher[] enrichers, ILogger<MessagingTelemetry>? logger = null)
{
    private readonly IActivityTagEnricher[] _enrichers = enrichers;
    private readonly ILogger<MessagingTelemetry>? _logger = logger;
    private readonly bool _hasEnrichers = enrichers.Length > 0;

    /// <summary>
    /// Shared fallback instance carrying the default built-in enrichers (tenant-id, intent, retry-count). Used
    /// when an emission site is constructed outside the DI container (for example in unit tests) and no configured
    /// <see cref="MessagingTelemetry"/> is available. Production wiring always injects the DI singleton built from
    /// <see cref="MessagingInstrumentationOptions"/>.
    /// </summary>
    public static MessagingTelemetry Default { get; } = new(new MessagingInstrumentationOptions().BuildEnrichers());

    // --- Persist (message.persist) --------------------------------------------------------------------------

    public Activity? PersistStart(Message message, string operation, IntentType intentType, long startTimestampMs)
    {
        var parentContext = _Extract(message.Headers).ActivityContext;

        if (parentContext == default)
        {
            parentContext = Activity.Current?.Context ?? default;
        }

        var activity = MessagingDiagnostics.Start("message.persist", ActivityKind.Internal, parentContext);
        if (activity is null)
        {
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
                    intentType,
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

    public static void PersistStop(Activity? activity, string operation, long startTimestampMs, long endTimestampMs)
    {
        var elapsedMs = endTimestampMs - startTimestampMs;

        activity?.AddEvent(
            new ActivityEvent(
                "message.persist.success",
                DateTimeOffset.FromUnixTimeMilliseconds(endTimestampMs),
                [new(MessagingTags.PersistenceDurationMs, elapsedMs)]
            )
        );

        MessagingMetrics.RecordPersistence(operation, elapsedMs, isPublish: true);

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
        IntentType intentType,
        BrokerAddress broker,
        long startTimestampMs
    )
    {
        var parentContext = _Extract(message.Headers).ActivityContext;

        // Message size is a metric regardless of whether a span is sampled (subscribing to the meter is the toggle).
        MessagingMetrics.RecordMessageSize(message.Body.Length, message.Name);

        var activity = MessagingDiagnostics.Start("message.publish", ActivityKind.Producer, parentContext);
        if (activity is null)
        {
            return null;
        }

        activity.SetTag("messaging.system", broker.Name);
        activity.SetTag("messaging.message.id", message.Id);
        activity.SetTag("messaging.message.body.size", message.Body.Length);
        activity.SetTag("messaging.message.conversation_id", message.GetCorrelationId());
        activity.SetTag("messaging.destination.name", message.Name);
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
                    intentType,
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
        long endTimestampMs
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

        MessagingMetrics.RecordPublish(message.Name, broker.Name, elapsedMs);

        activity?.Stop();
    }

    public static void PublishError(
        Activity? activity,
        TransportMessage message,
        BrokerAddress broker,
        Exception exception
    )
    {
        MessagingMetrics.RecordPublishError(message.Name, broker.Name, exception.GetType().Name);

        if (activity is null)
        {
            return;
        }

        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity.AddException(exception);
        activity.Stop();
    }

    // --- Consume (message.consume) --------------------------------------------------------------------------

    public Activity? ConsumeStart(
        TransportMessage message,
        IntentType intentType,
        BrokerAddress broker,
        long startTimestampMs
    )
    {
        var parentContext = _Extract(message.Headers);
        Baggage.Current = parentContext.Baggage;

        var activity = MessagingDiagnostics.Start(
            "message.consume",
            ActivityKind.Consumer,
            parentContext.ActivityContext
        );
        if (activity is null)
        {
            return null;
        }

        activity.SetTag("messaging.system", broker.Name);
        activity.SetTag("messaging.message.id", message.Id);
        activity.SetTag("messaging.message.body.size", message.Body.Length);
        activity.SetTag("messaging.operation.type", "receive");
        activity.SetTag("messaging.client.id", message.GetExecutionInstanceId());
        activity.SetTag("messaging.destination.name", message.Name);
        activity.SetTag("messaging.consumer.group.name", message.GetGroup());
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
                    intentType,
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

        MessagingMetrics.RecordConsume(message.Name, broker.Name, message.GetGroup(), elapsedMs);
        MessagingMetrics.RecordPersistence(message.Name, elapsedMs, isPublish: false);

        activity?.Stop();
    }

    public static void ConsumeError(
        Activity? activity,
        TransportMessage message,
        BrokerAddress broker,
        Exception exception
    )
    {
        MessagingMetrics.RecordConsumeError(message.Name, broker.Name, exception.GetType().Name, message.GetGroup());

        if (activity is null)
        {
            return;
        }

        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity.AddException(exception);
        activity.Stop();
    }

    // --- Subscriber invoke (subscriber.invoke) --------------------------------------------------------------

    public Activity? SubscriberInvokeStart(
        Message message,
        string operation,
        IntentType intentType,
        MethodInfo method,
        int retryCount,
        long startTimestampMs
    )
    {
        var context = default(ActivityContext);
        var propagatedContext = _Extract(message.Headers);

        if (propagatedContext != default)
        {
            context = propagatedContext.ActivityContext;
            Baggage.Current = propagatedContext.Baggage;
        }

        var activity = MessagingDiagnostics.Start("subscriber.invoke", ActivityKind.Internal, context);
        if (activity is null)
        {
            return null;
        }

        activity.SetTag("code.function.name", method.Name);
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
                    intentType,
                    message.Headers,
                    retryCount
                )
            );
        }

        return activity;
    }

    public static void SubscriberInvokeStop(
        Activity? activity,
        string operation,
        MethodInfo method,
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

        MessagingMetrics.RecordSubscriberInvocation(method.Name, operation, elapsedMs);

        activity?.Stop();
    }

    public static void SubscriberInvokeError(
        Activity? activity,
        string operation,
        MethodInfo method,
        Exception exception
    )
    {
        MessagingMetrics.RecordSubscriberError(method.Name, operation, exception.GetType().Name);

        if (activity is null)
        {
            return;
        }

        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity.AddException(exception);
        activity.Stop();
    }

    // --- Helpers --------------------------------------------------------------------------------------------

    private static void _SetServerTags(Activity activity, BrokerAddress broker)
    {
        if (broker.Endpoint is not { } endpoint)
        {
            return;
        }

        // Manual first/second-colon slicing instead of Split: this runs per sampled span, and Split allocates
        // a string[] plus a port substring for a value that is parsed straight into an int.
        var separatorIndex = endpoint.IndexOf(':', StringComparison.Ordinal);

        if (separatorIndex < 0)
        {
            activity.SetTag("server.address", endpoint);

            return;
        }

        activity.SetTag("server.address", endpoint[..separatorIndex]);

        var portSpan = endpoint.AsSpan(separatorIndex + 1);
        var portEnd = portSpan.IndexOf(':');

        if (portEnd >= 0)
        {
            portSpan = portSpan[..portEnd];
        }

        if (int.TryParse(portSpan, CultureInfo.InvariantCulture, out var port))
        {
            activity.SetTag("server.port", port);
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

    private static void _Inject(ActivityContext context, IDictionary<string, string?> headers)
    {
        Propagators.DefaultTextMapPropagator.Inject(
            new PropagationContext(context, Baggage.Current),
            headers,
            static (carrier, key, value) => carrier[key] = value
        );
    }

    private static MessagingEnrichmentContext _BuildEnrichmentContext(
        MessagingEventKind kind,
        string messageId,
        string operation,
        IntentType intentType,
        IDictionary<string, string?> headers,
        int retryCount
    )
    {
        return new MessagingEnrichmentContext
        {
            Kind = kind,
            MessageId = messageId,
            MessageName = operation,
            IntentType = intentType,
            TenantId = headers.TryGetValue(Headers.TenantId, out var tid) ? tid : null,
            CorrelationId = headers.TryGetValue(Headers.CorrelationId, out var cid) ? cid : null,
            RetryCount = retryCount,
            Headers =
                headers as IReadOnlyDictionary<string, string?> ?? new ReadOnlyDictionary<string, string?>(headers),
        };
    }

    private void _CallEnrichers(Activity activity, in MessagingEnrichmentContext context)
    {
        for (var i = 0; i < _enrichers.Length; i++)
        {
            var enricher = _enrichers[i];

            try
            {
                enricher.Enrich(activity, context);
            }
            catch (Exception ex)
            {
                if (_logger is not null)
                {
                    MessagingTelemetryLog.EnricherFailed(
                        _logger,
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
