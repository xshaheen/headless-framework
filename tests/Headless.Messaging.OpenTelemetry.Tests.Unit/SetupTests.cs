// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Messaging.OpenTelemetry;
using Headless.Messaging.OpenTelemetry.Internal;
using Headless.Testing.Tests;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using DiagnosticListener = Headless.Messaging.OpenTelemetry.DiagnosticListener;

namespace Tests;

public sealed class SetupTests : TestBase
{
    [Fact]
    public void should_add_messaging_instrumentation_to_tracer_provider()
    {
        // given
        var activities = new List<Activity>();

        // when
        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddMessagingInstrumentation()
            .AddInMemoryExporter(activities)
            .Build();

        // then
        tracerProvider.Should().NotBeNull();
    }

    [Fact]
    public void should_add_messaging_instrumentation_with_metrics_enabled()
    {
        // given
        var activities = new List<Activity>();

        // when
        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddMessagingInstrumentation(o => o.EnableMetrics = true)
            .AddInMemoryExporter(activities)
            .Build();

        // then
        tracerProvider.Should().NotBeNull();
    }

    [Fact]
    public void should_add_messaging_instrumentation_with_metrics_disabled()
    {
        // given
        var activities = new List<Activity>();

        // when
        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddMessagingInstrumentation(o => o.EnableMetrics = false)
            .AddInMemoryExporter(activities)
            .Build();

        // then
        tracerProvider.Should().NotBeNull();
    }

    [Fact]
    public void should_accept_custom_enricher()
    {
        // given - register a custom enricher alongside the built-in defaults
        var customEnricher = Substitute.For<IActivityTagEnricher>();
        var options = new MessagingInstrumentationOptions();
        options.AddEnricher(customEnricher);

        // when - build the enricher composition the same way Setup does at registration time
        var enrichers = options.BuildEnrichers();

        // then - built-in defaults precede the custom enricher, in declared order
        enrichers.Should().HaveCount(4);
        enrichers[0].Should().BeOfType<TenantIdTagEnricher>();
        enrichers[1].Should().BeOfType<IntentTagEnricher>();
        enrichers[2].Should().BeOfType<RetryCountTagEnricher>();
        enrichers[3].Should().BeSameAs(customEnricher);

        // and TracerProvider build still succeeds end-to-end
        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddMessagingInstrumentation(o => o.AddEnricher(customEnricher))
            .Build();
        tracerProvider.Should().NotBeNull();
    }

    [Fact]
    public void should_suppress_tenant_id_tag_when_configured()
    {
        // given
        var options = new MessagingInstrumentationOptions { SuppressTenantIdTag = true };

        // when - build the enricher composition that Setup will register
        var enrichers = options.BuildEnrichers();

        // then - no TenantIdTagEnricher is composed when suppression is requested
        enrichers.Should().NotContain(e => e is TenantIdTagEnricher);
        // and the other built-ins remain
        enrichers.Should().ContainSingle(e => e is IntentTagEnricher);
        enrichers.Should().ContainSingle(e => e is RetryCountTagEnricher);

        // and TracerProvider build still succeeds end-to-end
        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddMessagingInstrumentation(o => o.SuppressTenantIdTag = true)
            .Build();
        tracerProvider.Should().NotBeNull();
    }

    [Fact]
    public void should_suppress_retry_count_tag_when_configured()
    {
        // given
        var options = new MessagingInstrumentationOptions { SuppressRetryCountTag = true };

        // when
        var enrichers = options.BuildEnrichers();

        // then - no RetryCountTagEnricher is composed when suppression is requested
        enrichers.Should().NotContain(e => e is RetryCountTagEnricher);
        // and the other built-ins remain
        enrichers.Should().ContainSingle(e => e is TenantIdTagEnricher);
        enrichers.Should().ContainSingle(e => e is IntentTagEnricher);

        // and TracerProvider build still succeeds end-to-end
        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddMessagingInstrumentation(o => o.SuppressRetryCountTag = true)
            .Build();
        tracerProvider.Should().NotBeNull();
    }

    [Fact]
    public void should_include_built_in_enrichers_by_default()
    {
        // given
        var options = new MessagingInstrumentationOptions();

        // when
        var enrichers = options.BuildEnrichers();

        // then - built-ins are included by default, in declared order
        enrichers.Should().HaveCount(3);
        enrichers[0].Should().BeOfType<TenantIdTagEnricher>();
        enrichers[1].Should().BeOfType<IntentTagEnricher>();
        enrichers[2].Should().BeOfType<RetryCountTagEnricher>();
    }

    [Fact]
    public void should_suppress_intent_tags_when_configured()
    {
        // given
        var options = new MessagingInstrumentationOptions { SuppressIntentTags = true };

        // when
        var enrichers = options.BuildEnrichers();

        // then
        enrichers.Should().NotContain(e => e is IntentTagEnricher);
        enrichers.Should().ContainSingle(e => e is TenantIdTagEnricher);
        enrichers.Should().ContainSingle(e => e is RetryCountTagEnricher);

        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddMessagingInstrumentation(o => o.SuppressIntentTags = true)
            .Build();
        tracerProvider.Should().NotBeNull();
    }

    [Fact]
    public void should_throw_when_builder_is_null_for_tracing()
    {
        // given
        TracerProviderBuilder builder = null!;

        // when
        var act = () => builder.AddMessagingInstrumentation();

        // then
        act.Should().Throw<ArgumentNullException>().WithParameterName("builder");
    }

    [Fact]
    public void should_add_messaging_meter_to_meter_provider()
    {
        // given
        var metrics = new List<Metric>();

        // when
        using var meterProvider = Sdk.CreateMeterProviderBuilder()
            .AddMessagingInstrumentation()
            .AddInMemoryExporter(metrics)
            .Build();

        // then
        meterProvider.Should().NotBeNull();
    }

    [Fact]
    public void should_throw_when_builder_is_null_for_metrics()
    {
        // given
        MeterProviderBuilder builder = null!;

        // when
        var act = builder.AddMessagingInstrumentation;

        // then
        act.Should().Throw<ArgumentNullException>().WithParameterName("builder");
    }

    [Fact]
    public void should_create_activities_when_instrumentation_is_registered()
    {
        // given
        var activities = new List<Activity>();
        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddSource(DiagnosticListener.SourceName)
            .AddMessagingInstrumentation()
            .AddInMemoryExporter(activities)
            .Build();

        // when - directly create activity through the ActivitySource
        using var activitySource = new ActivitySource(DiagnosticListener.SourceName);
        // ReSharper disable once ExplicitCallerInfoArgument
        using var activity = activitySource.StartActivity("test-activity");
        activity?.Stop();
        tracerProvider.ForceFlush();

        // then
        activities.Should().ContainSingle();
        activities[0].DisplayName.Should().Be("test-activity");
    }

    [Fact]
    public void should_chain_tracer_provider_builder()
    {
        // given
        var activities = new List<Activity>();

        // when
        var result = Sdk.CreateTracerProviderBuilder().AddMessagingInstrumentation();

        // then
        result.Should().NotBeNull();
        result.Should().BeAssignableTo<TracerProviderBuilder>();
    }

    [Fact]
    public void should_chain_meter_provider_builder()
    {
        // given

        // when
        var result = Sdk.CreateMeterProviderBuilder().AddMessagingInstrumentation();

        // then
        result.Should().NotBeNull();
        result.Should().BeAssignableTo<MeterProviderBuilder>();
    }
}
