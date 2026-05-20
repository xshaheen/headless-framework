// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.RateLimiting.Redis;
using Headless.Redis;
using Headless.Testing.Tests;
using StackExchange.Redis;

namespace Tests;

/// <summary>
/// Tests for Redis connection failure handling in distributed rate limiting.
/// </summary>
public sealed class RedisConnectionFailureTests : TestBase
{
    [Fact]
    public async Task should_throw_when_redis_unavailable_on_increment()
    {
        // given
        var options = ConfigurationOptions.Parse("localhost:59999");
        options.AbortOnConnectFail = false;
        options.ConnectTimeout = 100;
        options.SyncTimeout = 100;

        var multiplexer = await ConnectionMultiplexer.ConnectAsync(options);
        var scriptsLoader = new HeadlessRedisScriptsLoader(multiplexer);
        var storage = new RedisDistributedRateLimiterStorage(multiplexer, scriptsLoader);

        var resource = $"rate-limit:{Faker.Random.AlphaNumeric(10)}";

        // when
        var act = () => storage.IncrementAsync(resource, TimeSpan.FromMinutes(5));

        // then
        await act.Should().ThrowAsync<RedisException>();

        await multiplexer.DisposeAsync();
    }

    [Fact]
    public async Task should_throw_when_redis_unavailable_on_get_hit_counts()
    {
        // given
        var options = ConfigurationOptions.Parse("localhost:59999");
        options.AbortOnConnectFail = false;
        options.ConnectTimeout = 100;
        options.SyncTimeout = 100;

        var multiplexer = await ConnectionMultiplexer.ConnectAsync(options);
        var scriptsLoader = new HeadlessRedisScriptsLoader(multiplexer);
        var storage = new RedisDistributedRateLimiterStorage(multiplexer, scriptsLoader);

        var resource = $"rate-limit:{Faker.Random.AlphaNumeric(10)}";

        // when
        var act = () => storage.GetHitCountsAsync(resource);

        // then
        await act.Should().ThrowAsync<RedisException>();

        await multiplexer.DisposeAsync();
    }
}
