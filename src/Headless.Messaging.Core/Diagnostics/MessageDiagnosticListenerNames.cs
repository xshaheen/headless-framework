// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Diagnostics;

/// <summary>
/// Names for the messaging <see cref="System.Diagnostics.Tracing.EventSource"/> real-time rate counters
/// (<see cref="MessageEventCounterSource"/>). Span/metric OpenTelemetry emission is native and does not use
/// <c>DiagnosticSource</c> — see <see cref="Headless.Messaging.MessagingDiagnostics"/>.
/// </summary>
public static class MessageDiagnosticListenerNames
{
    private const string _Prefix = "Headless.Messages.";

    // Real-time EventCounter metrics (consumed by the messaging dashboard's EventListener).
    public const string MetricListenerName = _Prefix + "EventCounter";
    public const string PublishedPerSec = "published-per-second";
    public const string ConsumePerSec = "consume-per-second";
    public const string InvokeSubscriberPerSec = "invoke-subscriber-per-second";
    public const string InvokeSubscriberElapsedMs = "invoke-subscriber-elapsed-ms";
    public const string PubSubDispatchFailures = "redis-pubsub-dispatch-failures";
}
