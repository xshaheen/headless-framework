// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace Tests.Diagnostics;

/// <summary>
/// Parity + behavior tests for the native <see cref="MessagingTelemetry"/> emitter that replaced the former
/// DiagnosticSource→span bridge satellite package. Uses BCL <see cref="ActivityListener"/> /
/// <see cref="MeterListener"/> so no OpenTelemetry SDK is required.
/// </summary>
public sealed class MessagingTelemetryTests : TestBase
{
    private static readonly BrokerAddress _Broker = new("TestBroker", "broker.local:5672");

    // Parity: the native emitter produces the same span names + headless.messaging.* attribute keys.
    // Asserts on the started Activity references directly so process-global listener leakage from other test
    // classes running in parallel cannot influence the result.
    [Fact]
    public void should_emit_expected_span_names_and_attribute_keys_when_full_flow()
    {
        using var listener = _StartActivityListener([]);
        var telemetry = MessagingTelemetry.Default;

        // persist
        var persistMessage = _CreateMessage("orders.placed");
        persistMessage.Headers[Headers.RequestedDeliveryMode] = nameof(DeliveryMode.Durable);
        persistMessage.Headers[Headers.ResolvedDeliveryMode] = nameof(DeliveryMode.Durable);
        var persist = telemetry.PersistStart(persistMessage, persistMessage.Name, MessageLane.Bus, 100);
        persist.Should().NotBeNull();
        persist!.OperationName.Should().Be("message.persist");
        persist.GetTagItem(MessagingTags.RequestedDeliveryMode).Should().Be("durable");
        persist.GetTagItem(MessagingTags.ResolvedDeliveryMode).Should().Be("durable");
        MessagingTelemetry.PersistStop(persist, persistMessage.Name, 100, 150);

        // publish (with a tenant header so the tenant attribute is emitted)
        var publishMessage = _CreateTransportMessage(
            "orders.placed",
            extraHeaders: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.TenantId] = "tenant-7",
                [Headers.RequestedDeliveryMode] = nameof(DeliveryMode.Durable),
                [Headers.ResolvedDeliveryMode] = nameof(DeliveryMode.Direct),
            }
        );
        var publish = telemetry.PublishStart(publishMessage, MessageLane.Bus, _Broker, 200);
        publish.Should().NotBeNull();
        publish!.OperationName.Should().Be("message.publish");
        publish.DisplayName.Should().Be("publish orders.placed");
        publish.Kind.Should().Be(ActivityKind.Producer);
        publish.GetTagItem("messaging.operation.name").Should().Be("publish");
        publish.GetTagItem("messaging.operation.type").Should().Be("send");
        publish.GetTagItem("messaging.system").Should().Be("TestBroker");
        publish.GetTagItem("server.address").Should().Be("broker.local");
        publish.GetTagItem("server.port").Should().Be(5672);
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
                MessagingTags.Lane,
                MessagingTags.DestinationKind,
                TenantTelemetryOptions.DefaultAttributeName,
                MessagingTags.RequestedDeliveryMode,
                MessagingTags.ResolvedDeliveryMode,
            ]);
        publish.GetTagItem(MessagingTags.Lane).Should().Be("bus");
        publish.GetTagItem(TenantTelemetryOptions.DefaultAttributeName).Should().Be("tenant-7");
        publish.GetTagItem(MessagingTags.RequestedDeliveryMode).Should().Be("durable");
        publish.GetTagItem(MessagingTags.ResolvedDeliveryMode).Should().Be("direct");
        MessagingTelemetry.PublishStop(publish, publishMessage, _Broker, 200, 260);

        // consume
        var consumeMessage = _CreateTransportMessage("orders.placed");
        var consume = telemetry.ConsumeStart(consumeMessage, MessageLane.Queue, _Broker, 300);
        consume.Should().NotBeNull();
        consume!.OperationName.Should().Be("message.consume");
        consume.DisplayName.Should().Be("receive orders.placed");
        consume.Kind.Should().Be(ActivityKind.Consumer);
        consume.GetTagItem("messaging.operation.name").Should().Be("receive");
        consume.GetTagItem("messaging.operation.type").Should().Be("receive");
        consume.GetTagItem("messaging.system").Should().Be("TestBroker");
        consume.GetTagItem("messaging.destination.name").Should().Be("orders.placed");
        consume.GetTagItem("messaging.consumer.group.name").Should().Be("workers");
        _TagKeys(consume).Should().Contain(["messaging.client.id", "server.address", "server.port"]);
        consume.GetTagItem(MessagingTags.Lane).Should().Be("queue");
        MessagingTelemetry.ConsumeStop(consume, consumeMessage, _Broker, 300, 330);

        // subscriber invoke (retryCount>0 so the retry-count enricher tag is emitted)
        var invokeMessage = _CreateMessage("orders.placed");
        var subscriber = telemetry.SubscriberInvokeStart(
            invokeMessage,
            invokeMessage.Name,
            MessageLane.Bus,
            _Method,
            retryCount: 3,
            400,
            messagingSystem: "TestBroker"
        );
        subscriber.Should().NotBeNull();
        subscriber!.OperationName.Should().Be("subscriber.invoke");
        subscriber.DisplayName.Should().Be("process orders.placed");
        subscriber.Kind.Should().Be(ActivityKind.Consumer);
        subscriber.GetTagItem("messaging.operation.name").Should().Be("process");
        subscriber.GetTagItem("messaging.operation.type").Should().Be("process");
        subscriber.GetTagItem("messaging.system").Should().Be("TestBroker");
        subscriber.GetTagItem("messaging.destination.name").Should().Be("orders.placed");
        subscriber.GetTagItem("messaging.consumer.group.name").Should().Be("workers");
        _TagKeys(subscriber).Should().Contain(["code.function.name", MessagingTags.RetryCount]);
        subscriber.GetTagItem(MessagingTags.RetryCount).Should().Be(3);
        MessagingTelemetry.SubscriberInvokeStop(subscriber, invokeMessage, _Method, "TestBroker", 400, 480);
    }

    // Each phase records the semantic-convention instrument with the convention attributes; a failure lands on the
    // same instrument with error.type instead of a separate error counter.
    [Fact]
    public void should_record_semantic_convention_instruments_when_full_flow()
    {
        var measurements = new ConcurrentBag<(string Name, object Value, KeyValuePair<string, object?>[] Tags)>();
        using var listener = _StartMeterListener(measurements);
        var telemetry = MessagingTelemetry.Default;
        var broker = new BrokerAddress(Guid.NewGuid().ToString("N"), "broker.local:5672");

        var publishMessage = _CreateTransportMessage(
            "orders.placed",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.RequestedDeliveryMode] = nameof(DeliveryMode.Direct),
                [Headers.ResolvedDeliveryMode] = nameof(DeliveryMode.Direct),
            }
        );
        var publish = telemetry.PublishStart(publishMessage, MessageLane.Bus, broker, 200);
        MessagingTelemetry.PublishStop(publish, publishMessage, broker, 200, 260);
        MessagingTelemetry.PublishError(
            publish,
            publishMessage,
            broker,
            new PublisherSentFailedException("send failed", new TimeoutException("broker down")),
            elapsedMs: 1500
        );

        var consumeMessage = _CreateTransportMessage("orders.placed");
        var consume = telemetry.ConsumeStart(consumeMessage, MessageLane.Queue, broker, 300);
        MessagingTelemetry.ConsumeStop(consume, consumeMessage, broker, 300, 330);
        MessagingTelemetry.ConsumeError(
            consume,
            consumeMessage,
            broker,
            new InvalidOperationException("boom"),
            elapsedMs: 10
        );

        var invokeMessage = _CreateMessage("orders.placed");
        var subscriber = telemetry.SubscriberInvokeStart(
            invokeMessage,
            invokeMessage.Name,
            MessageLane.Bus,
            _Method,
            0,
            400,
            broker.Name
        );
        MessagingTelemetry.SubscriberInvokeStop(subscriber, invokeMessage, _Method, broker.Name, 400, 480);
        MessagingTelemetry.SubscriberInvokeError(
            subscriber,
            invokeMessage,
            _Method,
            broker.Name,
            new SubscriberExecutionFailedException("failed", new FormatException("bad")),
            elapsedMs: 20
        );

        var ours = measurements.Where(m => _HasTag(m.Tags, "messaging.system", broker.Name)).ToArray();

        // Publish: one counted send per attempt, success and failure, with the duration in seconds on both.
        var sent = ours.Where(m => string.Equals(m.Name, "messaging.client.sent.messages", StringComparison.Ordinal))
            .ToArray();
        sent.Should().HaveCount(2);
        sent.Should().OnlyContain(m => _HasPublishTags(m.Tags));
        sent.Should().ContainSingle(m => _HasTag(m.Tags, "error.type", typeof(TimeoutException).FullName));
        var sentTags = sent.Single(m => !_HasTagKey(m.Tags, "error.type")).Tags;
        sentTags.Should().ContainSingle(tag => tag.Key == MessagingTags.Lane).Which.Value.Should().Be("bus");
        sentTags
            .Should()
            .ContainSingle(tag => tag.Key == MessagingTags.RequestedDeliveryMode)
            .Which.Value.Should()
            .Be("direct");
        sentTags
            .Should()
            .ContainSingle(tag => tag.Key == MessagingTags.ResolvedDeliveryMode)
            .Which.Value.Should()
            .Be("direct");

        var sendDurations = ours.Where(m =>
                string.Equals(m.Name, "messaging.client.operation.duration", StringComparison.Ordinal)
                && _HasTag(m.Tags, "messaging.operation.type", "send")
            )
            .ToArray();
        sendDurations.Select(m => (double)m.Value).Should().BeEquivalentTo([0.06, 1.5]);
        sendDurations
            .Should()
            .ContainSingle(m => _HasTag(m.Tags, "error.type", typeof(TimeoutException).FullName))
            .Which.Value.Should()
            .Be(1.5);

        // Receive: each delivery counted once, failed or not, with the consumer group.
        var consumed = ours.Where(m =>
                string.Equals(m.Name, "messaging.client.consumed.messages", StringComparison.Ordinal)
            )
            .ToArray();
        consumed.Should().HaveCount(2);
        consumed
            .Should()
            .OnlyContain(m =>
                _HasTag(m.Tags, "messaging.operation.name", "receive")
                && _HasTag(m.Tags, "messaging.operation.type", "receive")
                && _HasTag(m.Tags, "messaging.destination.name", "orders.placed")
                && _HasTag(m.Tags, "messaging.consumer.group.name", "workers")
                && _HasTag(m.Tags, "server.address", "broker.local")
            );
        consumed.Should().ContainSingle(m => _HasTag(m.Tags, "error.type", typeof(InvalidOperationException).FullName));
        ours.Where(m =>
                string.Equals(m.Name, "messaging.client.operation.duration", StringComparison.Ordinal)
                && _HasTag(m.Tags, "messaging.operation.type", "receive")
            )
            .Select(m => (double)m.Value)
            .Should()
            .BeEquivalentTo([0.03, 0.01]);

        // Process: the histogram's count is the invocation count; the wrapper exception is not the error type.
        var processed = ours.Where(m => string.Equals(m.Name, "messaging.process.duration", StringComparison.Ordinal))
            .ToArray();
        processed.Should().HaveCount(2);
        processed
            .Should()
            .OnlyContain(m =>
                _HasTag(m.Tags, "messaging.operation.name", "process")
                && _HasTag(m.Tags, "messaging.operation.type", "process")
                && _HasTag(m.Tags, "messaging.destination.name", "orders.placed")
                && _HasTag(m.Tags, "messaging.consumer.group.name", "workers")
                && _HasTag(m.Tags, "headless.messaging.subscriber", _Method)
            );
        processed
            .Should()
            .ContainSingle(m => _HasTag(m.Tags, "error.type", typeof(FormatException).FullName))
            .Which.Value.Should()
            .Be(0.02);

        ours.Should()
            .Contain(m =>
                string.Equals(m.Name, "headless.messaging.message.body.size", StringComparison.Ordinal)
                && _HasTag(m.Tags, "messaging.destination.name", "orders.placed")
                && Equals(m.Value, 4L)
            );

        // No retired instrument or attribute name survives.
        measurements
            .Select(m => m.Name)
            .Should()
            .NotContain(name =>
                name.StartsWith("messaging.publish.", StringComparison.Ordinal)
                || name.StartsWith("messaging.consume.", StringComparison.Ordinal)
                || name.StartsWith("messaging.subscriber.", StringComparison.Ordinal)
            );
        ours.SelectMany(m => m.Tags).Select(tag => tag.Key).Should().NotContain("messaging.operation");
    }

    [Fact]
    public void should_declare_semantic_convention_units_and_bucket_advice()
    {
        // Touch the type so its instruments exist before the listener asks for them.
        _ = MessagingMetrics.AnyEnabled;
        var instruments = new ConcurrentDictionary<string, Instrument>(StringComparer.Ordinal);
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, _) =>
            {
                if (string.Equals(instrument.Meter.Name, MessagingDiagnostics.SourceName, StringComparison.Ordinal))
                {
                    instruments[instrument.Name] = instrument;
                }
            },
        };
        listener.Start();

        instruments["messaging.client.sent.messages"].Unit.Should().Be("{message}");
        instruments["messaging.client.consumed.messages"].Unit.Should().Be("{message}");

        double[] advisedBoundaries = [0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 7.5, 10];
        foreach (
            var name in new[]
            {
                "messaging.client.operation.duration",
                "messaging.process.duration",
                "headless.messaging.persistence.duration",
                "headless.messaging.request_reply.duration",
            }
        )
        {
            var histogram = instruments[name].Should().BeOfType<Histogram<double>>().Subject;
            histogram.Unit.Should().Be("s", name);
            histogram.Advice!.HistogramBucketBoundaries.Should().Equal(advisedBoundaries, name);
        }

        instruments["headless.messaging.message.body.size"].Unit.Should().Be("By");

        // Only the convention instruments use the messaging.* namespace; the framework's own are headless.messaging.*.
        string[] conventionNames =
        [
            "messaging.client.sent.messages",
            "messaging.client.consumed.messages",
            "messaging.client.operation.duration",
            "messaging.process.duration",
        ];
        instruments
            .Keys.Where(name => !conventionNames.Contains(name, StringComparer.Ordinal))
            .Should()
            .OnlyContain(name => name.StartsWith("headless.messaging.", StringComparison.Ordinal));
    }

    [Fact]
    public void should_tag_failed_spans_with_error_type()
    {
        using var listener = _StartActivityListener([]);
        var telemetry = MessagingTelemetry.Default;

        var publishMessage = _CreateTransportMessage("orders.placed");
        var publish = telemetry.PublishStart(publishMessage, MessageLane.Bus, _Broker, 100);
        MessagingTelemetry.PublishError(
            publish,
            publishMessage,
            _Broker,
            new PublisherSentFailedException("send failed", new TimeoutException("broker down")),
            elapsedMs: 5
        );

        var ambiguousMessage = _CreateTransportMessage("orders.placed");
        var ambiguous = telemetry.PublishStart(ambiguousMessage, MessageLane.Bus, _Broker, 100);
        MessagingTelemetry.PublishAmbiguous(
            ambiguous,
            ambiguousMessage,
            _Broker,
            new OperationCanceledException(),
            elapsedMs: 5
        );

        var consumeMessage = _CreateTransportMessage("orders.placed");
        var consume = telemetry.ConsumeStart(consumeMessage, MessageLane.Bus, _Broker, 100);
        MessagingTelemetry.ConsumeError(
            consume,
            consumeMessage,
            _Broker,
            new InvalidOperationException("boom"),
            elapsedMs: 5
        );

        var invokeMessage = _CreateMessage("orders.placed");
        var subscriber = telemetry.SubscriberInvokeStart(
            invokeMessage,
            invokeMessage.Name,
            MessageLane.Bus,
            _Method,
            0,
            100
        );
        MessagingTelemetry.SubscriberInvokeError(
            subscriber,
            invokeMessage,
            _Method,
            messagingSystem: null,
            new SubscriberExecutionFailedException("failed", new FormatException("bad")),
            elapsedMs: 5
        );

        publish!.GetTagItem("error.type").Should().Be(typeof(TimeoutException).FullName);
        ambiguous!.GetTagItem("error.type").Should().Be("ambiguous_delivery");
        consume!.GetTagItem("error.type").Should().Be(typeof(InvalidOperationException).FullName);
        subscriber!.GetTagItem("error.type").Should().Be(typeof(FormatException).FullName);
        subscriber.GetTagItem("messaging.system").Should().BeNull();
    }

    [Theory]
    [InlineData("broker.local:5672", "broker.local", 5672)]
    [InlineData("broker.local", "broker.local", null)]
    [InlineData("kafka-1:9092,kafka-2:9092", "kafka-1", 9092)]
    [InlineData("nats://nats-1:4222,nats://nats-2:4222", "nats-1", 4222)]
    [InlineData("https://sqs.eu-west-1.amazonaws.com/123456789012/orders", "sqs.eu-west-1.amazonaws.com", null)]
    [InlineData("amqp://rabbit.local:5673/vhost", "rabbit.local", 5673)]
    [InlineData("", null, null)]
    public void should_derive_server_address_and_port_from_broker_endpoint(
        string endpoint,
        string? expectedAddress,
        int? expectedPort
    )
    {
        var server = MessagingServerEndpoint.From(new BrokerAddress("kafka", endpoint));

        server.Address.Should().Be(expectedAddress);
        server.Port.Should().Be(expectedPort);
    }

    [Fact]
    public void should_emit_bounded_inbox_metrics_without_sensitive_or_unbounded_dimensions()
    {
        var consumerIdentity = $"orders.consumer.{Guid.NewGuid():N}";
        var measurements = new ConcurrentBag<(string Name, KeyValuePair<string, object?>[] Tags)>();
        using var listener = _StartInboxMeterListener(measurements, consumerIdentity);

        foreach (var kind in Enum.GetValues<InboxMetricKind>())
        {
            MessagingMetrics.RecordInbox(
                kind,
                consumerIdentity,
                MessageLane.Queue,
                InboxMetricOutcome.Winner,
                InboxGuarantee.Transactional,
                "PostgreSql",
                tenantId: "tenant-unbounded",
                tenantTagName: null
            );
        }

        var inboxMeasurements = measurements
            .Where(measurement => measurement.Name.StartsWith("headless.messaging.inbox.", StringComparison.Ordinal))
            .ToArray();
        inboxMeasurements
            .Select(measurement => measurement.Name)
            .Should()
            .BeEquivalentTo([
                "headless.messaging.inbox.duplicates",
                "headless.messaging.inbox.attempts",
                "headless.messaging.inbox.recoveries",
                "headless.messaging.inbox.terminal",
                "headless.messaging.inbox.replays",
                "headless.messaging.inbox.retention",
                "headless.messaging.inbox.capabilities",
            ]);
        inboxMeasurements.Should().OnlyContain(measurement => _HasExpectedInboxTags(measurement.Tags));
    }

    [Fact]
    public void should_add_tenant_metric_dimension_only_when_explicitly_enabled()
    {
        var consumerIdentity = $"orders.consumer.{Guid.NewGuid():N}";
        var measurements = new ConcurrentBag<(string Name, KeyValuePair<string, object?>[] Tags)>();
        using var listener = _StartInboxMeterListener(measurements, consumerIdentity);

        MessagingMetrics.RecordInbox(
            InboxMetricKind.Duplicate,
            consumerIdentity,
            MessageLane.Bus,
            InboxMetricOutcome.SucceededDuplicate,
            InboxGuarantee.Durable,
            "PostgreSql",
            tenantId: "tenant-7",
            tenantTagName: TenantTelemetryOptions.DefaultAttributeName
        );

        measurements
            .Should()
            .Contain(measurement =>
                measurement.Name == "headless.messaging.inbox.duplicates"
                && measurement.Tags.Any(tag =>
                    string.Equals(tag.Key, TenantTelemetryOptions.DefaultAttributeName, StringComparison.Ordinal)
                )
            );
    }

    // Publish injects traceparent; consume extracts and continues the same trace.
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

        try
        {
            // Ambient baggage present at publish time must survive the header round-trip.
            Baggage.Current = Baggage.Create(
                new Dictionary<string, string>(StringComparer.Ordinal) { ["tenant"] = "t-42" }
            );

            var message = _CreateTransportMessage("orders.placed");
            var publish = telemetry.PublishStart(message, MessageLane.Bus, _Broker, 100);
            publish.Should().NotBeNull();

            // The publish span injected a W3C traceparent into the outgoing headers.
            message.Headers.Should().ContainKey("traceparent");
            message.Headers["traceparent"].Should().Contain(publish!.TraceId.ToHexString());
            message.Headers.Should().ContainKey("baggage");

            MessagingTelemetry.PublishStop(publish, message, _Broker, 100, 120);

            // Simulate the consume side arriving with no ambient baggage of its own.
            Baggage.Current = default;

            // A consumer reading those headers continues the publish trace with baggage intact.
            var consume = telemetry.ConsumeStart(message, MessageLane.Bus, _Broker, 200);
            consume.Should().NotBeNull();
            consume!.TraceId.Should().Be(publish.TraceId);
            consume.ParentSpanId.Should().Be(publish.SpanId);
            Baggage.Current.GetBaggage("tenant").Should().Be("t-42");
        }
        finally
        {
            // AsyncLocal hygiene: never leak baggage into parallel tests.
            Baggage.Current = default;
        }
    }

    // A custom enricher's tag is present even when the span ends immediately (sync at start).
    [Fact]
    public void should_apply_custom_enricher_tag_synchronously_when_span_starts()
    {
        using var listener = _StartActivityListener([]);
        var telemetry = new MessagingTelemetry([new StubEnricher("app.custom", "value-1")]);

        var message = _CreateTransportMessage("orders.placed");
        var publish = telemetry.PublishStart(message, MessageLane.Bus, _Broker, 100);
        // The tag must already be attached before the span ends — a fire-and-forget async enricher would drop it.
        publish.Should().NotBeNull();
        publish!.GetTagItem("app.custom").Should().Be("value-1");
        MessagingTelemetry.PublishStop(publish, message, _Broker, 100, 110);
    }

    [Fact]
    public void should_tag_tenant_attribute_with_configured_name_before_enrichers_run()
    {
        using var listener = _StartActivityListener([]);
        var observedByEnricher = new List<object?>();
        var telemetry = new MessagingTelemetry(
            [new ObservingEnricher("app.tenant", observedByEnricher)],
            tenantTelemetry: new TenantTelemetryOptions { AttributeName = "app.tenant" }
        );
        var message = _CreateTransportMessage(
            "orders.placed",
            extraHeaders: new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.TenantId] = "tenant-7" }
        );

        var publish = telemetry.PublishStart(message, MessageLane.Bus, _Broker, 100);

        publish.Should().NotBeNull();
        publish!.GetTagItem("app.tenant").Should().Be("tenant-7");
        publish.GetTagItem(TenantTelemetryOptions.DefaultAttributeName).Should().BeNull();
        observedByEnricher.Should().Equal("tenant-7");
        MessagingTelemetry.PublishStop(publish, message, _Broker, 100, 110);
    }

    [Fact]
    public void should_not_tag_tenant_attribute_when_trace_enrichment_is_off_or_message_has_no_tenant()
    {
        using var listener = _StartActivityListener([]);
        var disabled = new MessagingTelemetry([], tenantTelemetry: new TenantTelemetryOptions { EnrichTraces = false });
        var tenantMessage = _CreateTransportMessage(
            "orders.placed",
            extraHeaders: new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.TenantId] = "tenant-7" }
        );
        var noTenantMessage = _CreateTransportMessage("orders.placed");

        var disabledPublish = disabled.PublishStart(tenantMessage, MessageLane.Bus, _Broker, 100);
        var noTenantPublish = MessagingTelemetry.Default.PublishStart(noTenantMessage, MessageLane.Bus, _Broker, 100);

        disabledPublish!.GetTagItem(TenantTelemetryOptions.DefaultAttributeName).Should().BeNull();
        noTenantPublish!.GetTagItem(TenantTelemetryOptions.DefaultAttributeName).Should().BeNull();
        MessagingTelemetry.PublishStop(disabledPublish, tenantMessage, _Broker, 100, 110);
        MessagingTelemetry.PublishStop(noTenantPublish, noTenantMessage, _Broker, 100, 110);
    }

    // A throwing enricher is isolated; the operation and later enrichers are unaffected.
    [Fact]
    public void should_isolate_throwing_enricher_when_span_starts()
    {
        using var listener = _StartActivityListener([]);
        var telemetry = new MessagingTelemetry([new ThrowingEnricher(), new StubEnricher("app.after", "still-here")]);

        var message = _CreateTransportMessage("orders.placed");
        Activity? publish = null;
        var act = () =>
        {
            publish = telemetry.PublishStart(message, MessageLane.Bus, _Broker, 100);
            MessagingTelemetry.PublishStop(publish, message, _Broker, 100, 110);
        };

        act.Should().NotThrow();
        publish.Should().NotBeNull();
        publish!.GetTagItem("app.after").Should().Be("still-here");
    }

    // --- Helpers --------------------------------------------------------------------------------------------

    private const string _Method = "HandleAsync";

    // Process-global callback: parallel tests' activities all land here — the collection must be thread-safe.
    private static ActivityListener _StartActivityListener(ConcurrentBag<Activity> captured)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source =>
                string.Equals(source.Name, MessagingDiagnostics.SourceName, StringComparison.Ordinal),
            Sample = static (ref _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = captured.Add,
        };

        ActivitySource.AddActivityListener(listener);

        return listener;
    }

    private static MeterListener _StartMeterListener(
        ConcurrentBag<(string Name, object Value, KeyValuePair<string, object?>[] Tags)> captured
    )
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
            (instrument, value, tags, _) => captured.Add((instrument.Name, value, tags.ToArray()))
        );
        listener.SetMeasurementEventCallback<double>(
            (instrument, value, tags, _) => captured.Add((instrument.Name, value, tags.ToArray()))
        );

        listener.Start();

        return listener;
    }

    private static MeterListener _StartInboxMeterListener(
        ConcurrentBag<(string Name, KeyValuePair<string, object?>[] Tags)> captured,
        string consumerIdentity
    )
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
            (instrument, _, tags, _) =>
            {
                for (var i = 0; i < tags.Length; i++)
                {
                    if (
                        string.Equals(tags[i].Key, MessagingTags.InboxConsumer, StringComparison.Ordinal)
                        && string.Equals(tags[i].Value as string, consumerIdentity, StringComparison.Ordinal)
                    )
                    {
                        captured.Add((instrument.Name, tags.ToArray()));
                        return;
                    }
                }
            }
        );
        listener.Start();
        return listener;
    }

    private static bool _HasExpectedInboxTags(KeyValuePair<string, object?>[] tags)
    {
        var tagKeys = tags.Select(tag => tag.Key).ToArray();
        return tagKeys.Contains(MessagingTags.InboxConsumer, StringComparer.Ordinal)
            && tagKeys.Contains(MessagingTags.Lane, StringComparer.Ordinal)
            && tagKeys.Contains(MessagingTags.InboxOutcome, StringComparer.Ordinal)
            && tagKeys.Contains(MessagingTags.InboxGuarantee, StringComparer.Ordinal)
            && tagKeys.Contains(MessagingTags.InboxProvider, StringComparer.Ordinal)
            && !tagKeys.Contains(TenantTelemetryOptions.DefaultAttributeName, StringComparer.Ordinal)
            && !tagKeys.Any(key =>
                key.Contains("message", StringComparison.OrdinalIgnoreCase)
                || key.Contains("replay", StringComparison.OrdinalIgnoreCase)
                || key.Contains("payload", StringComparison.OrdinalIgnoreCase)
                || key.Contains("header", StringComparison.OrdinalIgnoreCase)
            );
    }

    private static bool _HasPublishTags(KeyValuePair<string, object?>[] tags)
    {
        return _HasTag(tags, "messaging.operation.name", "publish")
            && _HasTag(tags, "messaging.operation.type", "send")
            && _HasTag(tags, "messaging.destination.name", "orders.placed")
            && _HasTag(tags, "server.address", "broker.local")
            && _HasTag(tags, "server.port", 5672);
    }

    private static bool _HasTag(KeyValuePair<string, object?>[] tags, string key, object? value)
    {
        return tags.Any(tag => string.Equals(tag.Key, key, StringComparison.Ordinal) && Equals(tag.Value, value));
    }

    private static bool _HasTagKey(KeyValuePair<string, object?>[] tags, string key)
    {
        return tags.Any(tag => string.Equals(tag.Key, key, StringComparison.Ordinal));
    }

    private static string[] _TagKeys(Activity activity)
    {
        return [.. activity.TagObjects.Select(t => t.Key)];
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
            [Headers.ConsumerIdentity] = "workers",
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
            [Headers.ConsumerIdentity] = "workers",
        };

        return new Message(headers, value: null);
    }

    private sealed class ObservingEnricher(string key, List<object?> observed) : IActivityTagEnricher
    {
        public void Enrich(Activity activity, in MessagingEnrichmentContext context)
        {
            observed.Add(activity.GetTagItem(key));
        }
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
