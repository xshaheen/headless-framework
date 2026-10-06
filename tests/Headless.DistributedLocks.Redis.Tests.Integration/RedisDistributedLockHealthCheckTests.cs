// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Tests;

[Collection<RedisTestFixture>]
public sealed class RedisDistributedLockHealthCheckTests(RedisTestFixture fixture) : TestBase
{
    [Fact]
    public async Task should_report_redis_distributed_locks_healthy()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConnectionMultiplexer>(fixture.ConnectionMultiplexer);
        services.AddHeadlessDistributedLocks(setup => setup.UseRedis());
        await using var provider = services.BuildServiceProvider();

        // when
        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync(AbortToken);

        // then
        var entry = report.Entries["distributed-locks-redis"];
        entry.Status.Should().Be(HealthStatus.Healthy);
        entry.Tags.Should().Contain([HeadlessHealthCheckTags.Ready, HeadlessHealthCheckTags.Redis]);
    }
}
