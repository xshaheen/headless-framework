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

namespace Tests;

/// <summary>
/// Parity + behavior tests for the native <see cref="MessagingTelemetry"/> emitter that replaced the former
/// DiagnosticSource→span bridge satellite package. Uses BCL <see cref="ActivityListener"/> /
/// <see cref="MeterListener"/> so no OpenTelemetry SDK is required.
/// </summary>
public sealed class MessagingTelemetryTests : TestBase
{
    private static readonly BrokerAddress _Broker = new("TestBroker", "broker.local:5672");

    // AE1 (R2 parity): the native emitter produces the same span names + headless.messaging.* attribute keys.
    // Asserts on the started Activity references directly so process-global listener leakage from other test
    // classes running in parallel cannot influence the result.
    [Fact]
    public void should_emit_expected_span_names_and_attribute_keys_when_full_flow()
    {
        using var listener = _StartActivityListener([]);
        var telemetry = MessagingTelemetry.Default;

        // persist
        var persistMessage = _CreateMessage("orders.placed");
        var persist = telemetry.PersistStart(persistMessage, persistMessage.Name, IntentType.Bus, 100);
        persist.Should().NotBeNull();
        persist!.OperationName.Should().Be("message.persist");
        MessagingTelemetry.PersistStop(persist, persistMessage.Name, 100, 150);

        // publish (with a tenant header so the tenant-id enricher tag is emitted)
        var publishMessage = _CreateTransportMessage(
            "orders.placed",
            extraHeaders: new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.TenantId] = "tenant-7" }
        );
        var publish = telemetry.PublishStart(publishMessage, IntentType.Bus, _Broker, 200);
        publish.Should().NotBeNull();
        publish!.OperationName.Should().Be("message.publish");
        _TagKeys(publish)
            .Should()
            .Contain([
                "messaging.system",
                "messaging.message.id",
                "messaging.message.body.size",
                "messaging.message.conversation_id",
                "messaging.destination.name",
                "server.address",
                "server.port",
                MessagingTags.Intent,
                MessagingTags.DestinationKind,
                MessagingTags.TenantId,
            ]);
        publish.GetTagItem(MessagingTags.Intent).Should().Be("bus");
        publish.GetTagItem(MessagingTags.TenantId).Should().Be("tenant-7");
        MessagingTelemetry.PublishStop(publish, publishMessage, _Broker, 200, 260);

        // consume
        var consumeMessage = _CreateTransportMessage("orders.placed");
        var consume = telemetry.ConsumeStart(consumeMessage, IntentType.Queue, _Broker, 300);
        consume.Should().NotBeNull();
        consume!.OperationName.Should().Be("message.consume");
        _TagKeys(consume)
            .Should()
            .Contain(["messaging.operation.type", "messaging.client.id", "messaging.consumer.group.name"]);
        consume.GetTagItem(MessagingTags.Intent).Should().Be("queue");
        MessagingTelemetry.ConsumeStop(consume, consumeMessage, _Broker, 300, 330);

        // subscriber invoke (retryCount>0 so the retry-count enricher tag is emitted)
        var invokeMessage = _CreateMessage("orders.placed");
        var subscriber = telemetry.SubscriberInvokeStart(
            invokeMessage,
            invokeMessage.Name,
            IntentType.Bus,
            _Method,
            retryCount: 3,
            400
        );
        subscriber.Should().NotBeNull();
        subscriber!.OperationName.Should().Be("subscriber.invoke");
        _TagKeys(subscriber).Should().Contain(["code.function.name", MessagingTags.RetryCount]);
        subscriber.GetTagItem(MessagingTags.RetryCount).Should().Be(3);
        MessagingTelemetry.SubscriberInvokeStop(subscriber, invokeMessage.Name, _Method, 400, 480);
    }

    // AE1 (R2 parity): the native emitter records the same semconv instrument names + dimensions.
    [Fact]
    public void should_record_expected_instrument_names_when_full_flow()
    {
        var measurements = new List<(string Name, string[] TagKeys)>();
        using var listener = _StartMeterListener(measurements);
        var telemetry = MessagingTelemetry.Default;

        var publishMessage = _CreateTransportMessage("orders.placed");
        var publish = telemetry.PublishStart(publishMessage, IntentType.Bus, _Broker, 200);
        MessagingTelemetry.PublishStop(publish, publishMessage, _Broker, 200, 260);
        MessagingTelemetry.PublishError(publish, publishMessage, _Broker, new InvalidOperationException("boom"));

        var consumeMessage = _CreateTransportMessage("orders.placed");
        var consume = telemetry.ConsumeStart(consumeMessage, IntentType.Queue, _Broker, 300);
        MessagingTelemetry.ConsumeStop(consume, consumeMessage, _Broker, 300, 330);
        MessagingTelemetry.ConsumeError(consume, consumeMessage, _Broker, new InvalidOperationException("boom"));

        var invokeMessage = _CreateMessage("orders.placed");
        var subscriber = telemetry.SubscriberInvokeStart(
            invokeMessage,
            invokeMessage.Name,
            IntentType.Bus,
            _Method,
            0,
            400
        );
        MessagingTelemetry.SubscriberInvokeStop(subscriber, invokeMessage.Name, _Method, 400, 480);
        MessagingTelemetry.SubscriberInvokeError(
            subscriber,
            invokeMessage.Name,
            _Method,
            new InvalidOperationException("boom")
        );

        var names = measurements.Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
        names
            .Should()
            .Contain([
                "messaging.publish.messages",
                "messaging.publish.duration",
                "messaging.publish.errors",
                "messaging.message.size",
                "messaging.consume.messages",
                "messaging.consume.duration",
                "messaging.consume.errors",
                "messaging.persistence.duration",
                "messaging.subscriber.invocations",
                "messaging.subscriber.duration",
                "messaging.subscriber.errors",
            ]);

        var publishCounter = measurements.First(m =>
            string.Equals(m.Name, "messaging.publish.messages", StringComparison.Ordinal)
        );
        publishCounter.TagKeys.Should().Contain(["messaging.operation", "messaging.system"]);

        var consumeError = measurements.First(m =>
            string.Equals(m.Name, "messaging.consume.errors", StringComparison.Ordinal)
        );
        consumeError
            .TagKeys.Should()
            .Contain(["messaging.operation", "messaging.system", "error.type", "messaging.consumer.group"]);
    }

    // AE2 (R4/R5): publish injects traceparent; consume extracts and continues the same trace.
    [Fact]
    public void should_propagate_trace_context_through_headers_when_publish_then_consume()
    {
        // The app's OpenTelemetry setup normally assigns the W3C propagator; do so for the test since no SDK
        // provider is built here. (The bridge depended on the same Propagators.DefaultTextMapPropagator.)
        Sdk.SetDefaultTextMapPropagator(
            new CompositeTextMapPropagator([new TraceContextPropagator(), new BaggagePropagator()])
        );

        using var listener = _StartActivityListener([]);
        var telemetry = MessagingTelemetry.Default;

        var message = _CreateTransportMessage("orders.placed");
        var publish = telemetry.PublishStart(message, IntentType.Bus, _Broker, 100);
        publish.Should().NotBeNull();

        // The publish span injected a W3C traceparent into the outgoing headers.
        message.Headers.Should().ContainKey("traceparent");
        message.Headers["traceparent"].Should().Contain(publish!.TraceId.ToHexString());

        MessagingTelemetry.PublishStop(publish, message, _Broker, 100, 120);

        // A consumer reading those headers continues the publish trace.
        var consume = telemetry.ConsumeStart(message, IntentType.Bus, _Broker, 200);
        consume.Should().NotBeNull();
        consume!.TraceId.Should().Be(publish.TraceId);
        consume.ParentSpanId.Should().Be(publish.SpanId);
    }

    // AE3 (R7): a custom enricher's tag is present even when the span ends immediately (sync at start).
    [Fact]
    public void should_apply_custom_enricher_tag_synchronously_when_span_starts()
    {
        using var listener = _StartActivityListener([]);
        var telemetry = new MessagingTelemetry([new StubEnricher("app.custom", "value-1")]);

        var message = _CreateTransportMessage("orders.placed");
        var publish = telemetry.PublishStart(message, IntentType.Bus, _Broker, 100);
        // The tag must already be attached before the span ends — a fire-and-forget async enricher would drop it.
        publish.Should().NotBeNull();
        publish!.GetTagItem("app.custom").Should().Be("value-1");
        MessagingTelemetry.PublishStop(publish, message, _Broker, 100, 110);
    }

    // AE3 (R7): a throwing enricher is isolated; the operation and later enrichers are unaffected.
    [Fact]
    public void should_isolate_throwing_enricher_when_span_starts()
    {
        using var listener = _StartActivityListener([]);
        var telemetry = new MessagingTelemetry([new ThrowingEnricher(), new StubEnricher("app.after", "still-here")]);

        var message = _CreateTransportMessage("orders.placed");
        Activity? publish = null;
        var act = () =>
        {
            publish = telemetry.PublishStart(message, IntentType.Bus, _Broker, 100);
            MessagingTelemetry.PublishStop(publish, message, _Broker, 100, 110);
        };

        act.Should().NotThrow();
        publish.Should().NotBeNull();
        publish!.GetTagItem("app.after").Should().Be("still-here");
    }

    // --- Helpers --------------------------------------------------------------------------------------------

    private static readonly MethodInfo _Method = typeof(MessagingTelemetryTests).GetMethod(
        nameof(_SampleHandler),
        BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly,
        binder: null,
        types: Type.EmptyTypes,
        modifiers: null
    )!;

    private static void _SampleHandler() { }

    private static ActivityListener _StartActivityListener(List<Activity> captured)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source =>
                string.Equals(source.Name, MessagingDiagnostics.SourceName, StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = captured.Add,
        };

        ActivitySource.AddActivityListener(listener);

        return listener;
    }

    private static MeterListener _StartMeterListener(List<(string Name, string[] TagKeys)> captured)
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

        listener.SetMeasurementEventCallback<long>(
            (instrument, _, tags, _) => captured.Add((instrument.Name, _Keys(tags)))
        );
        listener.SetMeasurementEventCallback<double>(
            (instrument, _, tags, _) => captured.Add((instrument.Name, _Keys(tags)))
        );

        listener.Start();

        return listener;
    }

    private static string[] _Keys(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var keys = new string[tags.Length];
        for (var i = 0; i < tags.Length; i++)
        {
            keys[i] = tags[i].Key;
        }

        return keys;
    }

    private static string[] _TagKeys(Activity activity)
    {
        return activity.TagObjects.Select(t => t.Key).ToArray();
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

    private static Message _CreateMessage(string name)
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = Guid.NewGuid().ToString(),
            [Headers.MessageName] = name,
            [Headers.Group] = "workers",
        };

        return new Message(headers, value: null);
    }

    private sealed class StubEnricher(string key, string value) : IActivityTagEnricher
    {
        public void Enrich(Activity activity, in MessagingEnrichmentContext context)
        {
            activity.SetTag(key, value);
        }
    }

    private sealed class ThrowingEnricher : IActivityTagEnricher
    {
        public void Enrich(Activity activity, in MessagingEnrichmentContext context)
        {
            throw new InvalidOperationException("enricher boom");
        }
    }
}
