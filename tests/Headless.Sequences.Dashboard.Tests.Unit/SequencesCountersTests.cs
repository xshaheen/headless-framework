// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Sequences;
using Headless.Testing.Tests;
using NSubstitute;

namespace Tests;

public sealed class SequencesCountersTests : TestBase
{
    private static List<string> _Names(JsonElement page) =>
        [
            .. page.GetProperty("items")
                .EnumerateArray()
                .Select(static item =>
                    $"{item.GetProperty("tenantId").GetString()}/{item.GetProperty("name").GetString()}"
                ),
        ];

    private static JsonElement _Item(JsonElement page, string tenant, string name) =>
        page.GetProperty("items")
            .EnumerateArray()
            .Single(item =>
                string.Equals(item.GetProperty("tenantId").GetString(), tenant, StringComparison.Ordinal)
                && string.Equals(item.GetProperty("name").GetString(), name, StringComparison.Ordinal)
            );

    private static async Task<SequencesHost> _SeededAsync(CancellationToken cancellationToken)
    {
        var host = await SequencesHost.StartAsync(cancellationToken);
        await host.IssueGapFreeAsync("acme", "invoice", 3, cancellationToken);
        await host.IssueGapFreeAsync("acme", "invoice", 7, cancellationToken, partition: "2025");
        await host.IssueFastAsync("globex", "order", 2, cancellationToken);
        await host.IssueFastAsync("", "ticket", 5, cancellationToken);
        await host.IssueFastAsync("acme", "adhoc", 1, cancellationToken);

        return host;
    }

    [Fact]
    public async Task should_list_a_gap_free_yearly_counter_with_its_last_number_and_next_reset()
    {
        // given
        await using var host = await _SeededAsync(AbortToken);

        // when
        var (status, page) = await host.GetAsync("/counters", AbortToken);

        // then
        status.Should().Be(HttpStatusCode.OK);
        var invoice = page.GetProperty("items")
            .EnumerateArray()
            .Single(static item =>
                string.Equals(item.GetProperty("id").GetString(), "acme/invoice/2026", StringComparison.Ordinal)
            );
        invoice.GetProperty("value").GetInt64().Should().Be(3);
        invoice.GetProperty("lastNumber").GetString().Should().Be("INV-2026-000003");
        invoice.GetProperty("isCurrentPartition").GetBoolean().Should().BeTrue();
        invoice.GetProperty("mode").GetString().Should().Be("GapFree");
        invoice.GetProperty("reset").GetString().Should().Be("Year");
        invoice.GetProperty("policySource").GetString().Should().Be("registered");
        invoice.GetProperty("isHost").GetBoolean().Should().BeFalse();
        // January 1, 2027 at midnight in Cairo, two hours ahead of UTC in winter.
        invoice
            .GetProperty("nextResetAt")
            .GetDateTimeOffset()
            .Should()
            .Be(new DateTimeOffset(2026, 12, 31, 22, 0, 0, TimeSpan.Zero));
        invoice.GetProperty("policy").GetProperty("timeZone").GetString().Should().Be(SequencesHost.Cairo.Id);
        invoice.GetProperty("policy").GetProperty("format").GetString().Should().Be("INV-{yyyy}-{seq:D6}");
    }

    [Fact]
    public async Task should_leave_a_counter_unchanged_across_reads()
    {
        // given
        await using var host = await _SeededAsync(AbortToken);

        // when
        var (_, first) = await host.GetAsync("/counters", AbortToken);
        var (_, second) = await host.GetAsync("/counters", AbortToken);

        // then
        _Item(second, "acme", "adhoc").GetProperty("value").GetInt64().Should().Be(1);
        second.GetRawText().Should().Be(first.GetRawText());
    }

    [Fact]
    public async Task should_list_in_key_order()
    {
        // given
        await using var host = await _SeededAsync(AbortToken);

        // when
        var (_, page) = await host.GetAsync("/counters", AbortToken);

        // then
        _Names(page).Should().Equal("/ticket", "acme/adhoc", "acme/invoice", "acme/invoice", "globex/order");
    }

    [Theory]
    [InlineData("Fast")]
    [InlineData("fast")]
    public async Task should_list_only_the_mode_asked_for(string mode)
    {
        // given
        await using var host = await _SeededAsync(AbortToken);

        // when
        var (status, page) = await host.GetAsync($"/counters?mode={mode}", AbortToken);

        // then
        status.Should().Be(HttpStatusCode.OK);
        _Names(page).Should().Equal("/ticket", "acme/adhoc", "globex/order");
    }

