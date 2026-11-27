// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http.Json;
using Headless.Coordination;
using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// The module's shell answers through the shell's own endpoints: Needs you, the sidebar counts, and id lookup, on a
/// host with both halves, and with a membership store that does not answer.
/// </summary>
public sealed class ClusterSourcesTests : TestBase
{
    private static readonly DateTimeOffset _Beat = new(2026, 11, 27, 9, 59, 30, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(ClusterDashboardHost.Now);
    private readonly FakeMembership _membership = new() { Identity = NodeIdentity.Parse("orders-api-0@14") };
    private readonly HealthToggle _jobs = new() { Status = HealthStatus.Unhealthy };
    private readonly HealthToggle _dependency = new() { Status = HealthStatus.Degraded };
    private readonly HealthToggle _blobs = new();

    public ClusterSourcesTests()
    {
        _membership.SetSnapshot(
            FakeMembership.Node("orders-api-0@14", NodeLivenessState.Alive, "api", _Beat),
            FakeMembership.Node("orders-api-0@13", NodeLivenessState.Dead, "api", _Beat.AddMinutes(-5)),
            FakeMembership.Node("orders-worker-1@5", NodeLivenessState.Suspected, "worker", _Beat),
            FakeMembership.Node("orders-worker-2@3", NodeLivenessState.Dead, "worker", _Beat.AddMinutes(-1))
        );
    }

    [Fact]
    public async Task should_list_bad_checks_as_health_check_items_and_suspected_or_dead_nodes_as_work()
    {
        // given
        await using var app = await _StartAsync();
        await app.Services.GetRequiredService<IHealthCheckMonitor>().ProbeAllAsync(AbortToken);

        // when
        var items = _Items(await ClusterDashboardHost.GetJsonAsync(app, "/headless/api/needs-you", AbortToken));

        // then
        items
            .Select(static item =>
                (
                    item.GetProperty("id").GetString(),
                    item.GetProperty("kind").GetString(),
                    item.GetProperty("severity").GetString()
                )
            )
            .Should()
            .BeEquivalentTo([
                ("cluster.check.jobs", "health-check", "critical"),
                ("cluster.check.sandbox-dependency", "health-check", "warning"),
                ("cluster.node.orders-worker-1@5", "work", "warning"),
                ("cluster.node.orders-api-0@13", "work", "critical"),
                ("cluster.node.orders-worker-2@3", "work", "critical"),
            ]);
        var jobs = items.Single(static item =>
            string.Equals(item.GetProperty("id").GetString(), "cluster.check.jobs", StringComparison.Ordinal)
        );
        jobs.GetProperty("groupKey").GetString().Should().Be("cluster.health.Unhealthy");
        jobs.GetProperty("target").GetProperty("kind").GetString().Should().Be("check");
        var suspected = items.Single(static item =>
            string.Equals(
                item.GetProperty("id").GetString(),
                "cluster.node.orders-worker-1@5",
                StringComparison.Ordinal
            )
        );
        suspected.GetProperty("groupKey").GetString().Should().Be("cluster.nodes.Suspected");
        suspected.GetProperty("since").GetDateTimeOffset().Should().Be(_Beat);
    }

    [Fact]
    public async Task should_date_a_check_item_from_the_first_result_of_its_current_run_of_that_status()
    {
        // given a check that was healthy once, then degraded three times
        _dependency.Status = HealthStatus.Healthy;
        await using var app = await _StartAsync();
        var monitor = app.Services.GetRequiredService<IHealthCheckMonitor>();
        await monitor.ProbeAsync("sandbox-dependency", AbortToken);
        _dependency.Status = HealthStatus.Degraded;
        var firstDegraded = _time.GetUtcNow().AddSeconds(10);

        for (var probe = 0; probe < 3; probe++)
        {
            _time.Advance(TimeSpan.FromSeconds(10));
            await monitor.ProbeAsync("sandbox-dependency", AbortToken);
        }

        // when
        var items = _Items(await ClusterDashboardHost.GetJsonAsync(app, "/headless/api/needs-you", AbortToken));

        // then
        var item = items.Single(static item =>
            string.Equals(
                item.GetProperty("id").GetString(),
                "cluster.check.sandbox-dependency",
                StringComparison.Ordinal
            )
        );
        item.GetProperty("since").GetDateTimeOffset().Should().Be(firstDegraded);
        item.GetProperty("lastSeenAt").GetDateTimeOffset().Should().Be(_time.GetUtcNow());
    }

    [Fact]
    public async Task should_keep_the_health_answers_and_name_coordination_when_the_store_does_not_answer()
    {
        // given
        await using var app = await _StartAsync();
        await app.Services.GetRequiredService<IHealthCheckMonitor>().ProbeAllAsync(AbortToken);
        _membership.IsUnreachable = true;

        // when
        var needsYou = await ClusterDashboardHost.GetJsonAsync(app, "/headless/api/needs-you", AbortToken);
        var counts = await ClusterDashboardHost.GetJsonAsync(app, "/headless/api/counts?product=cluster", AbortToken);

        // then
        var module = _Module(needsYou);
        module.GetProperty("stale").GetBoolean().Should().BeFalse();
        module
            .GetProperty("unavailable")
            .EnumerateArray()
            .Select(static s => s.GetString())
            .Should()
            .Equal("coordination");
        _Items(needsYou)
            .Should()
            .OnlyContain(static item =>
                string.Equals(item.GetProperty("kind").GetString(), "health-check", StringComparison.Ordinal)
            );
        var answered = counts.GetProperty("counts");
        answered.GetProperty("checks").GetProperty("count").GetInt64().Should().Be(3);
        answered.GetProperty("Degraded").GetProperty("count").GetInt64().Should().Be(1);
        answered.GetProperty("Unhealthy").GetProperty("count").GetInt64().Should().Be(1);
        answered
            .GetProperty("nodes")
            .GetProperty("unavailable")
            .EnumerateArray()
            .Select(static s => s.GetString())
            .Should()
            .Equal("coordination");
    }

    [Fact]
    public async Task should_count_every_incarnation_by_state_and_every_check_by_its_latest_status()
    {
        // given
        await using var app = await _StartAsync();
        await app.Services.GetRequiredService<IHealthCheckMonitor>().ProbeAllAsync(AbortToken);

        // when
        var counts = (
            await ClusterDashboardHost.GetJsonAsync(app, "/headless/api/counts?product=cluster", AbortToken)
        ).GetProperty("counts");

        // then
        long Count(string key) => counts.GetProperty(key).GetProperty("count").GetInt64();

        Count("nodes").Should().Be(4);
        Count("Alive").Should().Be(1);
        Count("Suspected").Should().Be(1);
        Count("Dead").Should().Be(2);
        Count("checks").Should().Be(3);
        Count("Degraded").Should().Be(1);
        Count("Unhealthy").Should().Be(1);
    }

    [Fact]
    public async Task should_answer_no_node_keys_without_coordination_and_no_check_keys_without_health_checks()
    {
        // given
        await using var healthOnly = await ClusterDashboardHost.StartAsync(
            AbortToken,
            before: services => _blobs.AddTo(services.AddHealthChecks(), "blobs")
        );
        await using var coordinationOnly = await ClusterDashboardHost.StartAsync(AbortToken, membership: _membership);

        // when
        var health = (
            await ClusterDashboardHost.GetJsonAsync(healthOnly, "/headless/api/counts?product=cluster", AbortToken)
        ).GetProperty("counts");
        var nodes = (
            await ClusterDashboardHost.GetJsonAsync(
                coordinationOnly,
                "/headless/api/counts?product=cluster",
                AbortToken
            )
        ).GetProperty("counts");

        // then
        health
            .EnumerateObject()
            .Select(static key => key.Name)
            .Should()
            .BeEquivalentTo("checks", "Degraded", "Unhealthy");
        nodes
            .EnumerateObject()
            .Select(static key => key.Name)
            .Should()
            .BeEquivalentTo("nodes", "Alive", "Suspected", "Dead");
    }

    [Fact]
    public async Task should_answer_the_module_as_stale_on_a_coordination_only_host_whose_store_does_not_answer()
    {
        // given no health counts answer in the store's place, so the module has no count to keep
        await using var app = await ClusterDashboardHost.StartAsync(AbortToken, membership: _membership);
        _membership.IsUnreachable = true;

        // when
        var answer = await ClusterDashboardHost.GetJsonAsync(app, "/headless/api/counts?product=cluster", AbortToken);

        // then
        answer.GetProperty("counts").EnumerateObject().Should().BeEmpty();
        answer
            .GetProperty("stale")
            .EnumerateArray()
            .Select(static module => module.GetProperty("id").GetString())
            .Should()
            .Equal("cluster");
    }

    [Theory]
    [InlineData("orders-api-0@14", new[] { "node:orders-api-0@14" })]
    [InlineData("orders-api-0", new[] { "node:orders-api-0@13", "node:orders-api-0@14" })]
    [InlineData("JOBS", new[] { "check:jobs" })]
    [InlineData("nothing", new string[0])]
    public async Task should_resolve_an_exact_identity_a_bare_node_id_and_a_check_name(string query, string[] expected)
    {
        // given
        await using var app = await _StartAsync();
        using var client = ClusterDashboardHost.Client(app);
        var cookie = await ClusterDashboardHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var response = await ClusterDashboardHost.PostAsync(
            client,
            "/headless/api/resolve",
            new { query },
            cookie,
            AbortToken
        );

        // then
        var body = await ClusterDashboardHost.ReadAsync(response, AbortToken);
        _Items(body)
            .Select(static item =>
                item.GetProperty("target").GetProperty("kind").GetString()
                + ":"
                + item.GetProperty("target").GetProperty("id").GetString()
            )
            .Should()
            .Equal(expected);
    }

    [Fact]
    public async Task should_still_resolve_a_health_check_when_the_membership_store_does_not_answer()
    {
        // given
        await using var app = await _StartAsync();
        _membership.IsUnreachable = true;
        using var client = ClusterDashboardHost.Client(app);
        var cookie = await ClusterDashboardHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var response = await ClusterDashboardHost.PostAsync(
            client,
            "/headless/api/resolve",
            new { query = "jobs" },
            cookie,
            AbortToken
        );

        // then
        var body = await ClusterDashboardHost.ReadAsync(response, AbortToken);
        _Items(body)
            .Select(static item =>
                item.GetProperty("target").GetProperty("kind").GetString()
                + ":"
                + item.GetProperty("target").GetProperty("id").GetString()
            )
            .Should()
            .Equal("check:jobs");
    }

    private Task<WebApplication> _StartAsync() =>
        ClusterDashboardHost.StartAsync(
            AbortToken,
            time: _time,
            membership: _membership,
            before: services =>
            {
                var checks = services.AddHealthChecks();
                _jobs.AddTo(checks, "jobs");
                _dependency.AddTo(checks, "sandbox-dependency");
                _blobs.AddTo(checks, "blobs");
            }
        );

    private static JsonElement _Module(JsonElement answer) =>
        answer
            .GetProperty("modules")
            .EnumerateArray()
            .Single(static module =>
                string.Equals(module.GetProperty("id").GetString(), "cluster", StringComparison.Ordinal)
            );

    private static List<JsonElement> _Items(JsonElement answer) =>
        [.. _Module(answer).GetProperty("items").EnumerateArray()];
}
