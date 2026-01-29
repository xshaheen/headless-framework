// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Redis;
using Headless.Serializer;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;

namespace Tests;

[Collection(nameof(RedisCacheFixture))]
public abstract class RedisCacheTestBase(RedisCacheFixture fixture) : TestBase
{
    protected RedisCacheFixture Fixture { get; } = fixture;

    protected RedisCache CreateCache(string? keyPrefix = null)
    {
        var options = new RedisCacheOptions
        {
            ConnectionMultiplexer = Fixture.ConnectionMultiplexer,
            KeyPrefix = keyPrefix ?? "",
        };

        var logger = LoggerFactory.CreateLogger<RedisCache>();
        return new RedisCache(new SystemJsonSerializer(), TimeProvider.System, options, logger);
    }

    protected async Task FlushAsync()
    {
        await Fixture.ConnectionMultiplexer.FlushAllAsync();
    }
}
