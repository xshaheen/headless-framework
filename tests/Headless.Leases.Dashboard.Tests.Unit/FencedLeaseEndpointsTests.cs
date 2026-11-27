// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Fencing;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Tests;

/// <summary>The fenced-lease reads over a real in-memory fencing store on a fake clock.</summary>
public sealed class FencedLeaseEndpointsTests : TestBase
{
    private const string _Fenced = LeasesDashboardHost.Api + "/fenced";

    private readonly FakeTimeProvider _time = new(LeasesDashboardHost.Now);

    /// <summary>
    /// Three live, two expired, and four ended leases across the host scope and the tenants <c>acme</c> and
    /// <c>globex</c>.
    /// </summary>
    private async Task<WebApplication> _StartSeededAsync()
    {
        var app = await LeasesDashboardHost.StartAsync(AbortToken, _time, fencing: true);
        var leases = app.Services.GetRequiredService<IFencedLeases>();

        await LeasesDashboardHost.GrantAsync(app, null, "ledger-export", "export/1", AbortToken);
        await LeasesDashboardHost.GrantAsync(app, "acme", "ledger-export", "export/2", AbortToken);
        await LeasesDashboardHost.GrantAsync(app, "globex", "invoice-batch", "invoice/1", AbortToken);
        await LeasesDashboardHost.GrantAsync(app, "acme", "cleanup", "stuck/1", AbortToken, TimeSpan.FromSeconds(1));
        await LeasesDashboardHost.GrantAsync(app, null, "cleanup", "stuck/2", AbortToken, TimeSpan.FromSeconds(1));
        var settled = await LeasesDashboardHost.GrantAsync(app, "acme", "ledger-export", "done/1", AbortToken);
        var settled2 = await LeasesDashboardHost.GrantAsync(app, "globex", "ledger-export", "done/2", AbortToken);
        var released = await LeasesDashboardHost.GrantAsync(app, null, "invoice-batch", "done/3", AbortToken);
        await LeasesDashboardHost.GrantAsync(app, "acme", "invoice-batch", "done/4", AbortToken);

        await leases.SettleAsync(settled, AbortToken);
        await leases.SettleAsync(settled2, AbortToken);
        await leases.ReleaseAsync(released, AbortToken);

        using (app.Services.GetRequiredService<Headless.MultiTenancy.ICurrentTenant>().Change("acme"))
        {
            await leases.RevokeAsync("invoice-batch", "done/4", AbortToken);
        }

        _time.Advance(TimeSpan.FromSeconds(2));

        return app;
    }

    private static List<string> _Resources(JsonElement page) =>
        [
            .. page.GetProperty("items")
                .EnumerateArray()
                .Select(static item => item.GetProperty("resource").GetString()!),
        ];

    [Fact]
    public async Task should_list_every_lease_in_tenant_kind_resource_order_with_the_host_scope_first()
    {
        // given
        await using var app = await _StartSeededAsync();

        // when
        var page = await LeasesDashboardHost.GetJsonAsync(app, _Fenced, AbortToken);

        // then
        var items = page.GetProperty("items").EnumerateArray().ToList();
        items.Should().HaveCount(9);
        items
            .Select(static item =>
                (
                    item.GetProperty("tenantId").ValueKind == JsonValueKind.Null
                        ? ""
                        : item.GetProperty("tenantId").GetString()!,
                    item.GetProperty("kind").GetString()!,
                    item.GetProperty("resource").GetString()!
                )
            )
            .Should()
            .BeInAscendingOrder(
                Comparer<(string, string, string)>.Create(
                    static (a, b) =>
                        string.CompareOrdinal($"{a.Item1}\n{a.Item2}\n{a.Item3}", $"{b.Item1}\n{b.Item2}\n{b.Item3}")
                )
            );
        items[0].GetProperty("tenantId").ValueKind.Should().Be(JsonValueKind.Null);
        page.GetProperty("nextCursor").ValueKind.Should().Be(JsonValueKind.Null);
        page.GetProperty("readAt").GetDateTimeOffset().Should().Be(_time.GetUtcNow());
    }

