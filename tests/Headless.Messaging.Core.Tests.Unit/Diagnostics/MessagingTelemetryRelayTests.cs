// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Testing.Tests;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace Tests.Diagnostics;

/// <summary>
/// K4 pass-through relay (issue #696): when messaging telemetry is enabled but no <see cref="Activity"/> is
/// started — a metrics-only configuration, or the sampler dropped the span — the emitter forwards the
/// incoming/ambient parent trace context verbatim into the outgoing/stored message headers instead of dropping
/// it. Fully-unobserved hosts stay zero-cost (the caller gate never invokes the emitter).
/// </summary>
/// <remarks>
/// These tests need <see cref="MessagingDiagnostics"/>'s process-global <see cref="ActivitySource"/> to have no
/// recording listener (so <c>StartActivity</c> returns null) and, for the unobserved case, no listeners at all —
/// both conditions are corrupted by any concurrently-running test class that attaches a messaging
/// <see cref="ActivityListener"/> / <see cref="MeterListener"/>. The collection therefore disables
/// parallelization so it runs in isolation, and the default text-map propagator (also process-global) is set
/// per test.
/// </remarks>
[Collection(MessagingTelemetryRelayCollection.Name)]
public sealed class MessagingTelemetryRelayTests : TestBase
{
    private static readonly BrokerAddress _Broker = new("TestBroker", "broker.local:5672");
    private const string _AmbientSourceName = "Tests.Diagnostics.AmbientParent";
    private static readonly ActivitySource _AmbientSource = new(_AmbientSourceName);

    // (a) Metrics-only publish relays the ambient parent trace verbatim into the outgoing headers, with baggage —
    //     and metrics still fire. Regression guard: before the relay, a metrics-only publish dropped the parent.
    [Fact]
    public void should_relay_ambient_activity_parent_into_headers_when_metrics_only_publish()
    {
        _UseW3CPropagator();
        var measurements = new List<string>();
        using var meterListener = _StartMeterListener(measurements);
        using var ambientListener = _StartAmbientActivityListener();
        var telemetry = MessagingTelemetry.Default;

        try
        {
            Baggage.Current = Baggage.Create(
                new Dictionary<string, string>(StringComparer.Ordinal) { ["tenant"] = "t-1" }
            );

            using var ambient = _AmbientSource.StartActivity("ambient.work");
            ambient.Should().NotBeNull();

            // The outgoing message carries no trace context of its own; the only parent is the ambient activity.
            var message = _CreateTransportMessage("orders.placed");
            message.Headers.Should().NotContainKey("traceparent");

            var publish = telemetry.PublishStart(message, IntentType.Bus, _Broker, 100);

            // Metrics-only: no messaging trace listener, so no span is started — the relay path is taken.
            publish.Should().BeNull();
            message.Headers.Should().ContainKey("traceparent");
            message.Headers["traceparent"].Should().Contain(ambient!.TraceId.ToHexString());
            message.Headers["traceparent"].Should().Contain(ambient.SpanId.ToHexString());
            message.Headers.Should().ContainKey("baggage");
            message.Headers["baggage"].Should().Contain("tenant=t-1");

            // Metrics are unaffected by tracing being off.
            measurements.Should().Contain("messaging.message.size");
        }
        finally
        {
            Baggage.Current = default;
            MessagingAmbientContext.Current = default;
        }
    }

