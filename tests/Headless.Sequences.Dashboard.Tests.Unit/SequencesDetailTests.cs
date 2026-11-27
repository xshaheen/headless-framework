// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Sequences;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using NSubstitute;

namespace Tests;

public sealed class SequencesDetailTests : TestBase
{
    [Fact]
    public async Task should_read_one_counter_with_its_policy()
    {
        // given
        await using var host = await SequencesHost.StartAsync(AbortToken);
        await host.IssueGapFreeAsync("acme", "invoice", 3, AbortToken);

        // when
        var (status, counter) = await host.GetAsync(
            "/counter?id=" + Uri.EscapeDataString("acme/invoice/2026"),
            AbortToken
        );

        // then
        status.Should().Be(HttpStatusCode.OK);
        counter.GetProperty("id").GetString().Should().Be("acme/invoice/2026");
        counter.GetProperty("value").GetInt64().Should().Be(3);
        var policy = counter.GetProperty("policy");
        policy.GetProperty("start").GetInt64().Should().Be(1);
        policy.GetProperty("step").GetInt64().Should().Be(1);
        policy.GetProperty("mode").GetString().Should().Be("GapFree");
        policy.GetProperty("blockSize").GetInt32().Should().Be(1);
        policy.GetProperty("reset").GetString().Should().Be("Year");
        policy.GetProperty("format").GetString().Should().Be("INV-{yyyy}-{seq:D6}");
        policy.GetProperty("fiscalYearStartMonth").GetInt32().Should().Be(1);
        policy.GetProperty("timeZone").GetString().Should().Be(SequencesHost.Cairo.Id);
    }

