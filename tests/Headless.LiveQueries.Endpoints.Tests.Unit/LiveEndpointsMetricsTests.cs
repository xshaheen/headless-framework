// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.Metrics;
using Headless.LiveQueries;
using Headless.LiveQueries.Internal;
using Headless.Testing.Tests;

namespace Tests;

public sealed class LiveEndpointsMetricsTests : TestBase
{
    [Fact]
    public void should_add_its_instruments_to_the_versioned_live_queries_meter()
    {
        // given
        using var factory = new RecordingMeterFactory();

        // when
        _ = new LiveEndpointsMetrics(factory);

        // then - the same meter the hub records on, so one subscription hears both
        var created = factory.Created.Should().ContainSingle().Subject;
        created.Name.Should().Be(LiveQueriesDiagnostics.SourceName);
        created.Version.Should().NotBeNullOrWhiteSpace();
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
