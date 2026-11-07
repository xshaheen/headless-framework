// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard;
using Headless.Testing.Tests;

namespace Tests.Aggregation;

public sealed class TraceLinkTests : TestBase
{
    private const string _Template = "https://apm.example/trace/{traceId}";

    [Fact]
    public void should_fill_the_template_when_the_trace_id_is_a_w3c_trace_id()
    {
        TraceLink
            .Build(_Template, "4bf92f3577b34da6a3ce929d0e0e4736")
            .Should()
            .Be("https://apm.example/trace/4bf92f3577b34da6a3ce929d0e0e4736");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void should_build_no_link_when_the_host_set_no_template(string? template)
    {
        TraceLink.Build(template, "4bf92f3577b34da6a3ce929d0e0e4736").Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("00000000000000000000000000000000")]
    [InlineData("4BF92F3577B34DA6A3CE929D0E0E4736")]
    [InlineData("4bf92f3577b34da6a3ce929d0e0e473")]
    [InlineData("4bf92f3577b34da6/../../admin?x=1")]
    public void should_build_no_link_when_the_trace_id_is_not_a_w3c_trace_id(string? traceId)
    {
        TraceLink.Build(_Template, traceId).Should().BeNull();
    }
}
