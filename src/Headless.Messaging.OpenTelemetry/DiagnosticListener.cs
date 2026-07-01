// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.ObjectModel;
using System.Diagnostics;
using Headless.Messaging.Diagnostics;
using Headless.Messaging.Messages;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace Headless.Messaging.OpenTelemetry;

internal sealed class DiagnosticListener(
    IActivityTagEnricher[] enrichers,
    ILogger<DiagnosticListener>? logger = null,
    MessagingMetrics? metrics = null
) : IObserver<KeyValuePair<string, object?>>
{
    public const string SourceName = MessagingDiagnostics.SourceName;

    private static readonly TextMapPropagator _Propagator = Propagators.DefaultTextMapPropagator;

    private readonly bool _hasEnrichers = enrichers.Length > 0;

    public void OnCompleted() { }

    public void OnError(Exception error) { }

    public void OnNext(KeyValuePair<string, object?> evt)
    {
        switch (evt.Key)
        {
            case MessageDiagnosticListenerNames.BeforePublishMessageStore:
                {
                    var eventData = (MessageEventDataPubStore)evt.Value!;

                    var parentContext = _Propagator
                        .Extract(
                            default,
                            eventData.Message,
                            (msg, key) =>
                            {
                                if (msg.Headers.TryGetValue(key, out var value) && value != null)
                                {
                                    return [value];
                                }

                                return [];
                            }
                        )
                        .ActivityContext;

                    if (parentContext == default)
                    {
                        parentContext = Activity.Current?.Context ?? default;
                    }
                    var activity = MessagingDiagnostics.Start("message.persist", ActivityKind.Internal, parentContext);
                    if (activity != null)
                    {
                        activity.SetTag("messaging.destination.name", eventData.Operation);
                        activity.AddEvent(
                            new ActivityEvent(
                                "message.persist.start",
                                DateTimeOffset.FromUnixTimeMilliseconds(eventData.OperationTimestamp!.Value)
                            )
                        );

                        if (_hasEnrichers)
                        {
                            _CallEnrichers(
                                activity,
                                _BuildEnrichmentContext(
                                    MessagingEventKind.Persist,
                                    eventData.Message.Id,
                                    eventData.Operation,
                                    eventData.IntentType,
                                    eventData.Message.Headers,
                                    retryCount: 0
                                ),
                                eventData.CancellationToken
                            );
                        }

                        if (parentContext != default && Activity.Current != null)
                        {
                            _Propagator.Inject(
                                new PropagationContext(Activity.Current.Context, Baggage.Current),
                                eventData.Message,
                                (msg, key, value) => msg.Headers[key] = value
                            );
                        }
                    }
                }
                break;
            case MessageDiagnosticListenerNames.AfterPublishMessageStore:
                {
                    var eventData = (MessageEventDataPubStore)evt.Value!;

                    Activity.Current?.AddEvent(
                        new ActivityEvent(
                            "message.persist.success",
                            DateTimeOffset.FromUnixTimeMilliseconds(eventData.OperationTimestamp!.Value),
                            [new(MessagingTags.PersistenceDurationMs, eventData.ElapsedTimeMs)]
                        )
                    );

                    if (eventData.ElapsedTimeMs.HasValue)
                    {
                        metrics?.RecordPersistence(eventData.Operation, eventData.ElapsedTimeMs.Value, isPublish: true);
                    }

                    Activity.Current?.Stop();
                }
                break;
            case MessageDiagnosticListenerNames.ErrorPublishMessageStore:
                {
                    var eventData = (MessageEventDataPubStore)evt.Value!;
                    if (Activity.Current is { } activity)
                    {
                        var exception = eventData.Exception!;
                        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
                        activity.AddException(exception);
                        activity.Stop();
                    }
                }
                break;
            case MessageDiagnosticListenerNames.BeforePublish:
                {
                    var eventData = (MessageEventDataPubSend)evt.Value!;
                    var parentContext = _Propagator.Extract(
                        default,
                        eventData.TransportMessage,
                        (msg, key) =>
                        {
                            if (msg.Headers.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value))
                            {
                                return [value];
                            }

                            return [];
                        }
                    );

                    var activity = MessagingDiagnostics.Start(
                        "message.publish",
                        ActivityKind.Producer,
                        parentContext.ActivityContext
                    );
                    if (activity != null)
                    {
                        activity.SetTag("messaging.system", eventData.BrokerAddress.Name);
                        activity.SetTag("messaging.message.id", eventData.TransportMessage.Id);
                        activity.SetTag("messaging.message.body.size", eventData.TransportMessage.Body.Length);
                        activity.SetTag(
                            "messaging.message.conversation_id",
                            eventData.TransportMessage.GetCorrelationId()
                        );
                        activity.SetTag("messaging.destination.name", eventData.Operation);

                        metrics?.RecordMessageSize(eventData.TransportMessage.Body.Length, eventData.Operation);
                        if (eventData.BrokerAddress.Endpoint is { } endpoint)
                        {
                            var parts = endpoint.Split(':');
                            if (parts.Length > 0)
                            {
                                activity.SetTag("server.address", parts[0]);
                            }
                            if (parts.Length > 1 && int.TryParse(parts[1], CultureInfo.InvariantCulture, out var port))
                            {
                                activity.SetTag("server.port", port);
                            }
                        }
                        activity.AddEvent(
                            new ActivityEvent(
                                "message.publish.start",
                                DateTimeOffset.FromUnixTimeMilliseconds(eventData.OperationTimestamp!.Value)
                            )
                        );

                        if (_hasEnrichers)
                        {
                            _CallEnrichers(
                                activity,
                                _BuildEnrichmentContext(
                                    MessagingEventKind.Publish,
                                    eventData.TransportMessage.Id,
                                    eventData.Operation,
                                    eventData.IntentType,
                                    eventData.TransportMessage.Headers,
                                    retryCount: 0
                                ),
                                eventData.CancellationToken
                            );
                        }

                        _Propagator.Inject(
                            new PropagationContext(activity.Context, Baggage.Current),
                            eventData.TransportMessage,
                            (msg, key, value) => msg.Headers[key] = value
                        );
                    }
                }
                break;
            case MessageDiagnosticListenerNames.AfterPublish:
                {
                    var eventData = (MessageEventDataPubSend)evt.Value!;
                    if (Activity.Current is { } activity)
                    {
                        activity.AddEvent(
                            new ActivityEvent(
                                "message.publish.success",
                                DateTimeOffset.FromUnixTimeMilliseconds(eventData.OperationTimestamp!.Value),
                                [new(MessagingTags.SendDurationMs, eventData.ElapsedTimeMs)]
                            )
                        );

                        metrics?.RecordPublish(
                            eventData.Operation,
                            eventData.BrokerAddress.Name,
                            eventData.ElapsedTimeMs
                        );

                        activity.Stop();
                    }
                }
                break;
            case MessageDiagnosticListenerNames.ErrorPublish:
                {
                    var eventData = (MessageEventDataPubSend)evt.Value!;
                    if (Activity.Current is { } activity)
                    {
                        var exception = eventData.Exception!;
                        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
                        activity.AddException(exception);

                        metrics?.RecordPublishError(
                            eventData.Operation,
                            eventData.BrokerAddress.Name,
                            exception.GetType().Name
                        );

                        activity.Stop();
                    }
                }
                break;
            case MessageDiagnosticListenerNames.BeforeConsume:
                {
                    var eventData = (MessageEventDataSubStore)evt.Value!;
                    var parentContext = _Propagator.Extract(
                        default,
                        eventData.TransportMessage,
                        (msg, key) =>
                        {
                            if (msg.Headers.TryGetValue(key, out var value) && value != null)
                            {
                                return [value];
                            }

                            return [];
                        }
                    );

                    Baggage.Current = parentContext.Baggage;
                    var activity = MessagingDiagnostics.Start(
                        "message.consume",
                        ActivityKind.Consumer,
                        parentContext.ActivityContext
                    );

                    if (activity != null)
                    {
                        activity.SetTag("messaging.system", eventData.BrokerAddress.Name);
                        activity.SetTag("messaging.message.id", eventData.TransportMessage.Id);
                        activity.SetTag("messaging.message.body.size", eventData.TransportMessage.Body.Length);
                        activity.SetTag("messaging.operation.type", "receive");
                        activity.SetTag("messaging.client.id", eventData.TransportMessage.GetExecutionInstanceId());
                        activity.SetTag("messaging.destination.name", eventData.Operation);
                        activity.SetTag("messaging.consumer.group.name", eventData.TransportMessage.GetGroup());
                        if (eventData.BrokerAddress.Endpoint is { } endpoint)
                        {
                            var parts = endpoint.Split(':');
                            if (parts.Length > 0)
                            {
                                activity.SetTag("server.address", parts[0]);
                            }
                            if (parts.Length > 1 && int.TryParse(parts[1], CultureInfo.InvariantCulture, out var port))
                            {
                                activity.SetTag("server.port", port);
                            }
                        }
                        activity.AddEvent(
                            new ActivityEvent(
                                "message.consume.start",
                                DateTimeOffset.FromUnixTimeMilliseconds(eventData.OperationTimestamp!.Value)
                            )
                        );

                        if (_hasEnrichers)
                        {
                            _CallEnrichers(
                                activity,
                                _BuildEnrichmentContext(
                                    MessagingEventKind.Consume,
                                    eventData.TransportMessage.Id,
                                    eventData.Operation,
                                    eventData.IntentType,
                                    eventData.TransportMessage.Headers,
                                    retryCount: 0
                                ),
                                eventData.CancellationToken
                            );
                        }
                    }
                }
                break;
            case MessageDiagnosticListenerNames.AfterConsume:
                {
                    var eventData = (MessageEventDataSubStore)evt.Value!;
                    if (Activity.Current is { } activity)
                    {
                        activity.AddEvent(
                            new ActivityEvent(
                                "message.consume.success",
                                DateTimeOffset.FromUnixTimeMilliseconds(eventData.OperationTimestamp!.Value),
                                [new(MessagingTags.ReceiveDurationMs, eventData.ElapsedTimeMs)]
                            )
                        );

                        metrics?.RecordConsume(
                            eventData.Operation,
                            eventData.BrokerAddress.Name,
                            eventData.TransportMessage.GetGroup(),
                            eventData.ElapsedTimeMs
                        );

                        if (eventData.ElapsedTimeMs.HasValue)
                        {
                            metrics?.RecordPersistence(
                                eventData.Operation,
                                eventData.ElapsedTimeMs.Value,
                                isPublish: false
                            );
                        }

                        activity.Stop();
                    }
                }
                break;
            case MessageDiagnosticListenerNames.ErrorConsume:
                {
                    var eventData = (MessageEventDataSubStore)evt.Value!;
                    if (Activity.Current is { } activity)
                    {
                        var exception = eventData.Exception!;
                        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
                        activity.AddException(exception);

                        metrics?.RecordConsumeError(
                            eventData.Operation,
                            eventData.BrokerAddress.Name,
                            exception.GetType().Name,
                            eventData.TransportMessage.GetGroup()
                        );

                        activity.Stop();
                    }
                }
                break;
            case MessageDiagnosticListenerNames.BeforeSubscriberInvoke:
                {
                    ActivityContext context = default;
                    var eventData = (MessageEventDataSubExecute)evt.Value!;
                    var propagatedContext = _Propagator.Extract(
                        default,
                        eventData.Message,
                        (msg, key) =>
                        {
                            if (msg.Headers.TryGetValue(key, out var value) && value != null)
                            {
                                return [value];
                            }

                            return [];
                        }
                    );

                    if (propagatedContext != default)
                    {
                        context = propagatedContext.ActivityContext;
                        Baggage.Current = propagatedContext.Baggage;
                    }

                    var activity = MessagingDiagnostics.Start("subscriber.invoke", ActivityKind.Internal, context);

                    if (activity != null)
                    {
                        activity.SetTag("code.function.name", eventData.MethodInfo!.Name);

                        // The retry-count tag is now owned by RetryCountTagEnricher (registered
                        // by default; suppressible via MessagingInstrumentationOptions.SuppressRetryCountTag).

                        activity.AddEvent(
                            new ActivityEvent(
                                "subscriber.invoke.start",
                                DateTimeOffset.FromUnixTimeMilliseconds(eventData.OperationTimestamp!.Value)
                            )
                        );

                        if (_hasEnrichers)
                        {
                            _CallEnrichers(
                                activity,
                                _BuildEnrichmentContext(
                                    MessagingEventKind.SubscriberInvoke,
                                    eventData.Message.Id,
                                    eventData.Operation,
                                    eventData.IntentType,
                                    eventData.Message.Headers,
                                    retryCount: eventData.RetryCount
                                ),
                                eventData.CancellationToken
                            );
                        }
                    }
                }
                break;
            case MessageDiagnosticListenerNames.AfterSubscriberInvoke:
                {
                    var eventData = (MessageEventDataSubExecute)evt.Value!;
                    if (Activity.Current is { } activity)
                    {
                        activity.AddEvent(
                            new ActivityEvent(
                                "subscriber.invoke.success",
                                DateTimeOffset.FromUnixTimeMilliseconds(eventData.OperationTimestamp!.Value),
                                [new(MessagingTags.InvokeDurationMs, eventData.ElapsedTimeMs)]
                            )
                        );

                        metrics?.RecordSubscriberInvocation(
                            eventData.MethodInfo!.Name,
                            eventData.Operation,
                            eventData.ElapsedTimeMs
                        );

                        activity.Stop();
                    }
                }
                break;
            case MessageDiagnosticListenerNames.ErrorSubscriberInvoke:
                {
                    var eventData = (MessageEventDataSubExecute)evt.Value!;
                    if (Activity.Current is { } activity)
                    {
                        var exception = eventData.Exception!;
                        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
                        activity.AddException(exception);

                        metrics?.RecordSubscriberError(
                            eventData.MethodInfo!.Name,
                            eventData.Operation,
                            exception.GetType().Name
                        );

                        activity.Stop();
                    }
                }
                break;
        }
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

    private void _CallEnrichers(
        Activity activity,
        in MessagingEnrichmentContext context,
        CancellationToken cancellationToken
    )
    {
        // Fast path: enrichers that complete synchronously have their tags applied before this
        // method returns. Async tails are observed but not awaited — see IActivityTagEnricher.Enrich
        // remarks for the fire-and-forget contract.
        for (var i = 0; i < enrichers.Length; i++)
        {
            var enricher = enrichers[i];
            ValueTask vt;
            try
            {
                vt = enricher.Enrich(activity, context, cancellationToken);
            }
            catch (Exception ex)
            {
                _LogEnricherException(enricher, ex);
                continue;
            }

            if (vt.IsCompletedSuccessfully)
            {
                continue;
            }

            if (vt.IsCompleted)
            {
                try
                {
#pragma warning disable MA0045 // _CallEnrichers is intentionally synchronous (fire-and-forget async tail); GetResult() runs only on an already-completed ValueTask to observe exceptions, so it never blocks.
                    vt.GetAwaiter().GetResult();
#pragma warning restore MA0045
                }
                catch (Exception ex)
                {
                    _LogEnricherException(enricher, ex);
                }

                continue;
            }

            // Async tail: fire-and-forget. Tags added after the activity stops are dropped.
            _ = _ObserveAsyncEnricherAsync(enricher, vt);
        }
    }

    private async Task _ObserveAsyncEnricherAsync(IActivityTagEnricher enricher, ValueTask vt)
    {
        try
        {
            await vt.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _LogEnricherException(enricher, ex);
        }
    }

    private void _LogEnricherException(IActivityTagEnricher enricher, Exception ex)
    {
        if (logger is not null)
        {
            DiagnosticListenerLog.EnricherFailed(logger, ex, enricher.GetType().FullName ?? enricher.GetType().Name);
        }
    }
}

internal static partial class DiagnosticListenerLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Enricher {EnricherType} threw an exception and was skipped")]
    internal static partial void EnricherFailed(ILogger logger, Exception ex, string enricherType);
}