    // (b) The marquee end-to-end-ish path: a metrics-only consume stashes the incoming context, and a later
    //     publish on the same async flow — with no header context and no ambient Activity — relays the consumed
    //     trace + baggage verbatim onto the outgoing message via the AsyncLocal fallback.
    [Fact]
    public void should_relay_consumed_context_to_outgoing_publish_when_metrics_only()
    {
        _UseW3CPropagator();
        using var meterListener = _StartMeterListener([]);
        var telemetry = MessagingTelemetry.Default;

        var traceId = ActivityTraceId.CreateRandom();
        var spanId = ActivitySpanId.CreateRandom();

        try
        {
            MessagingAmbientContext.Current = default;
            Baggage.Current = default;

            // Consume a message carrying an upstream trace + baggage. Metrics-only: no consume span is started,
            // but ConsumeStart still stashes the extracted context for the handler's downstream publish.
            var consumed = _CreateTransportMessage(
                "orders.placed",
                extraHeaders: new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["traceparent"] = _Traceparent(traceId, spanId),
                    ["baggage"] = "tenant=t-42",
                }
            );
            var consume = telemetry.ConsumeStart(consumed, IntentType.Queue, _Broker, 100);
            consume.Should().BeNull();

            // The handler publishes a fresh message with no trace context of its own, and there is no ambient
            // Activity (metrics-only started none) — the consume-scope fallback supplies the parent.
            Activity.Current.Should().BeNull();
            var outgoing = _CreateTransportMessage("orders.shipped");
            outgoing.Headers.Should().NotContainKey("traceparent");

            var publish = telemetry.PublishStart(outgoing, IntentType.Bus, _Broker, 200);

            publish.Should().BeNull();
            outgoing.Headers.Should().ContainKey("traceparent");
            outgoing.Headers["traceparent"].Should().Contain(traceId.ToHexString());
            outgoing.Headers["traceparent"].Should().Contain(spanId.ToHexString());
            outgoing.Headers.Should().ContainKey("baggage");
            outgoing.Headers["baggage"].Should().Contain("tenant=t-42");
        }
        finally
        {
            MessagingAmbientContext.Current = default;
            Baggage.Current = default;
        }
    }

    // (b') Same fallback via the subscriber-invoke site, which is the emission site closest to the real handler
    //      (SubscribeExecutor invokes it, then the handler runs in the same async scope).
    [Fact]
    public void should_relay_subscriber_invoke_context_to_outgoing_publish_when_metrics_only()
    {
        _UseW3CPropagator();
        using var meterListener = _StartMeterListener([]);
        var telemetry = MessagingTelemetry.Default;

        var traceId = ActivityTraceId.CreateRandom();
        var spanId = ActivitySpanId.CreateRandom();

        try
        {
            MessagingAmbientContext.Current = default;
            Baggage.Current = default;

            var invokeMessage = _CreateMessage(
                "orders.placed",
                extraHeaders: new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["traceparent"] = _Traceparent(traceId, spanId),
                }
            );
            var subscriber = telemetry.SubscriberInvokeStart(
                invokeMessage,
                invokeMessage.Name,
                IntentType.Bus,
                _Method,
                retryCount: 0,
                100
            );
            subscriber.Should().BeNull();

            var outgoing = _CreateTransportMessage("orders.shipped");
            var publish = telemetry.PublishStart(outgoing, IntentType.Bus, _Broker, 200);

            publish.Should().BeNull();
            outgoing.Headers.Should().ContainKey("traceparent");
            outgoing.Headers["traceparent"].Should().Contain(traceId.ToHexString());
            outgoing.Headers["traceparent"].Should().Contain(spanId.ToHexString());
        }
        finally
        {
            MessagingAmbientContext.Current = default;
            Baggage.Current = default;
        }
    }

    // (persist relay) A metrics-only persist stamps the stored message headers with the ambient parent so the
    //     durable row carries traceparent. Regression guard: before the relay, metrics-only persist injected
    //     nothing (inject only ran on the activity!=null path).
    [Fact]
    public void should_relay_ambient_parent_into_stored_headers_when_metrics_only_persist()
    {
        _UseW3CPropagator();
        using var meterListener = _StartMeterListener([]);
        using var ambientListener = _StartAmbientActivityListener();
        var telemetry = MessagingTelemetry.Default;

        try
        {
            MessagingAmbientContext.Current = default;
            Baggage.Current = default;

            using var ambient = _AmbientSource.StartActivity("ambient.work");
            ambient.Should().NotBeNull();

            var message = _CreateMessage("orders.placed");
            message.Headers.Should().NotContainKey("traceparent");

            var persist = telemetry.PersistStart(message, message.Name, IntentType.Bus, 100);

            persist.Should().BeNull();
            message.Headers.Should().ContainKey("traceparent");
            message.Headers["traceparent"].Should().Contain(ambient!.TraceId.ToHexString());
            message.Headers["traceparent"].Should().Contain(ambient.SpanId.ToHexString());
        }
        finally
        {
            MessagingAmbientContext.Current = default;
            Baggage.Current = default;
        }
    }

    // (relay never fabricates a root) With no parent at all — no header context, no ambient Activity, no
    //     consume-scope context — a metrics-only publish leaves the headers untouched.
    [Fact]
    public void should_not_inject_when_no_parent_exists_and_metrics_only_publish()
    {
        _UseW3CPropagator();
        using var meterListener = _StartMeterListener([]);
        var telemetry = MessagingTelemetry.Default;

        try
        {
            MessagingAmbientContext.Current = default;
            Baggage.Current = default;
            Activity.Current.Should().BeNull();

            var message = _CreateTransportMessage("orders.placed");
            var publish = telemetry.PublishStart(message, IntentType.Bus, _Broker, 100);

            publish.Should().BeNull();
            message.Headers.Should().NotContainKey("traceparent");
        }
        finally
        {
            MessagingAmbientContext.Current = default;
            Baggage.Current = default;
        }
    }

    // (c) Fully unobserved: with no listeners at all attached to the messaging scope, the caller gate
    //     (MessagingDiagnostics.IsEnabled) is false, so the emission sites are never invoked and headers stay
    //     untouched. Runs in the isolated collection so no foreign listener flips IsEnabled.
    [Fact]
    public void should_report_disabled_when_fully_unobserved()
    {
        MessagingDiagnostics.IsEnabled.Should().BeFalse();
    }

    // (d) Tracing enabled: the relay path is NOT taken. The publish span continues the incoming trace but injects
    //     its OWN (new) span id, exactly as before — proving the relay branch does not hijack the normal path.
    [Fact]
    public void should_inject_publish_span_context_not_relay_when_tracing_enabled()
    {
        _UseW3CPropagator();
        using var listener = _StartMessagingActivityListener();
        var telemetry = MessagingTelemetry.Default;

        var traceId = ActivityTraceId.CreateRandom();
        var spanId = ActivitySpanId.CreateRandom();

        var message = _CreateTransportMessage(
            "orders.placed",
            extraHeaders: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["traceparent"] = _Traceparent(traceId, spanId),
            }
        );

        var publish = telemetry.PublishStart(message, IntentType.Bus, _Broker, 100);

        publish.Should().NotBeNull();
        publish!.TraceId.Should().Be(traceId); // continues the incoming trace
        publish.SpanId.Should().NotBe(spanId); // but is a distinct producer span
        message.Headers["traceparent"].Should().Contain(traceId.ToHexString());
        message.Headers["traceparent"].Should().Contain(publish.SpanId.ToHexString());
        message.Headers["traceparent"].Should().NotContain(spanId.ToHexString());

        MessagingTelemetry.PublishStop(publish, message, _Broker, 100, 120);
    }

    // --- Helpers --------------------------------------------------------------------------------------------

    private static readonly System.Reflection.MethodInfo _Method = typeof(MessagingTelemetryRelayTests).GetMethod(
        nameof(_SampleHandler),
        System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Static
            | System.Reflection.BindingFlags.DeclaredOnly,
        binder: null,
        types: Type.EmptyTypes,
        modifiers: null
    )!;

    private static void _SampleHandler() { }

    private static void _UseW3CPropagator()
    {
        // The app's OpenTelemetry setup normally assigns the W3C propagator; do so for the test since no SDK
        // provider is built here. Process-global, but the collection runs in isolation.
        Sdk.SetDefaultTextMapPropagator(
            new CompositeTextMapPropagator([new TraceContextPropagator(), new BaggagePropagator()])
        );
    }

    private static string _Traceparent(ActivityTraceId traceId, ActivitySpanId spanId)
    {
        return $"00-{traceId.ToHexString()}-{spanId.ToHexString()}-01";
    }

    private static ActivityListener _StartMessagingActivityListener()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source =>
                string.Equals(source.Name, MessagingDiagnostics.SourceName, StringComparison.Ordinal),
            Sample = static (ref _) => ActivitySamplingResult.AllDataAndRecorded,
        };

        ActivitySource.AddActivityListener(listener);

        return listener;
    }

    private static ActivityListener _StartAmbientActivityListener()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, _AmbientSourceName, StringComparison.Ordinal),
            Sample = static (ref _) => ActivitySamplingResult.AllDataAndRecorded,
        };

        ActivitySource.AddActivityListener(listener);

        return listener;
    }

    private static MeterListener _StartMeterListener(List<string> captured)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (string.Equals(instrument.Meter.Name, MessagingDiagnostics.SourceName, StringComparison.Ordinal))
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };

        listener.SetMeasurementEventCallback<long>((instrument, _, _, _) => captured.Add(instrument.Name));
        listener.SetMeasurementEventCallback<double>((instrument, _, _, _) => captured.Add(instrument.Name));
        listener.Start();

        return listener;
    }

    private static TransportMessage _CreateTransportMessage(
        string name,
        IDictionary<string, string?>? extraHeaders = null
    )
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = Guid.NewGuid().ToString(),
            [Headers.MessageName] = name,
            [Headers.Group] = "workers",
            [Headers.CorrelationId] = "corr-1",
            [Headers.ExecutionInstanceId] = "host-1",
        };

        if (extraHeaders is not null)
        {
            foreach (var (key, value) in extraHeaders)
            {
                headers[key] = value;
            }
        }

        return new TransportMessage(headers, new byte[] { 1, 2, 3, 4 });
    }

    private static Message _CreateMessage(string name, IDictionary<string, string?>? extraHeaders = null)
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = Guid.NewGuid().ToString(),
            [Headers.MessageName] = name,
            [Headers.Group] = "workers",
        };

        if (extraHeaders is not null)
        {
            foreach (var (key, value) in extraHeaders)
            {
                headers[key] = value;
            }
        }

        return new Message(headers, value: null);
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class MessagingTelemetryRelayCollection
{
    public const string Name = "MessagingTelemetryRelay";
}
