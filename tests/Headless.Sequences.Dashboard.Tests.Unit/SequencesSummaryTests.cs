// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Sequences;
using Headless.Testing.Tests;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Tests;

public sealed class SequencesSummaryTests : TestBase
{
    [Fact]
    public async Task should_count_by_mode_and_current_partition_with_the_earliest_next_reset()
    {
        // given
        await using var host = await SequencesHost.StartAsync(AbortToken);
        await host.IssueGapFreeAsync("acme", "invoice", 3, AbortToken);
        await host.IssueGapFreeAsync("acme", "invoice", 1, AbortToken, partition: "2025");
        await host.IssueFastAsync("", "ticket", 1, AbortToken);
        await host.ReportAsync("acme", "shipment", 9, AbortToken);

        // when
        var (status, summary) = await host.GetAsync("/summary", AbortToken);

        // then
        status.Should().Be(HttpStatusCode.OK);
        summary.GetProperty("total").GetInt32().Should().Be(4);
        summary.GetProperty("gapFree").GetInt32().Should().Be(2);
        summary.GetProperty("fast").GetInt32().Should().Be(1);
        summary.GetProperty("reported").GetInt32().Should().Be(1);
        summary.GetProperty("current").GetInt32().Should().Be(3);
        summary.GetProperty("capped").GetBoolean().Should().BeFalse();
        summary.GetProperty("currentCapped").GetBoolean().Should().BeFalse();
        // The monthly order policy resets first: July 1, 2026, UTC.
        summary
            .GetProperty("nextResetAt")
            .GetDateTimeOffset()
            .Should()
            .Be(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task should_count_within_the_scope_and_search()
    {
        // given
        await using var host = await SequencesHost.StartAsync(AbortToken);
        await host.IssueGapFreeAsync("acme", "invoice", 1, AbortToken);
        await host.IssueFastAsync("", "ticket", 1, AbortToken);

        // when
        var (_, hostScope) = await host.GetAsync("/summary?scope=host", AbortToken);
        var (_, searched) = await host.GetAsync("/summary?q=inv", AbortToken);

        // then
        hostScope.GetProperty("total").GetInt32().Should().Be(1);
        hostScope.GetProperty("fast").GetInt32().Should().Be(1);
        searched.GetProperty("total").GetInt32().Should().Be(1);
        searched.GetProperty("gapFree").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task should_carry_a_capped_count()
    {
        // given
        var inspector = Substitute.For<ISequenceInspector>();
        inspector
            .GetSummaryAsync(Arg.Any<SequenceSummaryQuery>(), Arg.Any<CancellationToken>())
            .Returns(
                new SequenceCounterSummary
                {
                    Total = 10_000,
                    Fast = 10_000,
                    GapFree = 0,
                    Reported = 0,
                    Capped = true,
                }
            );
        await using var host = await SequencesHost.StartAsync(AbortToken, inspector: inspector);

        // when
        var (_, summary) = await host.GetAsync("/summary", AbortToken);

        // then
        summary.GetProperty("total").GetInt32().Should().Be(10_000);
        summary.GetProperty("capped").GetBoolean().Should().BeTrue();
        summary.GetProperty("currentCapped").GetBoolean().Should().BeTrue();
        summary.GetProperty("nextResetAt").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Theory]
    [InlineData("/summary")]
    [InlineData("/counters")]
    [InlineData("/counter?id=acme%2Finvoice%2F2026")]
    [InlineData("/counter/partitions?id=acme%2Finvoice%2F2026")]
    public async Task should_answer_503_without_the_failure_text_when_the_store_fails(string path)
    {
        // given
        var inspector = Substitute.For<ISequenceInspector>();
        var failure = new InvalidOperationException("SQLite Error 1: no such table: headless.sequence_counters");
        inspector.GetSummaryAsync(Arg.Any<SequenceSummaryQuery>(), Arg.Any<CancellationToken>()).ThrowsAsync(failure);
        inspector.ListCountersAsync(Arg.Any<SequenceCounterQuery>(), Arg.Any<CancellationToken>()).ThrowsAsync(failure);
        inspector
            .ListPartitionsAsync(Arg.Any<SequencePartitionQuery>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(failure);
        await using var host = await SequencesHost.StartAsync(AbortToken, inspector: inspector);

        // when
        var (status, problem) = await host.GetAsync(path, AbortToken);

        // then
        status.Should().Be(HttpStatusCode.ServiceUnavailable);
        problem.GetProperty("detail").GetString().Should().Be("The sequence store is unavailable.");
        problem.GetRawText().Should().NotContain("sequence_counters");
    }
}
