// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Framework.Testing.Tests;
using Headless.Messaging.OpenTelemetry;
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
            .AddMessagingInstrumentation(enableMetrics: true)
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
            .AddMessagingInstrumentation(enableMetrics: false)
            .AddInMemoryExporter(activities)
            .Build();

        // then
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
        var act = () => builder.AddMessagingInstrumentation();

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
        using var activity = activitySource.StartActivity("test-activity", ActivityKind.Internal);
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
        var result = Sdk.CreateTracerProviderBuilder()
            .AddMessagingInstrumentation();

        // then
        result.Should().NotBeNull();
        result.Should().BeAssignableTo<TracerProviderBuilder>();
    }

    [Fact]
    public void should_chain_meter_provider_builder()
    {
        // given

        // when
        var result = Sdk.CreateMeterProviderBuilder()
            .AddMessagingInstrumentation();

        // then
        result.Should().NotBeNull();
        result.Should().BeAssignableTo<MeterProviderBuilder>();
    }
}