    [Theory]
    [InlineData("/counters?mode=1", "mode")]
    [InlineData("/counters?mode=Slow", "mode")]
    [InlineData("/counters?scope=tenant", "tenant")]
    [InlineData("/counters?tenant=acme", "tenant")]
    [InlineData("/counters?scope=host&tenant=acme", "tenant")]
    [InlineData("/counters?scope=planet", "scope")]
    [InlineData("/counters?current=maybe", "current")]
    [InlineData("/counters?limit=ten", "limit")]
    [InlineData("/counters?after=garbage", "after")]
    [InlineData("/summary?scope=tenant", "tenant")]
    public async Task should_refuse_an_invalid_filter_naming_its_field(string path, string field)
    {
        // given
        await using var host = await SequencesHost.StartAsync(AbortToken);

        // when
        var (status, problem) = await host.GetAsync(path, AbortToken);

        // then
        status.Should().Be(HttpStatusCode.BadRequest);
        problem.GetProperty("errors").TryGetProperty(field, out _).Should().BeTrue();
    }

    [Fact]
    public async Task should_list_only_host_counters_in_the_host_scope_and_one_tenant_in_the_tenant_scope()
    {
        // given
        await using var host = await _SeededAsync(AbortToken);

        // when
        var (_, hostScope) = await host.GetAsync("/counters?scope=host", AbortToken);
        var (_, acme) = await host.GetAsync("/counters?scope=tenant&tenant=acme", AbortToken);

        // then
        _Names(hostScope).Should().Equal("/ticket");
        _Item(hostScope, "", "ticket").GetProperty("isHost").GetBoolean().Should().BeTrue();
        _Names(acme).Should().Equal("acme/adhoc", "acme/invoice", "acme/invoice");
    }

    [Fact]
    public async Task should_drop_closed_partitions_and_keep_never_resetting_counters_when_current_only()
    {
        // given
        await using var host = await _SeededAsync(AbortToken);

        // when
        var (_, page) = await host.GetAsync("/counters?current=true", AbortToken);

        // then
        var ids = page.GetProperty("items").EnumerateArray().Select(static item => item.GetProperty("id").GetString());
        ids.Should().Contain("acme/invoice/2026").And.Contain("/ticket/").And.NotContain("acme/invoice/2025");
    }

    [Fact]
    public async Task should_search_names_and_tenant_ids_ignoring_ascii_case()
    {
        // given
        await using var host = await _SeededAsync(AbortToken);

        // when
        var (_, byName) = await host.GetAsync("/counters?q=INV", AbortToken);
        var (_, byTenant) = await host.GetAsync("/counters?q=GLOB", AbortToken);

        // then
        _Names(byName).Should().Equal("acme/invoice", "acme/invoice");
        _Names(byTenant).Should().Equal("globex/order");
    }

