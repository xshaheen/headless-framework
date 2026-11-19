// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Tests;

/// <summary>
/// Hosts that share one Redis with different <c>Headless:Prefix</c> values hold locks on the same resource name
/// independently, while hosts with the same prefix still exclude each other.
/// </summary>
[Collection<RedisTestFixture>]
public sealed class RedisDistributedLockHeadlessPrefixTests(RedisTestFixture fixture) : TestBase
{
    private static readonly DistributedLockAcquireOptions _NoWait = new() { AcquireTimeout = TimeSpan.Zero };

    [Fact]
    public async Task should_let_two_prefixed_hosts_hold_the_same_resource_at_once()
    {
        // given
        var resource = "orders-" + Faker.Random.AlphaNumeric(8);
        await using var first = _BuildProvider("env" + Faker.Random.AlphaNumeric(8).ToLowerInvariant());
        await using var second = _BuildProvider("env" + Faker.Random.AlphaNumeric(8).ToLowerInvariant());

        // when
        await using var firstLock = await first
            .GetRequiredService<IDistributedLock>()
            .TryAcquireAsync(resource, _NoWait, AbortToken);
        await using var secondLock = await second
            .GetRequiredService<IDistributedLock>()
            .TryAcquireAsync(resource, _NoWait, AbortToken);

        // then
        firstLock.Should().NotBeNull();
        secondLock.Should().NotBeNull();
    }

    [Fact]
    public async Task should_still_exclude_hosts_that_share_a_prefix()
    {
        // given
        var resource = "orders-" + Faker.Random.AlphaNumeric(8);
        var prefix = "env" + Faker.Random.AlphaNumeric(8).ToLowerInvariant();
        await using var first = _BuildProvider(prefix);
        await using var second = _BuildProvider(prefix);

        // when
        await using var firstLock = await first
            .GetRequiredService<IDistributedLock>()
            .TryAcquireAsync(resource, _NoWait, AbortToken);
        await using var secondLock = await second
            .GetRequiredService<IDistributedLock>()
            .TryAcquireAsync(resource, _NoWait, AbortToken);

        // then
        firstLock.Should().NotBeNull();
        secondLock.Should().BeNull();
    }

    private ServiceProvider _BuildProvider(string prefix)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConnectionMultiplexer>(fixture.ConnectionMultiplexer);
        services.ConfigureHeadlessPrefix(prefix);
        services.AddHeadlessDistributedLocks(setup => setup.UseRedis());

        return services.BuildServiceProvider();
    }
}
