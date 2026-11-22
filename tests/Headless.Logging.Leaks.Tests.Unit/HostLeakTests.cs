// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;

namespace Tests;

/// <summary>
/// ASP.NET Core's hosting logs, HTTP logging, and the request span, on a test server. Runs alone: the hosting layer's
/// activity source is process-wide, so a host running in parallel would export another host's request span.
/// </summary>
[Collection(nameof(HostLeakCollection))]
public sealed class HostLeakTests : TestBase
{
    [Theory]
    [MemberData(nameof(LeakMatrix.HostKept), MemberType = typeof(LeakMatrix))]
    public async Task should_keep_the_value_out_of_the_sink(string scenario, Redaction redaction, Sink sink)
    {
        // given
        var hostScenario = HostScenarios.All[scenario];

        // when
        var exported = await HostLeakHost.RunAsync(hostScenario, redaction, AbortToken);

        // then
        exported.Contains(sink, Pii.Marker(scenario)).Should().BeTrue("the request must reach the {0} sink", sink);
        exported
            .Contains(sink, hostScenario.Sentinel)
            .Should()
            .BeFalse("{0} must not reach the {1} sink", hostScenario.Sentinel, sink);
    }

    [Theory]
    [MemberData(nameof(LeakMatrix.HostLeaks), MemberType = typeof(LeakMatrix))]
    public async Task should_export_the_raw_value_as_a_known_gap(string scenario, Redaction redaction, Sink sink)
    {
        // given
        var hostScenario = HostScenarios.All[scenario];

        // when
        var exported = await HostLeakHost.RunAsync(hostScenario, redaction, AbortToken);

        // then: the gap is still open; when a change closes it, move the cell to HostKept
        exported.Contains(sink, Pii.Marker(scenario)).Should().BeTrue("the request must reach the {0} sink", sink);
        exported.Contains(sink, hostScenario.Sentinel).Should().BeTrue();
    }
}

[CollectionDefinition(nameof(HostLeakCollection), DisableParallelization = true)]
public sealed class HostLeakCollection;
