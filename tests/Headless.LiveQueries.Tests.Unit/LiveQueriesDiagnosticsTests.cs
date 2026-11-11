// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.Metrics;
using Headless.LiveQueries;
using Headless.LiveQueries.Internal;
using Headless.Testing.Tests;
using OpenTelemetry.Metrics;

namespace Tests;

public sealed class LiveQueriesDiagnosticsTests : TestBase
{
    [Fact]
    public void should_create_the_hosts_meter_with_the_framework_name_and_version()
    {
        // given
        using var factory = new RecordingMeterFactory();

        // when
        _ = new LiveQueriesMetrics(factory);

        // then
        var created = factory.Created.Should().ContainSingle().Subject;
        created.Name.Should().Be("Headless.LiveQueries");
        created.Version.Should().NotBeNullOrWhiteSpace("a dashboard tells framework releases apart by it");
    }

    [Fact]
    public void should_create_a_versioned_meter_when_the_host_has_no_meter_factory()
    {
        // given - a process-wide listener, which is all such a host can have
        string? version = null;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, _) =>
        {
            if (
                instrument.Meter.Scope is null
                && string.Equals(instrument.Meter.Name, "Headless.LiveQueries", StringComparison.Ordinal)
            )
            {
                Volatile.Write(ref version, instrument.Meter.Version ?? "");
            }
        };

        // when
        _ = new LiveQueriesMetrics();
        listener.Start();

        // then
        Volatile.Read(ref version).Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void should_publish_the_meter_name_as_a_constant()
    {
        LiveQueriesDiagnostics.SourceName.Should().Be("Headless.LiveQueries");
    }

    [Fact]
    public void should_subscribe_the_meter_provider_to_the_live_queries_meter()
    {
        // given
        var builder = new RecordingMeterProviderBuilder();

        // when
        var returned = builder.AddLiveQueriesInstrumentation();

        // then
        returned.Should().BeSameAs(builder);
        builder.Meters.Should().Equal(LiveQueriesDiagnostics.SourceName);
    }

    /// <summary>The builder contract alone, so the test needs no OpenTelemetry SDK.</summary>
    private sealed class RecordingMeterProviderBuilder : MeterProviderBuilder
    {
        public List<string> Meters { get; } = [];

        public override MeterProviderBuilder AddInstrumentation<TInstrumentation>(
            Func<TInstrumentation> instrumentationFactory
        )
        {
            return this;
        }

        public override MeterProviderBuilder AddMeter(params string[] names)
        {
            Meters.AddRange(names);

            return this;
        }
    }

    private sealed class RecordingMeterFactory : IMeterFactory
    {
        private readonly List<Meter> _meters = [];

        public List<MeterOptions> Created { get; } = [];

        public Meter Create(MeterOptions options)
        {
            Created.Add(options);
            var meter = new Meter(options);
            _meters.Add(meter);

            return meter;
        }

        public void Dispose()
        {
            foreach (var meter in _meters)
            {
                meter.Dispose();
            }
        }
    }
}