    [Fact]
    public async Task should_list_the_partitions_of_the_name_newest_first()
    {
        // given
        await using var host = await SequencesHost.StartAsync(AbortToken);
        await host.IssueGapFreeAsync("acme", "invoice", 3, AbortToken);
        await host.IssueGapFreeAsync("acme", "invoice", 7, AbortToken, partition: "2025");
        await host.IssueGapFreeAsync("globex", "invoice", 1, AbortToken);

        // when
        var (_, page) = await host.GetAsync(
            "/counter/partitions?id=" + Uri.EscapeDataString("acme/invoice/2026"),
            AbortToken
        );

        // then
        var items = page.GetProperty("items").EnumerateArray().ToList();
        items.Select(static item => item.GetProperty("partition").GetString()).Should().Equal("2026", "2025");
        items.Select(static item => item.GetProperty("isCurrentPartition").GetBoolean()).Should().Equal(true, false);
        page.GetProperty("nextCursor").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task should_page_partitions_with_the_cursor_it_returned()
    {
        // given
        await using var host = await SequencesHost.StartAsync(AbortToken);
        await host.IssueGapFreeAsync("acme", "invoice", 1, AbortToken);
        await host.IssueGapFreeAsync("acme", "invoice", 1, AbortToken, partition: "2025");
        await host.IssueGapFreeAsync("acme", "invoice", 1, AbortToken, partition: "2024");
        var id = Uri.EscapeDataString("acme/invoice/2026");

        // when
        var (_, first) = await host.GetAsync($"/counter/partitions?id={id}&limit=2", AbortToken);
        var cursor = Uri.EscapeDataString(first.GetProperty("nextCursor").GetString()!);
        var (_, second) = await host.GetAsync($"/counter/partitions?id={id}&limit=2&before={cursor}", AbortToken);

        // then
        second
            .GetProperty("items")
            .EnumerateArray()
            .Select(static item => item.GetProperty("partition").GetString())
            .Should()
            .Equal("2024");
    }

    [Fact]
    public async Task should_round_trip_a_part_holding_a_slash_and_a_percent_and_a_host_counter_with_no_partition()
    {
        // given
        await using var host = await SequencesHost.StartAsync(AbortToken);
        await host.IssueFastAsync("acme", "a/b%c", 1, AbortToken);
        await host.IssueFastAsync("", "ticket", 2, AbortToken);
        var (_, page) = await host.GetAsync("/counters", AbortToken);
        var ids = page.GetProperty("items")
            .EnumerateArray()
            .Select(static item => item.GetProperty("id").GetString()!)
            .ToList();

        // when
        var (slashStatus, slashed) = await host.GetAsync(
            "/counter?id="
                + Uri.EscapeDataString(ids.Single(static id => id.StartsWith("acme/", StringComparison.Ordinal))),
            AbortToken
        );
        var (hostStatus, hosted) = await host.GetAsync("/counter?id=" + Uri.EscapeDataString("/ticket/"), AbortToken);

        // then
        ids.Should().Contain("acme/a%2Fb%25c/").And.Contain("/ticket/");
        slashStatus.Should().Be(HttpStatusCode.OK);
        slashed.GetProperty("name").GetString().Should().Be("a/b%c");
        hostStatus.Should().Be(HttpStatusCode.OK);
        hosted.GetProperty("isHost").GetBoolean().Should().BeTrue();
        hosted.GetProperty("value").GetInt64().Should().Be(2);
    }

    [Fact]
    public async Task should_answer_404_for_a_missing_counter_and_400_for_a_malformed_id()
    {
        // given
        await using var host = await SequencesHost.StartAsync(AbortToken);
        await host.IssueGapFreeAsync("acme", "invoice", 1, AbortToken);

        // when
        var (missing, _) = await host.GetAsync("/counter?id=" + Uri.EscapeDataString("acme/invoice/1999"), AbortToken);
        var (malformed, problem) = await host.GetAsync("/counter?id=acme", AbortToken);
        var (noId, _) = await host.GetAsync("/counter", AbortToken);
        var (badBefore, _) = await host.GetAsync(
            "/counter/partitions?id=" + Uri.EscapeDataString("acme/invoice/2026") + "&before=x",
            AbortToken
        );

        // then
        missing.Should().Be(HttpStatusCode.NotFound);
        malformed.Should().Be(HttpStatusCode.BadRequest);
        problem.GetProperty("errors").TryGetProperty("id", out _).Should().BeTrue();
        noId.Should().Be(HttpStatusCode.BadRequest);
        badBefore.Should().Be(HttpStatusCode.BadRequest);
    }

    public static TheoryData<string, string> UnstorablePartitionIds =>
        new()
        {
            { "/counter", "acme/invoice/" + new string('9', SequenceFieldLimits.PartitionMaxLength + 1) },
            { "/counter/partitions", "acme/invoice/" + new string('9', SequenceFieldLimits.PartitionMaxLength + 1) },
            { "/counter", "acme/invoice/%202026" },
            { "/counter/partitions", "acme/invoice/%202026" },
            { "/counter", "acme/invoice/2026%20" },
            { "/counter/partitions", "acme/invoice/2026%20" },
            { "/counter", "acme/invoice/%20" },
            { "/counter/partitions", "acme/invoice/%20" },
        };

    [Theory]
    [MemberData(nameof(UnstorablePartitionIds))]
    public async Task should_answer_400_naming_id_without_a_store_read_when_the_partition_cannot_be_stored(
        string path,
        string id
    )
    {
        // given
        var inspector = Substitute.For<ISequenceInspector>();
        await using var host = await SequencesHost.StartAsync(AbortToken, inspector: inspector);

        // when
        var (status, problem) = await host.GetAsync($"{path}?id={Uri.EscapeDataString(id)}", AbortToken);

        // then
        status.Should().Be(HttpStatusCode.BadRequest);
        problem.GetProperty("errors").TryGetProperty("id", out _).Should().BeTrue();
        await inspector
            .DidNotReceiveWithAnyArgs()
            .ListPartitionsAsync(Arg.Any<SequencePartitionQuery>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("/counter")]
    [InlineData("/counter/partitions")]
    public async Task should_read_a_counter_with_no_partition_by_its_id(string path)
    {
        // given
        await using var host = await SequencesHost.StartAsync(AbortToken);
        await host.IssueFastAsync("", "ticket", 2, AbortToken);

        // when
        var (status, _) = await host.GetAsync($"{path}?id={Uri.EscapeDataString("/ticket/")}", AbortToken);

        // then
        status.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task should_let_a_cancelled_request_end_instead_of_answering_503()
    {
        // given: the store read is still running when the client goes away
        using var aborted = new CancellationTokenSource();
        var inspector = Substitute.For<ISequenceInspector>();
        inspector
            .ListCountersAsync(Arg.Any<SequenceCounterQuery>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<SequenceCounterPage>>(call =>
            {
                aborted.Cancel();

                throw new OperationCanceledException(call.Arg<CancellationToken>());
            });
        await using var host = await SequencesHost.StartAsync(
            AbortToken,
            inspector: inspector,
            configureApp: app =>
                app.Use(
                    (HttpContext context, RequestDelegate next) =>
                    {
                        context.RequestAborted = aborted.Token;

                        return next(context);
                    }
                )
        );
        var cookie = await host.SignInAsync("viewer", AbortToken);

        // when
        var send = async () =>
        {
            using var response = await host.SendAsync(
                HttpMethod.Get,
                SequencesHost.Api + "/counters",
                cookie,
                AbortToken
            );

            return response.StatusCode;
        };

        // then
        await send.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_stop_looking_past_the_newest_thousand_partitions()
    {
        // given: a name with far more partitions than the lookup reads, none of them the one asked for
        var inspector = Substitute.For<ISequenceInspector>();
        var policy = new SequencePolicy();
        inspector
            .ListPartitionsAsync(Arg.Any<SequencePartitionQuery>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var query = call.Arg<SequencePartitionQuery>();
                var counters = Enumerable
                    .Range(0, query.Limit)
                    .Select(i => new SequenceCounter
                    {
                        Key = new SequenceKey("acme", "device", $"d{i}"),
                        Value = 1,
                        UpdatedAt = SequencesHost.Now,
                        Policy = policy,
                        IsCurrentPartition = true,
                    })
                    .ToList();

                return new ValueTask<SequencePartitionPage>(new SequencePartitionPage(counters, "more"));
            });
        await using var host = await SequencesHost.StartAsync(AbortToken, inspector: inspector);

        // when
        var (status, _) = await host.GetAsync("/counter?id=" + Uri.EscapeDataString("acme/device/zz"), AbortToken);

        // then
        status.Should().Be(HttpStatusCode.NotFound);
        await inspector
            .Received(SequencesEndpoints.LookupPages)
            .ListPartitionsAsync(Arg.Any<SequencePartitionQuery>(), Arg.Any<CancellationToken>());
    }
}
