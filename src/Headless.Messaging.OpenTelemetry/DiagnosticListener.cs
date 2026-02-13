// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Messaging.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace Headless.Messaging.OpenTelemetry;

internal class DiagnosticListener : IObserver<KeyValuePair<string, object?>>
{
    public const string SourceName = "Headless.Messaging.OpenTelemetry";

    private const string _OperateNamePrefix = "Headless.Messaging/";
    private const string _ProducerOperateNameSuffix = "/Publisher";
    private const string _ConsumerOperateNameSuffix = "/Subscriber";
    private static readonly ActivitySource _ActivitySource = new(SourceName, "1.0.0");
    private static readonly TextMapPropagator _Propagator = Propagators.DefaultTextMapPropagator;

    private readonly MessagingMetrics? _metrics;

    public DiagnosticListener(MessagingMetrics? metrics = null)
    {
        _metrics = metrics;
    }

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
                    var activity = _ActivitySource.StartActivity(
                        "Event Persistence: " + eventData.Operation,
                        ActivityKind.Internal,
                        parentContext
                    );
                    if (activity != null)
                    {
                        activity.SetTag("messaging.destination.name", eventData.Operation);
                        activity.AddEvent(
                            new ActivityEvent(
                                "Message persistence start...",
                                DateTimeOffset.FromUnixTimeMilliseconds(eventData.OperationTimestamp!.Value)
                            )
                        );

                        if (parentContext != default && Activity.Current != null)
                        {
                            _Propagator.Inject(
                                new PropagationContext(Activity.Current.Context, Baggage.Current),
                                eventData.Message,
                                (msg, key, value) =>
                                {
                                    msg.Headers[key] = value;
                                }
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
                            "Message persistence succeeded!",
                            DateTimeOffset.FromUnixTimeMilliseconds(eventData.OperationTimestamp!.Value),
                            new ActivityTagsCollection
                            {
                                new("messaging.persistence.duration", eventData.ElapsedTimeMs),
                            }
                        )
                    );

                    if (eventData.ElapsedTimeMs.HasValue)
                    {
                        _metrics?.RecordPersistence(
                            eventData.Operation,
                            eventData.ElapsedTimeMs.Value,
                            isPublish: true
                        );
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

                    var activity = _ActivitySource.StartActivity(
                        _OperateNamePrefix + eventData.Operation + _ProducerOperateNameSuffix,
                        ActivityKind.Producer,
                        parentContext.ActivityContext
                    );
                    if (activity != null)
                    {
                        activity.SetTag("messaging.system", eventData.BrokerAddress.Name);
                        activity.SetTag("messaging.message.id", eventData.TransportMessage.GetId());
                        activity.SetTag("messaging.message.body.size", eventData.TransportMessage.Body.Length);
                        activity.SetTag(
                            "messaging.message.conversation_id",
                            eventData.TransportMessage.GetCorrelationId()
                        );
                        activity.SetTag("messaging.destination.name", eventData.Operation);

                        _metrics?.RecordMessageSize(eventData.TransportMessage.Body.Length, eventData.Operation);
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
                                "Message publishing start...",
                                DateTimeOffset.FromUnixTimeMilliseconds(eventData.OperationTimestamp!.Value)
                            )
                        );

                        _Propagator.Inject(
                            new PropagationContext(activity.Context, Baggage.Current),
                            eventData.TransportMessage,
                            (msg, key, value) =>
                            {
                                msg.Headers[key] = value;
                            }
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
                                "Message publishing succeeded!",
                                DateTimeOffset.FromUnixTimeMilliseconds(eventData.OperationTimestamp!.Value),
                                new ActivityTagsCollection { new("messaging.send.duration", eventData.ElapsedTimeMs) }
                            )
                        );

