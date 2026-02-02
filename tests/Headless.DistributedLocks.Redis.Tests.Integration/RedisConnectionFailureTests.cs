// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks.Redis;
using Headless.Redis;
using Headless.Testing.Tests;
using StackExchange.Redis;

namespace Tests;

/// <summary>
/// Tests for Redis connection failure handling in distributed locks.
/// </summary>
public sealed class RedisConnectionFailureTests : TestBase
{
    [Fact]
    public async Task should_throw_when_redis_unavailable_on_insert()
    {
        // given
        var options = ConfigurationOptions.Parse("localhost:59999"); // Non-existent Redis
        options.AbortOnConnectFail = false;
        options.ConnectTimeout = 100;
        options.SyncTimeout = 100;

        var multiplexer = await ConnectionMultiplexer.ConnectAsync(options);
        var scriptsLoader = new HeadlessRedisScriptsLoader(multiplexer);
        var storage = new RedisResourceLockStorage(multiplexer, scriptsLoader);

        var key = $"lock:{Faker.Random.AlphaNumeric(10)}";
        var lockId = Guid.NewGuid().ToString("N");

        // when
        var act = () => storage.InsertAsync(key, lockId, TimeSpan.FromMinutes(5)).AsTask();

        // then
        await act.Should().ThrowAsync<RedisConnectionException>();

        await multiplexer.DisposeAsync();
    }

    [Fact]
    public async Task should_throw_when_redis_unavailable_on_remove()
    {
        // given
        var options = ConfigurationOptions.Parse("localhost:59999"); // Non-existent Redis
        options.AbortOnConnectFail = false;
        options.ConnectTimeout = 100;
        options.SyncTimeout = 100;

        var multiplexer = await ConnectionMultiplexer.ConnectAsync(options);
        var scriptsLoader = new HeadlessRedisScriptsLoader(multiplexer);
        var storage = new RedisResourceLockStorage(multiplexer, scriptsLoader);

        var key = $"lock:{Faker.Random.AlphaNumeric(10)}";
        var lockId = Guid.NewGuid().ToString("N");

        // when
        var act = () => storage.RemoveIfEqualAsync(key, lockId).AsTask();

        // then
        await act.Should().ThrowAsync<RedisException>();

        await multiplexer.DisposeAsync();
    }

    [Fact]
    public async Task should_throw_when_redis_unavailable_on_throttling_increment()
    {
        // given
        var options = ConfigurationOptions.Parse("localhost:59999"); // Non-existent Redis
        options.AbortOnConnectFail = false;
        options.ConnectTimeout = 100;
        options.SyncTimeout = 100;

        var multiplexer = await ConnectionMultiplexer.ConnectAsync(options);
        var scriptsLoader = new HeadlessRedisScriptsLoader(multiplexer);
        var storage = new RedisThrottlingResourceLockStorage(multiplexer, scriptsLoader);

        var resource = $"throttle:{Faker.Random.AlphaNumeric(10)}";

        // when
        var act = () => storage.IncrementAsync(resource, TimeSpan.FromMinutes(5));

        // then
        await act.Should().ThrowAsync<RedisException>();

        await multiplexer.DisposeAsync();
    }
}
