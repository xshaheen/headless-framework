// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;

namespace Tests;

/// <summary>
/// Pushes each log scenario through a real pipeline and checks the bytes every sink received. Each cell first checks
/// that the entry reached the sink (its marker is there), so a sink that received nothing cannot pass as redacted.
/// </summary>
public sealed class LogLeakTests : TestBase
{
    [Theory]
    [MemberData(nameof(LeakMatrix.LogKept), MemberType = typeof(LeakMatrix))]
    public void should_keep_the_value_out_of_the_sink(
        string scenario,
        LogPipeline pipeline,
        Redaction redaction,
        Sink sink
    )
    {
        // given
        var leakScenario = LogScenarios.All[scenario];

        // when
        var exported = _Run(leakScenario, pipeline, redaction);

        // then
        exported.Contains(sink, Pii.Marker(scenario)).Should().BeTrue("the entry must reach the {0} sink", sink);
        exported
            .Contains(sink, leakScenario.Sentinel)
            .Should()
            .BeFalse("{0} must not reach the {1} sink: {2}", leakScenario.Sentinel, sink, exported.Text(sink));
    }

    [Theory]
    [MemberData(nameof(LeakMatrix.LogLeaks), MemberType = typeof(LeakMatrix))]
    public void should_export_the_raw_value_as_a_known_gap(
        string scenario,
        LogPipeline pipeline,
        Redaction redaction,
        Sink sink
    )
    {
        // given
        var leakScenario = LogScenarios.All[scenario];

        // when
        var exported = _Run(leakScenario, pipeline, redaction);

        // then: the gap is still open; when a change closes it, move the cell to LogKept
        exported.Contains(sink, Pii.Marker(scenario)).Should().BeTrue("the entry must reach the {0} sink", sink);
        exported.Contains(sink, leakScenario.Sentinel).Should().BeTrue();
    }

    private static Exported _Run(LogScenario scenario, LogPipeline pipeline, Redaction redaction)
    {
        var host = LogLeakHost.Start(pipeline, redaction);

        try
        {
            scenario.Write(host);

            return host.Collect();
        }
        finally
        {
            host.Dispose();
            host.DeleteFiles();
        }
    }
}
