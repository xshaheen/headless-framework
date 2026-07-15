// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Testing.Tests;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Tests.Diagnostics;

/// <summary>
/// Tests for the messaging instrumentation registration surface: built-in enrichers, the
/// <see cref="MessagingInstrumentationOptions"/> composition/suppression toggles, and the typed
/// <c>AddMessagingInstrumentation()</c> helpers on the OpenTelemetry provider builders (AE4).
/// </summary>
public sealed class MessagingInstrumentationTests : TestBase
{
    private static readonly BrokerAddress _Broker = new("TestBroker", "broker.local:5672");

    // --- Built-in enrichers ---------------------------------------------------------------------------------

    [Fact]
    public void should_tag_bus_intent_when_intent_enricher()
    {
        using var activity = new Activity("test");

        new IntentTagEnricher().Enrich(activity, new MessagingEnrichmentContext { IntentType = IntentType.Bus });

        activity.GetTagItem(MessagingTags.Intent).Should().Be("bus");
        activity.GetTagItem(MessagingTags.DestinationKind).Should().Be("topic");
    }

    [Fact]
    public void should_tag_queue_intent_when_intent_enricher()
    {
        using var activity = new Activity("test");

        new IntentTagEnricher().Enrich(activity, new MessagingEnrichmentContext { IntentType = IntentType.Queue });

        activity.GetTagItem(MessagingTags.Intent).Should().Be("queue");
        activity.GetTagItem(MessagingTags.DestinationKind).Should().Be("queue");
    }

    [Fact]
    public void should_tag_tenant_when_present_and_skip_when_absent()
    {
        using var withTenant = new Activity("with");
        new TenantIdTagEnricher().Enrich(withTenant, new MessagingEnrichmentContext { TenantId = "tenant-9" });
        withTenant.GetTagItem(MessagingTags.TenantId).Should().Be("tenant-9");

        using var withoutTenant = new Activity("without");
        new TenantIdTagEnricher().Enrich(withoutTenant, new MessagingEnrichmentContext { TenantId = null });
        withoutTenant.GetTagItem(MessagingTags.TenantId).Should().BeNull();
    }

    [Fact]
    public void should_tag_retry_count_when_positive_and_skip_when_zero()
    {
        using var withRetry = new Activity("with");
        new RetryCountTagEnricher().Enrich(withRetry, new MessagingEnrichmentContext { RetryCount = 4 });
        withRetry.GetTagItem(MessagingTags.RetryCount).Should().Be(4);

        using var noRetry = new Activity("without");
        new RetryCountTagEnricher().Enrich(noRetry, new MessagingEnrichmentContext { RetryCount = 0 });
        noRetry.GetTagItem(MessagingTags.RetryCount).Should().BeNull();
    }

    // --- Composition / suppression --------------------------------------------------------------------------

    [Fact]
    public void should_build_all_builtin_enrichers_in_order_when_default()
    {
        var options = new MessagingInstrumentationOptions();

        options
            .BuildEnrichers()
            .Select(e => e.GetType())
            .Should()
            .Equal(typeof(TenantIdTagEnricher), typeof(IntentTagEnricher), typeof(RetryCountTagEnricher));
    }

    [Fact]
    public void should_omit_suppressed_builtins_when_toggled()
    {
        var options = new MessagingInstrumentationOptions
        {
            SuppressTenantIdTag = true,
            SuppressIntentTags = true,
            SuppressRetryCountTag = true,
        };

        options.BuildEnrichers().Should().BeEmpty();
    }

    [Fact]
    public void should_append_custom_enricher_after_builtins()
    {
        var custom = new StubEnricher();
        var options = new MessagingInstrumentationOptions { SuppressTenantIdTag = true, SuppressRetryCountTag = true };
        options.AddEnricher(custom);

        options
            .BuildEnrichers()
            .Select(e => e.GetType())
            .Should()
            .Equal(typeof(IntentTagEnricher), typeof(StubEnricher));
        options.BuildEnrichers()[^1].Should().BeSameAs(custom);
    }

    // --- AE4: typed registration helpers --------------------------------------------------------------------

    [Fact]
    public void should_expose_headless_messaging_source_name()
    {
        MessagingDiagnostics.SourceName.Should().Be("Headless.Messaging");
    }

    [Fact]
    public void should_export_span_when_tracer_provider_adds_messaging_instrumentation()
    {
        var spans = new List<Activity>();
        using var tracer = Sdk.CreateTracerProviderBuilder()
            .AddMessagingInstrumentation()
            .AddInMemoryExporter(spans)
            .Build();

        var message = _CreateTransportMessage("orders.placed");
        var publish = MessagingTelemetry.Default.PublishStart(message, IntentType.Bus, _Broker, 100);
        MessagingTelemetry.PublishStop(publish, message, _Broker, 100, 120);

        tracer!.ForceFlush();

        spans.Select(s => s.OperationName).Should().Contain("message.publish");
    }

    [Fact]
    public void should_export_metric_when_meter_provider_adds_messaging_instrumentation()
    {
        var metrics = new List<Metric>();
        using var meter = Sdk.CreateMeterProviderBuilder()
            .AddMessagingInstrumentation()
            .AddInMemoryExporter(metrics)
            .Build();

        var message = _CreateTransportMessage("orders.placed");
        var publish = MessagingTelemetry.Default.PublishStart(message, IntentType.Bus, _Broker, 100);
        MessagingTelemetry.PublishStop(publish, message, _Broker, 100, 120);

        meter!.ForceFlush();

        metrics.Select(m => m.Name).Should().Contain("messaging.publish.messages");
    }

    // --- Helpers --------------------------------------------------------------------------------------------

    private static TransportMessage _CreateTransportMessage(string name)
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = Guid.NewGuid().ToString(),
            [Headers.MessageName] = name,
            [Headers.Group] = "workers",
        };

        return new TransportMessage(headers, new byte[] { 1, 2, 3 });
    }

    private sealed class StubEnricher : IActivityTagEnricher
    {
        public void Enrich(Activity activity, in MessagingEnrichmentContext context) { }
    }
}
