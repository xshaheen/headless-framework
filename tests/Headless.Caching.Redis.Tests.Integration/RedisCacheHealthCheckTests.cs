// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Caching;
using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Tests;

[Collection(nameof(RedisCacheFixture))]
public sealed class RedisCacheHealthCheckTests(RedisCacheFixture fixture) : TestBase
{
    [Fact]
    public async Task should_report_default_and_named_redis_caches_healthy()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessCaching(setup =>
        {
            setup.UseRedis(options => options.ConnectionMultiplexer = fixture.ConnectionMultiplexer);
            setup.AddNamed(
                "tenant",
                instance => instance.UseRedis(options => options.ConnectionMultiplexer = fixture.ConnectionMultiplexer)
            );
        });
        await using var provider = services.BuildServiceProvider();

        // when
        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync(AbortToken);

        // then
        report.Entries["cache-redis"].Status.Should().Be(HealthStatus.Healthy);
        report.Entries["cache-redis-tenant"].Status.Should().Be(HealthStatus.Healthy);
        report.Entries["cache-redis"].Tags.Should().Contain(HeadlessHealthCheckTags.Redis);
    }

    [Fact]
    public async Task should_report_the_configured_failure_status_when_an_endpoint_is_unreachable()
    {
        // given - one reachable endpoint and one where nothing listens: a single PING could pass, the endpoint probe must not
        var reachable = fixture.ConnectionMultiplexer.GetEndPoints(configuredOnly: true)[0];
        var options = new ConfigurationOptions
        {
            EndPoints = { reachable, new IPEndPoint(IPAddress.Loopback, 1) },
            AbortOnConnectFail = false,
            ConnectTimeout = 500,
        };
        await using var multiplexer = await ConnectionMultiplexer.ConnectAsync(options);

        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureHeadlessHealthCheck("cache-redis", health => health.FailureStatus = HealthStatus.Degraded);
        services.AddHeadlessCaching(setup => setup.UseRedis(redis => redis.ConnectionMultiplexer = multiplexer));
        await using var provider = services.BuildServiceProvider();

        // when
        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync(AbortToken);

        // then
        var entry = report.Entries["cache-redis"];
        entry.Status.Should().Be(HealthStatus.Degraded);
        entry.Description.Should().Be("The 'cache-redis' dependency probe failed.");
    }
}