    [Fact]
    public async Task should_pass_a_non_ascii_search_to_the_inspector_as_typed()
    {
        // given
        const string search = "فاتورة-Ä";
        var inspector = Substitute.For<ISequenceInspector>();
        inspector
            .ListCountersAsync(Arg.Any<SequenceCounterQuery>(), Arg.Any<CancellationToken>())
            .Returns(new SequenceCounterPage([], Next: null));
        inspector
            .GetSummaryAsync(Arg.Any<SequenceSummaryQuery>(), Arg.Any<CancellationToken>())
            .Returns(
                new SequenceCounterSummary
                {
                    Total = 0,
                    Fast = 0,
                    GapFree = 0,
                    Reported = 0,
                    Capped = false,
                }
            );
        await using var host = await SequencesHost.StartAsync(AbortToken, inspector: inspector);

        // when
        var (countersStatus, _) = await host.GetAsync("/counters?q=" + Uri.EscapeDataString(search), AbortToken);
        var (summaryStatus, _) = await host.GetAsync("/summary?q=" + Uri.EscapeDataString(search), AbortToken);

        // then
        countersStatus.Should().Be(HttpStatusCode.OK);
        summaryStatus.Should().Be(HttpStatusCode.OK);
        await inspector
            .Received(1)
            .ListCountersAsync(
                Arg.Is<SequenceCounterQuery>(static query => query.Search == search),
                Arg.Any<CancellationToken>()
            );
        await inspector
            .Received(2)
            .GetSummaryAsync(
                Arg.Is<SequenceSummaryQuery>(static query => query.Search == search),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_refuse_a_search_longer_than_a_name_with_the_inspector_message()
    {
        // given
        await using var host = await SequencesHost.StartAsync(AbortToken);

        // when
        var (status, problem) = await host.GetAsync("/counters?q=" + new string('x', 200), AbortToken);

        // then
        status.Should().Be(HttpStatusCode.BadRequest);
        problem.GetProperty("errors").TryGetProperty("q", out _).Should().BeTrue();
    }

    [Fact]
    public async Task should_page_with_the_cursor_it_returned()
    {
        // given
        await using var host = await _SeededAsync(AbortToken);

        // when
        var (_, first) = await host.GetAsync("/counters?limit=2", AbortToken);
        var second = (
            await host.GetAsync(
                $"/counters?limit=2&after={Uri.EscapeDataString(first.GetProperty("nextCursor").GetString()!)}",
                AbortToken
            )
        ).Body;
        var third = (
            await host.GetAsync(
                $"/counters?limit=2&after={Uri.EscapeDataString(second.GetProperty("nextCursor").GetString()!)}",
                AbortToken
            )
        ).Body;

        // then
        _Names(first).Should().Equal("/ticket", "acme/adhoc");
        _Names(second).Should().Equal("acme/invoice", "acme/invoice");
        _Names(third).Should().Equal("globex/order");
        third.GetProperty("nextCursor").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task should_report_the_default_policy_for_an_unregistered_name()
    {
        // given
        await using var host = await _SeededAsync(AbortToken);

        // when
        var (_, page) = await host.GetAsync("/counters", AbortToken);

        // then
        var adhoc = _Item(page, "acme", "adhoc");
        adhoc.GetProperty("policySource").GetString().Should().Be("default");
        adhoc.GetProperty("mode").GetString().Should().Be("Fast");
        adhoc.GetProperty("lastNumber").ValueKind.Should().Be(JsonValueKind.Null);
        adhoc.GetProperty("nextResetAt").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task should_write_a_reported_counter_last_number_as_null()
    {
        // given
        await using var host = await SequencesHost.StartAsync(AbortToken);
        await host.ReportAsync("acme", "shipment", 40, AbortToken);

        // when
        var (_, page) = await host.GetAsync("/counters", AbortToken);

        // then
        var shipment = _Item(page, "acme", "shipment");
        shipment.GetProperty("value").GetInt64().Should().Be(40);
        shipment.GetProperty("lastNumber").ValueKind.Should().Be(JsonValueKind.Null);
        shipment.GetProperty("mode").GetString().Should().Be("Reported");
    }

    [Fact]
    public async Task should_refuse_an_anonymous_request()
    {
        // given
        await using var host = await SequencesHost.StartAsync(AbortToken);

        // when
        using var response = await host.SendAsync(
            HttpMethod.Get,
            SequencesHost.Api + "/counters",
            cookie: null,
            AbortToken
        );

        // then
        response.IsSuccessStatusCode.Should().BeFalse();
    }

    [Theory]
    [InlineData("/summary")]
    [InlineData("/counters")]
    [InlineData("/counter?id=acme%2Finvoice%2F2026")]
    [InlineData("/counter/partitions?id=acme%2Finvoice%2F2026")]
    public async Task should_map_no_route_that_changes_a_counter(string path)
    {
        // given
        await using var host = await _SeededAsync(AbortToken);
        var cookie = await host.SignInAsync("ops", AbortToken);

        // when
        using var post = await host.SendAsync(HttpMethod.Post, SequencesHost.Api + path, cookie, AbortToken);
        using var put = await host.SendAsync(HttpMethod.Put, SequencesHost.Api + path, cookie, AbortToken);
        using var delete = await host.SendAsync(HttpMethod.Delete, SequencesHost.Api + path, cookie, AbortToken);

        // then
        post.IsSuccessStatusCode.Should().BeFalse();
        put.IsSuccessStatusCode.Should().BeFalse();
        delete.IsSuccessStatusCode.Should().BeFalse();
        var (_, page) = await host.GetAsync("/counters", AbortToken);
        _Item(page, "acme", "adhoc").GetProperty("value").GetInt64().Should().Be(1);
    }
}
