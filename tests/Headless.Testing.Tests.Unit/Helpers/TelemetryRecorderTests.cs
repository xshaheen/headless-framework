// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Headless.Testing;

namespace Tests.Helpers;

public sealed class TelemetryRecorderTests
{
    [Fact]
    public void should_reject_a_blank_source_name()
    {
        var act = () => new TelemetryRecorder(" ");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_reject_a_blank_instrument_name_in_of()
    {
        using var recorder = new TelemetryRecorder("Headless.Testing.RecorderTests");

        var act = () => recorder.Of(" ");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_record_the_stopped_activities_of_the_named_source_only()
    {
        using var recorder = new TelemetryRecorder("Headless.Testing.RecorderTests");
        using var source = new ActivitySource("Headless.Testing.RecorderTests");
        using var foreign = new ActivitySource("Headless.Testing.RecorderTests.Foreign");

        using (source.StartActivity("mine"))
        {
            using var ignored = foreign.StartActivity("not mine");
        }

        recorder.Activities.Should().ContainSingle().Which.OperationName.Should().Be("mine");
    }

    [Fact]
    public void should_not_subscribe_to_activities_when_spans_are_disabled()
    {
        using var recorder = new TelemetryRecorder("Headless.Testing.RecorderTests", spans: false);
        using var source = new ActivitySource("Headless.Testing.RecorderTests");

        using (source.StartActivity("mine"))
        {
            recorder.Activities.Should().BeEmpty();
        }
    }

    [Fact]
    public void should_record_the_measurements_of_the_named_meter_with_their_tags()
    {
        using var recorder = new TelemetryRecorder("Headless.Testing.RecorderTests");
        using var meter = new Meter("Headless.Testing.RecorderTests");
        var counter = meter.CreateCounter<long>("test.attempts", unit: "{attempt}");

        counter.Add(2, new KeyValuePair<string, object?>("test.outcome", "success"));

        var attempts = recorder.Of("test.attempts").Should().ContainSingle().Which;
        attempts.Unit.Should().Be("{attempt}");
        attempts.Value.Should().Be(2);
        attempts
            .Tags.Should()
            .Equal(new Dictionary<string, object?>(StringComparer.Ordinal) { ["test.outcome"] = "success" });
    }

    [Fact]
    public void should_not_subscribe_to_measurements_when_metrics_are_disabled()
    {
        using var recorder = new TelemetryRecorder("Headless.Testing.RecorderTests", metrics: false);
        using var meter = new Meter("Headless.Testing.RecorderTests");
        var counter = meter.CreateCounter<long>("test.attempts");

        counter.Add(1);

        recorder.Measurements.Should().BeEmpty();
    }
}