                        _metrics?.RecordPublish(
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

                        _metrics?.RecordPublishError(
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
                    var activity = _ActivitySource.StartActivity(
                        _OperateNamePrefix + eventData.Operation + _ConsumerOperateNameSuffix,
                        ActivityKind.Consumer,
                        parentContext.ActivityContext
                    );

                    if (activity != null)
                    {
                        activity.SetTag("messaging.system", eventData.BrokerAddress.Name);
                        activity.SetTag("messaging.message.id", eventData.TransportMessage.GetId());
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
                                "Message persistence start...",
                                DateTimeOffset.FromUnixTimeMilliseconds(eventData.OperationTimestamp!.Value)
                            )
                        );
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
                                "Message persistence succeeded!",
                                DateTimeOffset.FromUnixTimeMilliseconds(eventData.OperationTimestamp!.Value),
                                new ActivityTagsCollection
                                {
                                    new("messaging.receive.duration", eventData.ElapsedTimeMs),
                                }
                            )
                        );

                        _metrics?.RecordConsume(
                            eventData.Operation,
                            eventData.BrokerAddress.Name,
                            eventData.TransportMessage.GetGroup(),
                            eventData.ElapsedTimeMs
                        );

                        if (eventData.ElapsedTimeMs.HasValue)
                        {
                            _metrics?.RecordPersistence(
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

                        _metrics?.RecordConsumeError(
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

                    var activity = _ActivitySource.StartActivity(
                        "Subscriber Invoke: " + eventData.MethodInfo!.Name,
                        ActivityKind.Internal,
                        context
                    );

                    if (activity != null)
                    {
                        activity.SetTag("code.function.name", eventData.MethodInfo.Name);

                        activity.AddEvent(
                            new ActivityEvent(
                                "Begin invoke the subscriber:" + eventData.MethodInfo.Name,
                                DateTimeOffset.FromUnixTimeMilliseconds(eventData.OperationTimestamp!.Value)
                            )
                        );
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
                                "Subscriber invoke succeeded!",
                                DateTimeOffset.FromUnixTimeMilliseconds(eventData.OperationTimestamp!.Value),
                                new ActivityTagsCollection { new("messaging.invoke.duration", eventData.ElapsedTimeMs) }
                            )
                        );

                        _metrics?.RecordSubscriberInvocation(
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

                        _metrics?.RecordSubscriberError(
                            eventData.MethodInfo!.Name,
                            eventData.Operation,
                            exception.GetType().Name
                        );

                        activity.Stop();
                    }
                }
                break;
            case MessageDiagnosticListenerNames.BeforeScheduledJobDispatch:
                {
                    var eventData = (ScheduledJobEventData)evt.Value!;
                    var activity = _ActivitySource.StartActivity(
                        $"Messaging.ScheduledJob/{eventData.JobName}/Dispatch",
                        ActivityKind.Internal
                    );
                    if (activity != null)
                    {
                        activity.SetTag("messaging.job.name", eventData.JobName);
                        activity.SetTag("messaging.job.execution_id", eventData.ExecutionId.ToString());
                        activity.SetTag("messaging.job.attempt", eventData.Attempt);
                        activity.SetTag("messaging.job.scheduled_time", eventData.ScheduledTime.ToString("O"));
                        activity.AddEvent(
                            new ActivityEvent(
                                "Scheduled job dispatch start",
                                DateTimeOffset.FromUnixTimeMilliseconds(eventData.OperationTimestamp!.Value)
                            )
                        );
                    }
                }
                break;
            case MessageDiagnosticListenerNames.AfterScheduledJobDispatch:
                {
                    var eventData = (ScheduledJobEventData)evt.Value!;
                    if (Activity.Current is { } activity)
                    {
                        activity.SetTag("messaging.job.success", true);
                        if (eventData.ElapsedTimeMs.HasValue)
                        {
                            activity.SetTag("messaging.job.duration_ms", eventData.ElapsedTimeMs.Value);
                        }
                        activity.AddEvent(
                            new ActivityEvent(
                                "Scheduled job dispatch succeeded",
                                DateTimeOffset.FromUnixTimeMilliseconds(eventData.OperationTimestamp!.Value),
                                new ActivityTagsCollection { new("messaging.job.duration_ms", eventData.ElapsedTimeMs) }
                            )
                        );

                        _metrics?.RecordScheduledJobExecution(eventData.JobName, eventData.ElapsedTimeMs);

                        activity.Stop();
                    }
                }
                break;
            case MessageDiagnosticListenerNames.ErrorScheduledJobDispatch:
                {
                    var eventData = (ScheduledJobEventData)evt.Value!;
                    if (Activity.Current is { } activity)
                    {
                        var exception = eventData.Exception!;
                        activity.SetTag("messaging.job.success", false);
                        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
                        activity.AddException(exception);

                        _metrics?.RecordScheduledJobError(eventData.JobName, exception.GetType().Name);

                        activity.Stop();
                    }
                }
                break;
        }
    }
}