    [Fact]
    public async Task should_filter_by_state_and_refuse_an_unknown_state_naming_it()
    {
        // given
        await using var app = await _StartSeededAsync();

        // when
        var expired = await LeasesDashboardHost.GetJsonAsync(app, _Fenced + "?state=Expired", AbortToken);
        using var bad = await LeasesDashboardHost.SendAsync(app, _Fenced + "?state=Left", AbortToken);

        // then
        _Resources(expired).Should().BeEquivalentTo("stuck/1", "stuck/2");
        expired
            .GetProperty("items")
            .EnumerateArray()
            .Should()
            .AllSatisfy(static item => item.GetProperty("state").GetString().Should().Be("Expired"));
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await LeasesDashboardHost.ReadAsync(bad, AbortToken))
            .GetProperty("errors")
            .TryGetProperty("state", out _)
            .Should()
            .BeTrue();
    }

    [Fact]
    public async Task should_carry_the_stored_status_beside_the_state_group()
    {
        // given
        await using var app = await _StartSeededAsync();

        // when
        var ended = await LeasesDashboardHost.GetJsonAsync(app, _Fenced + "?state=Ended", AbortToken);

        // then
        ended
            .GetProperty("items")
            .EnumerateArray()
            .Select(static item => item.GetProperty("status").GetString())
            .Should()
            .BeEquivalentTo("Settled", "Settled", "Released", "Revoked");
    }

    [Fact]
    public async Task should_filter_by_host_scope_or_one_tenant_and_refuse_both_at_once()
    {
        // given
        await using var app = await _StartSeededAsync();

        // when
        var host = await LeasesDashboardHost.GetJsonAsync(app, _Fenced + "?scope=host", AbortToken);
        var acme = await LeasesDashboardHost.GetJsonAsync(app, _Fenced + "?tenant=acme", AbortToken);
        using var both = await LeasesDashboardHost.SendAsync(app, _Fenced + "?scope=host&tenant=acme", AbortToken);

        // then
        _Resources(host).Should().BeEquivalentTo("export/1", "stuck/2", "done/3");
        _Resources(acme).Should().BeEquivalentTo("export/2", "stuck/1", "done/1", "done/4");
        both.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task should_match_the_resource_prefix_literally()
    {
        // given
        await using var app = await LeasesDashboardHost.StartAsync(AbortToken, _time, fencing: true);
        await LeasesDashboardHost.GrantAsync(app, null, "ledger-export", "export/1", AbortToken);
        await LeasesDashboardHost.GrantAsync(app, null, "ledger-export", "export1", AbortToken);
        await LeasesDashboardHost.GrantAsync(app, null, "ledger-export", "invoice/1", AbortToken);

        // when
        var prefix = await LeasesDashboardHost.GetJsonAsync(app, _Fenced + "?prefix=export/", AbortToken);
        var wildcard = await LeasesDashboardHost.GetJsonAsync(app, _Fenced + "?prefix=export%25", AbortToken);

        // then
        _Resources(prefix).Should().Equal("export/1");
        _Resources(wildcard).Should().BeEmpty();
    }

    [Fact]
    public async Task should_page_fifty_at_a_time_with_no_repeat_or_gap()
    {
        // given
        await using var app = await LeasesDashboardHost.StartAsync(AbortToken, _time, fencing: true);

        for (var i = 0; i < 120; i++)
        {
            await LeasesDashboardHost.GrantAsync(
                app,
                null,
                "ledger-export",
                string.Create(CultureInfo.InvariantCulture, $"batch/{i:D3}"),
                AbortToken
            );
        }

        // when
        var seen = new List<string>();
        var sizes = new List<int>();
        string? cursor = null;

        do
        {
            var page = await LeasesDashboardHost.GetJsonAsync(
                app,
                cursor is null ? _Fenced : _Fenced + "?after=" + Uri.EscapeDataString(cursor),
                AbortToken
            );
            var resources = _Resources(page);
            sizes.Add(resources.Count);
            seen.AddRange(resources);
            cursor = page.GetProperty("nextCursor").GetString();
        } while (cursor is not null);

        // then
        sizes.Should().Equal(50, 50, 20);
        seen.Should().OnlyHaveUniqueItems().And.HaveCount(120);
    }

    [Fact]
    public async Task should_refuse_a_cursor_it_did_not_write_naming_after()
    {
        // given
        await using var app = await LeasesDashboardHost.StartAsync(AbortToken, _time, fencing: true);

        // when
        using var response = await LeasesDashboardHost.SendAsync(app, _Fenced + "?after=garbage", AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await LeasesDashboardHost.ReadAsync(response, AbortToken))
            .GetProperty("errors")
            .TryGetProperty("after", out _)
            .Should()
            .BeTrue();
    }

    [Fact]
    public async Task should_count_each_state_for_the_filter()
    {
        // given
        await using var app = await _StartSeededAsync();

        // when
        var counts = await LeasesDashboardHost.GetJsonAsync(app, _Fenced + "/counts", AbortToken);

        // then
        counts.GetProperty("live").GetProperty("count").GetInt32().Should().Be(3);
        counts.GetProperty("expired").GetProperty("count").GetInt32().Should().Be(2);
        counts.GetProperty("ended").GetProperty("count").GetInt32().Should().Be(4);
        counts.GetProperty("total").GetProperty("count").GetInt32().Should().Be(9);
        counts.GetProperty("total").GetProperty("capped").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task should_carry_the_cap_flag_of_a_capped_count()
    {
        // given
        var inspector = Substitute.For<IFencedLeaseInspector>();
        inspector
            .CountAsync(Arg.Any<FencedLeaseQuery>(), Arg.Any<CancellationToken>())
            .Returns(
                new FencedLeaseCounts(
                    new LeaseCount(LeaseCount.DefaultCap, Capped: true),
                    new LeaseCount(0, Capped: false),
                    new LeaseCount(0, Capped: false)
                )
            );
        await using var app = await LeasesDashboardHost.StartAsync(
            AbortToken,
            _time,
            before: services => services.AddSingleton(inspector)
        );

        // when
        var counts = await LeasesDashboardHost.GetJsonAsync(app, _Fenced + "/counts", AbortToken);

        // then
        counts.GetProperty("live").GetProperty("capped").GetBoolean().Should().BeTrue();
        counts.GetProperty("live").GetProperty("count").GetInt32().Should().Be(10_000);
        counts.GetProperty("total").GetProperty("capped").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task should_read_the_kinds_once_per_thirty_seconds_and_never_keep_a_failed_read()
    {
        // given
        var inspector = Substitute.For<IFencedLeaseInspector>();
        inspector.ListKindsAsync(Arg.Any<CancellationToken>()).Returns(["cleanup", "ledger-export"]);
        await using var app = await LeasesDashboardHost.StartAsync(
            AbortToken,
            _time,
            before: services => services.AddSingleton(inspector)
        );

        // when read twice within the window
        var kinds = await LeasesDashboardHost.GetJsonAsync(app, _Fenced + "/kinds", AbortToken);
        await LeasesDashboardHost.GetJsonAsync(app, _Fenced + "/kinds", AbortToken);

        // then
        kinds
            .GetProperty("items")
            .EnumerateArray()
            .Select(static kind => kind.GetString())
            .Should()
            .Equal("cleanup", "ledger-export");
        await inspector.Received(1).ListKindsAsync(Arg.Any<CancellationToken>());

        // when the window passes and the next read fails
        _time.Advance(TimeSpan.FromSeconds(31));
        inspector.ClearReceivedCalls();
        inspector
            .ListKindsAsync(Arg.Any<CancellationToken>())
            .Returns(
                _ => ValueTask.FromException<IReadOnlyList<string>>(new InvalidOperationException("store down")),
                _ => ValueTask.FromResult<IReadOnlyList<string>>(["cleanup"])
            );
        using var failed = await LeasesDashboardHost.SendAsync(app, _Fenced + "/kinds", AbortToken);
        var retried = await LeasesDashboardHost.GetJsonAsync(app, _Fenced + "/kinds", AbortToken);

        // then
        ((int)failed.StatusCode)
            .Should()
            .BeGreaterThanOrEqualTo(500);
        retried.GetProperty("items").GetArrayLength().Should().Be(1);
        await inspector.Received(2).ListKindsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_answer_a_failed_list_read_with_a_server_error_not_an_empty_page()
    {
        // given
        var inspector = Substitute.For<IFencedLeaseInspector>();
        inspector
            .ListAsync(
                Arg.Any<FencedLeaseQuery>(),
                Arg.Any<FencedLeasePosition?>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(new InvalidOperationException("store down"));
        await using var app = await LeasesDashboardHost.StartAsync(
            AbortToken,
            _time,
            before: services => services.AddSingleton(inspector)
        );

        // when
        using var response = await LeasesDashboardHost.SendAsync(app, _Fenced, AbortToken);

        // then
        ((int)response.StatusCode)
            .Should()
            .BeGreaterThanOrEqualTo(500);
    }

    [Fact]
    public async Task should_show_the_progress_size_and_contract_and_never_the_bytes()
    {
        // given
        await using var app = await LeasesDashboardHost.StartAsync(AbortToken, _time, fencing: true);
        var lease = await LeasesDashboardHost.GrantAsync(app, "acme", "ledger-export", "export/7", AbortToken);
        var bytes = new byte[1229];
        Array.Fill(bytes, (byte)'Z');
        var renewal = await app
            .Services.GetRequiredService<IFencedLeases>()
            .RenewAsync(lease, TimeSpan.FromMinutes(5), new LeaseProgress(bytes, "export-cursor/v1"), AbortToken);
        renewal.Status.Should().Be(LeaseRenewalStatus.Renewed);

        // when
        using var response = await LeasesDashboardHost.SendAsync(app, _Fenced, AbortToken);
        var body = await response.Content.ReadAsStringAsync(AbortToken);

        // then
        var item = (await LeasesDashboardHost.ReadAsync(response, AbortToken)).GetProperty("items")[0];
        item.GetProperty("progressSizeBytes").GetInt32().Should().Be(1229);
        item.GetProperty("progressContract").GetString().Should().Be("export-cursor/v1");
        item.GetProperty("holder").ValueKind.Should().Be(JsonValueKind.Null);
        body.Should().NotContain("ZZZZ").And.NotContain(Convert.ToBase64String(bytes)[..16]);
    }

    [Fact]
    public async Task should_look_up_one_lease_by_its_id_and_answer_not_found_once_it_is_purged()
    {
        // given
        await using var app = await LeasesDashboardHost.StartAsync(AbortToken, _time, fencing: true);
        var lease = await LeasesDashboardHost.GrantAsync(app, null, "cleanup", "batch/7", AbortToken);
        var page = await LeasesDashboardHost.GetJsonAsync(app, _Fenced, AbortToken);
        var id = page.GetProperty("items")[0].GetProperty("id").GetString()!;

        // when
        var found = await LeasesDashboardHost.GetJsonAsync(
            app,
            _Fenced + "/lease?id=" + Uri.EscapeDataString(id),
            AbortToken
        );
        var leases = app.Services.GetRequiredService<IFencedLeases>();
        await leases.ReleaseAsync(lease, AbortToken);
        _time.Advance(TimeSpan.FromMinutes(2));
        await leases.PurgeAsync("cleanup", TimeSpan.FromMinutes(1), AbortToken);
        using var gone = await LeasesDashboardHost.SendAsync(
            app,
            _Fenced + "/lease?id=" + Uri.EscapeDataString(id),
            AbortToken
        );
        using var malformed = await LeasesDashboardHost.SendAsync(app, _Fenced + "/lease?id=a%2Fb", AbortToken);

        // then
        id.Should().Be("/cleanup/batch%2F7");
        found.GetProperty("lease").GetProperty("resource").GetString().Should().Be("batch/7");
        gone.StatusCode.Should().Be(HttpStatusCode.NotFound);
        malformed.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task should_answer_not_found_naming_add_headless_fencing_on_a_host_without_fencing()
    {
        // given
        await using var app = await LeasesDashboardHost.StartAsync(AbortToken, _time, locks: true);

        // when
        var summary = await LeasesDashboardHost.GetJsonAsync(app, LeasesDashboardHost.Api + "/summary", AbortToken);
        using var response = await LeasesDashboardHost.SendAsync(app, _Fenced, AbortToken);

        // then
        summary.GetProperty("fencing").ValueKind.Should().Be(JsonValueKind.Null);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync(AbortToken)).Should().Contain("AddHeadlessFencing");
    }

    [Fact]
    public async Task should_let_a_viewer_read_and_refuse_an_anonymous_request()
    {
        // given
        await using var app = await _StartSeededAsync();

        // when
        var viewer = await LeasesDashboardHost.GetJsonAsync(app, _Fenced, AbortToken, role: "viewer");
        using var client = LeasesDashboardHost.Client(app);
        using var anonymous = await LeasesDashboardHost.GetAsync(client, _Fenced, cookie: null, AbortToken);

        // then
        viewer.GetProperty("items").GetArrayLength().Should().Be(9);
        anonymous.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }
}
