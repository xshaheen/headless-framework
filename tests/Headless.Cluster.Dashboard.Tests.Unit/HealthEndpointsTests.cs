// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Tests;

/// <summary>
/// The module's health reads on a real host: the overall health, the check list with its filter, and one check's
/// recent results. They read what probes recorded and never run a check themselves.
/// </summary>
public sealed class HealthEndpointsTests : TestBase
{
    private const string _Api = ClusterDashboardHost.Api;

    private readonly HealthToggle _dependency = new();
    private readonly HealthToggle _scheduler = new();

    [Fact]
    public async Task should_report_every_check_as_not_probed_before_the_first_probe()
    {
        // given
        await using var app = await _StartAsync();

        // when
        var health = await ClusterDashboardHost.GetJsonAsync(app, _Api + "/health", AbortToken);
        var checks = await ClusterDashboardHost.GetJsonAsync(app, _Api + "/health/checks", AbortToken);

        // then
        health.GetProperty("status").ValueKind.Should().Be(JsonValueKind.Null);
        health.GetProperty("probedAt").ValueKind.Should().Be(JsonValueKind.Null);
        health.GetProperty("slowest").ValueKind.Should().Be(JsonValueKind.Null);
        health.GetProperty("totals").GetProperty("notProbed").GetInt32().Should().Be(2);
        health.GetProperty("totals").GetProperty("healthy").GetInt32().Should().Be(0);
        var items = checks.GetProperty("items").EnumerateArray().ToList();
        items
            .Select(static item => item.GetProperty("name").GetString())
            .Should()
            .Equal("jobs-scheduler", "sandbox-dependency");
        items.Should().OnlyContain(static item => item.GetProperty("latest").ValueKind == JsonValueKind.Null);
        items[0].GetProperty("failureStatus").GetString().Should().Be("Unhealthy");
        items[0].GetProperty("isLiveness").GetBoolean().Should().BeFalse();
        items[0].GetProperty("tags").EnumerateArray().Select(static tag => tag.GetString()).Should().Equal("ready");
    }

