// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;

namespace Tests;

/// <summary>Span attributes, events, status, and propagated baggage, with and without the Headless span processors.</summary>
public sealed class SpanLeakTests : TestBase
{
    [Theory]
    [MemberData(nameof(LeakMatrix.SpanKept), MemberType = typeof(LeakMatrix))]
    public void should_keep_the_value_out_of_the_sink(string scenario, Redaction redaction, Sink sink)
    {
        // given
        var spanScenario = SpanScenarios.All[scenario];

        // when
        var exported = SpanLeakHost.Run(spanScenario, redaction);

        // then
        exported
            .Contains(sink, Pii.Marker(scenario))
            .Should()
            .BeTrue("the span or carrier must reach the {0} sink", sink);
        exported
            .Contains(sink, spanScenario.Sentinel)
            .Should()
            .BeFalse("{0} must not reach the {1} sink", spanScenario.Sentinel, sink);
    }

    [Theory]
    [MemberData(nameof(LeakMatrix.SpanLeaks), MemberType = typeof(LeakMatrix))]
    public void should_export_the_raw_value_as_a_known_gap(string scenario, Redaction redaction, Sink sink)
    {
        // given
        var spanScenario = SpanScenarios.All[scenario];

        // when
        var exported = SpanLeakHost.Run(spanScenario, redaction);

        // then: the gap is still open; when a change closes it, move the cell to SpanKept
        exported
            .Contains(sink, Pii.Marker(scenario))
            .Should()
            .BeTrue("the span or carrier must reach the {0} sink", sink);
        exported.Contains(sink, spanScenario.Sentinel).Should().BeTrue();
    }
}
