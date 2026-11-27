// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace Tests.HealthChecks;

public sealed class HealthCheckMonitorRegistrationTests : TestBase
{
    [Fact]
    public void should_resolve_the_monitor_and_the_health_check_service()
    {
        // given
        var services = _Services();

        // when
        services.AddHeadlessHealthCheckMonitor();
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetService<IHealthCheckMonitor>().Should().NotBeNull();
        provider.GetService<HealthCheckService>().Should().NotBeNull();
    }

    [Fact]
    public void should_add_no_hosted_service_or_publisher_beyond_the_health_checks_defaults()
    {
        // given
        var bare = _Services();
        bare.AddHealthChecks();
        var monitored = _Services();

        // when
        monitored.AddHeadlessHealthCheckMonitor();

        // then
        _HostedServiceTypes(monitored).Should().Equal(_HostedServiceTypes(bare));
        monitored.Should().NotContain(static descriptor => descriptor.ServiceType == typeof(IHealthCheckPublisher));
    }

    [Fact]
    public void should_run_no_check_when_the_monitor_resolves()
    {
        // given
        var runs = 0;
        var services = _Services();
        services
            .AddHealthChecks()
            .AddCheck(
                "counting",
                () =>
                {
                    Interlocked.Increment(ref runs);

                    return HealthCheckResult.Healthy();
                }
            );
        services.AddHeadlessHealthCheckMonitor();
        using var provider = services.BuildServiceProvider();

        // when
        var monitor = provider.GetRequiredService<IHealthCheckMonitor>();
        _ = monitor.GetChecks();
        _ = monitor.GetSnapshot();

        // then
        runs.Should().Be(0);
    }

    [Fact]
    public void should_keep_the_first_registration_when_called_twice()
    {
        // given
        var services = _Services();
        services.AddHealthChecks().AddCheck("pulse", static () => HealthCheckResult.Healthy(), tags: ["heartbeat"]);

        // when
        services.AddHeadlessHealthCheckMonitor("heartbeat");
        services.AddHeadlessHealthCheckMonitor("live");
        using var provider = services.BuildServiceProvider();

        // then
        services.Count(static descriptor => descriptor.ServiceType == typeof(IHealthCheckMonitor)).Should().Be(1);
        provider.GetRequiredService<IHealthCheckMonitor>().GetChecks().Single().IsLiveness.Should().BeTrue();
    }

    [Fact]
    public async Task should_not_record_a_health_check_run_that_bypasses_the_monitor()
    {
        // given: health endpoints and the check command call the health check service directly
        var services = _Services();
        services.AddHealthChecks().AddCheck("a", static () => HealthCheckResult.Healthy());
        services.AddHeadlessHealthCheckMonitor();
        await using var provider = services.BuildServiceProvider();
        var monitor = provider.GetRequiredService<IHealthCheckMonitor>();

        // when
        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync(AbortToken);

        // then
        report.Entries.Should().ContainKey("a");
        var snapshot = monitor.GetSnapshot();
        snapshot.Status.Should().BeNull();
        snapshot.Checks.Single().Results.Should().BeEmpty();
    }

    [Fact]
    public void should_reject_a_blank_alive_tag()
    {
        // given
        var services = _Services();

        // when
        var act = () => services.AddHeadlessHealthCheckMonitor(" ");

        // then
        act.Should().Throw<ArgumentException>();
    }

    private static ServiceCollection _Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        return services;
    }

    private static Type?[] _HostedServiceTypes(IServiceCollection services)
    {
        return
        [
            .. services
                .Where(static descriptor => descriptor.ServiceType == typeof(IHostedService))
                .Select(static descriptor => descriptor.ImplementationType),
        ];
    }
}