    [Fact]
    public async Task should_report_the_worst_latest_status_and_filter_checks_by_status_after_a_probe()
    {
        // given
        _dependency.Status = HealthStatus.Degraded;
        await using var app = await _StartAsync();
        await app.Services.GetRequiredService<IHealthCheckMonitor>().ProbeAllAsync(AbortToken);

        // when
        var health = await ClusterDashboardHost.GetJsonAsync(app, _Api + "/health", AbortToken);
        var degraded = await ClusterDashboardHost.GetJsonAsync(
            app,
            _Api + "/health/checks?status=degraded",
            AbortToken
        );
        var unprobed = await ClusterDashboardHost.GetJsonAsync(
            app,
            _Api + "/health/checks?status=NotProbed",
            AbortToken
        );

        // then
        health.GetProperty("status").GetString().Should().Be("Degraded");
        health.GetProperty("probedAt").ValueKind.Should().Be(JsonValueKind.String);
        health.GetProperty("totals").GetProperty("degraded").GetInt32().Should().Be(1);
        health.GetProperty("totals").GetProperty("healthy").GetInt32().Should().Be(1);
        health.GetProperty("totals").GetProperty("notProbed").GetInt32().Should().Be(0);
        health.GetProperty("slowest").GetProperty("name").ValueKind.Should().Be(JsonValueKind.String);
        degraded
            .GetProperty("items")
            .EnumerateArray()
            .Select(static item => item.GetProperty("name").GetString())
            .Should()
            .Equal("sandbox-dependency");
        var latest = degraded.GetProperty("items")[0].GetProperty("latest");
        latest.GetProperty("status").GetString().Should().Be("Degraded");
        latest.GetProperty("description").GetString().Should().Be("sandbox-dependency reports Degraded");
        unprobed.GetProperty("items").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task should_refuse_a_status_that_is_not_a_health_status()
    {
        // given
        await using var app = await _StartAsync();
        using var client = ClusterDashboardHost.Client(app);
        var cookie = await ClusterDashboardHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var response = await ClusterDashboardHost.GetAsync(
            client,
            _Api + "/health/checks?status=Sick",
            cookie,
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ClusterDashboardHost.ReadAsync(response, AbortToken))
            .GetProperty("rules")
            .GetProperty("status")
            .GetString()
            .Should()
            .Be("enum");
    }

    [Fact]
    public async Task should_match_a_check_name_case_insensitively_and_keep_its_last_30_results_newest_first()
    {
        // given
        await using var app = await _StartAsync();
        var monitor = app.Services.GetRequiredService<IHealthCheckMonitor>();

        for (var probe = 0; probe < 31; probe++)
        {
            _dependency.Status = probe == 30 ? HealthStatus.Unhealthy : HealthStatus.Healthy;
            await monitor.ProbeAsync("sandbox-dependency", AbortToken);
        }

        using var client = ClusterDashboardHost.Client(app);
        var cookie = await ClusterDashboardHost.SignInAsync(client, "ops", AbortToken);

        // when
        var check = await ClusterDashboardHost.GetJsonAsync(
            app,
            _Api + "/health/checks/SANDBOX-DEPENDENCY",
            AbortToken
        );
        using var missing = await ClusterDashboardHost.GetAsync(
            client,
            _Api + "/health/checks/missing",
            cookie,
            AbortToken
        );

        // then
        check.GetProperty("check").GetProperty("name").GetString().Should().Be("sandbox-dependency");
        var results = check.GetProperty("results").EnumerateArray().ToList();
        results.Should().HaveCount(30);
        results[0].GetProperty("status").GetString().Should().Be("Unhealthy");
        results
            .Skip(1)
            .Should()
            .OnlyContain(static result =>
                string.Equals(result.GetProperty("status").GetString(), "Healthy", StringComparison.Ordinal)
            );
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task should_never_run_a_check_when_the_health_reads_are_served()
    {
        // given
        await using var app = await _StartAsync();

        // when
        for (var read = 0; read < 10; read++)
        {
            await ClusterDashboardHost.GetJsonAsync(app, _Api + "/health", AbortToken);
            await ClusterDashboardHost.GetJsonAsync(app, _Api + "/health/checks", AbortToken);
        }

        // then
        _dependency.Runs.Should().Be(0);
        _scheduler.Runs.Should().Be(0);
        app.Services.GetRequiredService<IHealthCheckMonitor>().GetSnapshot().Status.Should().BeNull();
    }

    [Fact]
    public async Task should_answer_409_naming_the_monitor_when_checks_are_registered_without_it()
    {
        // given
        await using var app = await ClusterDashboardHost.StartAsync(
            AbortToken,
            after: services => _dependency.AddTo(services.AddHealthChecks(), "sandbox-dependency")
        );
        using var client = ClusterDashboardHost.Client(app);
        var cookie = await ClusterDashboardHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var response = await ClusterDashboardHost.GetAsync(client, _Api + "/health", cookie, AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync(AbortToken)).Should().Contain("AddHeadlessHealthCheckMonitor");
    }

    [Fact]
    public async Task should_answer_404_naming_add_health_checks_on_a_host_without_health_checks()
    {
        // given
        await using var app = await ClusterDashboardHost.StartAsync(AbortToken, membership: new FakeMembership());
        using var client = ClusterDashboardHost.Client(app);
        var cookie = await ClusterDashboardHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var response = await ClusterDashboardHost.GetAsync(client, _Api + "/health/checks", cookie, AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync(AbortToken)).Should().Contain("AddHealthChecks");
    }

    private Task<Microsoft.AspNetCore.Builder.WebApplication> _StartAsync() =>
        ClusterDashboardHost.StartAsync(
            AbortToken,
            before: services =>
            {
                var checks = services.AddHealthChecks();
                _dependency.AddTo(checks, "sandbox-dependency");
                _scheduler.AddTo(checks, "jobs-scheduler");
            }
        );
}
