// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

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
}
