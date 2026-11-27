// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Cluster;
using Headless.Dashboard;
using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>
/// <c>AddCluster()</c> on a real host: which host shapes start, when it registers the health check monitor itself,
/// and how each overload binds the module's options.
/// </summary>
public sealed class ClusterRegistrationTests : TestBase
{
    [Fact]
    public async Task should_start_and_register_the_monitor_when_health_checks_come_before_add_cluster()
    {
        // given a host with health checks registered first and no Coordination
        await using var app = await ClusterDashboardHost.StartAsync(
            AbortToken,
            before: static services =>
                services.AddHealthChecks().AddCheck("db", static () => HealthCheckResult.Healthy())
        );

        // then
        app.Services.GetService<IHealthCheckMonitor>().Should().NotBeNull();
    }

    [Fact]
    public async Task should_fail_startup_naming_both_remedies_when_the_host_serves_neither_half()
    {
        // when
        var start = () => ClusterDashboardHost.StartAsync(AbortToken);

        // then
        var failure = await start.Should().ThrowAsync<InvalidOperationException>();
        failure.Which.Message.Should().Contain("AddHeadlessCoordination").And.Contain("AddHealthChecks");
    }

    [Fact]
    public async Task should_start_without_a_monitor_when_only_coordination_is_registered()
    {
        // given
        await using var app = await ClusterDashboardHost.StartAsync(AbortToken, membership: new FakeMembership());

        // then
        app.Services.GetService<IHealthCheckMonitor>().Should().BeNull();
    }

    [Fact]
    public async Task should_start_and_register_the_monitor_when_health_checks_come_after_add_cluster()
    {
        // given a host that registers its health checks only after the module
        await using var app = await ClusterDashboardHost.StartAsync(
            AbortToken,
            after: static services =>
                services.AddHealthChecks().AddCheck("db", static () => HealthCheckResult.Healthy())
        );

        // then
        app.Services.GetService<HealthCheckService>().Should().NotBeNull();
        app.Services.GetService<IHealthCheckMonitor>().Should().NotBeNull();
    }

    [Fact]
    public async Task should_let_an_explicit_monitor_registration_after_add_cluster_keep_its_alive_tag()
    {
        // given
        await using var app = await ClusterDashboardHost.StartAsync(
            AbortToken,
            after: static services =>
            {
                services
                    .AddHealthChecks()
                    .AddCheck("custom-live", static () => HealthCheckResult.Healthy(), tags: ["heartbeat"]);
                services.AddHeadlessHealthCheckMonitor(aliveTag: "heartbeat");
            }
        );

        // then
        var monitor = app.Services.GetRequiredService<IHealthCheckMonitor>();
        monitor.GetChecks().Should().ContainSingle(static c => c.Name == "custom-live" && c.IsLiveness);
    }

    public static TheoryData<string> Overloads => ["action", "action-with-provider", "configuration"];

    [Theory]
    [MemberData(nameof(Overloads))]
    public async Task should_bind_the_view_policy_through_every_overload(string overload)
    {
        // given
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal) { ["ViewPolicy"] = "ClusterViewers" }
            )
            .Build();

        await using var app = await ClusterDashboardHost.StartAsync(
            AbortToken,
            membership: new FakeMembership(),
            addCluster: dashboard =>
                overload switch
                {
                    "action" => dashboard.AddCluster(static options => options.ViewPolicy = "ClusterViewers"),
                    "action-with-provider" => dashboard.AddCluster(
                        static (options, provider) =>
                        {
                            provider.Should().NotBeNull();
                            options.ViewPolicy = "ClusterViewers";
                        }
                    ),
                    _ => dashboard.AddCluster(configuration),
                }
        );

        // then
        app.Services.GetRequiredService<IOptions<ClusterDashboardOptions>>()
            .Value.ViewPolicy.Should()
            .Be("ClusterViewers");
    }

    [Fact]
    public async Task should_keep_the_default_options_with_the_parameterless_overload()
    {
        // given
        await using var app = await ClusterDashboardHost.StartAsync(AbortToken, membership: new FakeMembership());

        // then
        var options = app.Services.GetRequiredService<IOptions<ClusterDashboardOptions>>().Value;
        options.ViewPolicy.Should().BeNull();
        options.ChangePageSize.Should().Be(200);
    }

    [Fact]
    public async Task should_fail_startup_on_a_white_space_view_policy()
    {
        // when
        var start = () =>
            ClusterDashboardHost.StartAsync(
                AbortToken,
                membership: new FakeMembership(),
                addCluster: static dashboard => dashboard.AddCluster(static options => options.ViewPolicy = "  ")
            );

        // then
        (await start.Should().ThrowAsync<Exception>())
            .Which.ToString()
            .Should()
            .Contain("ViewPolicy");
    }

    [Fact]
    public async Task should_fail_startup_on_a_change_page_size_of_zero()
    {
        // when
        var start = () =>
            ClusterDashboardHost.StartAsync(
                AbortToken,
                membership: new FakeMembership(),
                addCluster: static dashboard => dashboard.AddCluster(static options => options.ChangePageSize = 0)
            );

        // then
        (await start.Should().ThrowAsync<Exception>())
            .Which.ToString()
            .Should()
            .Contain("ChangePageSize");
    }

    [Fact]
    public async Task should_serve_the_module_as_the_cluster_product()
    {
        // given
        await using var app = await ClusterDashboardHost.StartAsync(AbortToken, membership: new FakeMembership());

        // when
        var counts = await ClusterDashboardHost.GetJsonAsync(app, "/headless/api/counts?product=cluster", AbortToken);

        // then
        var module = app.Services.GetRequiredService<ClusterModule>();
        module.Id.Should().Be("cluster");
        module.DisplayName.Should().Be("Cluster");
        counts.GetProperty("product").GetString().Should().Be("cluster");
    }
}
