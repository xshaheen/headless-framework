// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Framework.Messages.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace Framework.Messages;

internal class DiagnosticListener : IObserver<KeyValuePair<string, object?>>
{
    public const string SourceName = "Headless.Messaging.OpenTelemetry";

    private const string _OperateNamePrefix = "Headless/";
    private const string _ProducerOperateNameSuffix = "/Publisher";
    private const string _ConsumerOperateNameSuffix = "/Subscriber";
    private static readonly ActivitySource _ActivitySource = new(SourceName, "1.0.0");
    private static readonly TextMapPropagator _Propagator = Propagators.DefaultTextMapPropagator;

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
                                "CAP message persistence start...",
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
                            "CAP message persistence succeeded!",
                            DateTimeOffset.FromUnixTimeMilliseconds(eventData.OperationTimestamp!.Value),
                            new ActivityTagsCollection { new("cap.persistence.duration", eventData.ElapsedTimeMs) }
                        )
                    );

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
                        if (eventData.BrokerAddress.Endpoint is { } endpoint)
                        {
                            var parts = endpoint.Split(':');
                            if (parts.Length > 0)
                            {
                                activity.SetTag("server.address", parts[0]);
                            }
                            if (parts.Length > 1 && int.TryParse(parts[1], out var port))
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
                                new ActivityTagsCollection { new("cap.send.duration", eventData.ElapsedTimeMs) }
                            )
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
                            if (parts.Length > 1 && int.TryParse(parts[1], out var port))
                            {
                                activity.SetTag("server.port", port);
                            }
                        }
                        activity.AddEvent(
                            new ActivityEvent(
                                "CAP message persistence start...",
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
                                "CAP message persistence succeeded!",
                                DateTimeOffset.FromUnixTimeMilliseconds(eventData.OperationTimestamp!.Value),
                                new ActivityTagsCollection { new("cap.receive.duration", eventData.ElapsedTimeMs) }
                            )
                        );

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
                                new ActivityTagsCollection { new("cap.invoke.duration", eventData.ElapsedTimeMs) }
                            )
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
                        activity.Stop();
                    }
                }
                break;
        }
    }
}
