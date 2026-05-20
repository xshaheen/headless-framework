// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.RateLimiting;
using Headless.RateLimiting.Cache;
using Headless.Redis;
using Headless.Serializer;
using Tests.TestSetup;

namespace Tests;

[Collection<CacheTestFixture>]
public sealed class RedisDistributedRateLimiterLeaseProviderTests(CacheTestFixture fixture)
    : DistributedRateLimiterTestsBase
{
    protected override IDistributedRateLimiterStorage GetRateLimiterStorage()
    {
        var cache = new RedisCache(
            new SystemJsonSerializer(),
            TimeProvider,
            new RedisCacheOptions { ConnectionMultiplexer = fixture.ConnectionMultiplexer },
            fixture.ScriptsLoader
        );

        return new CacheDistributedRateLimiterStorage(cache);
    }

    public override async ValueTask InitializeAsync()
    {
        await fixture.ConnectionMultiplexer.FlushAllAsync();
    }

    [Fact]
    public override Task should_rate_limit_calls_async() => base.should_rate_limit_calls_async();

    [Fact]
    public override Task should_rate_limit_concurrent_calls_async() => base.should_rate_limit_concurrent_calls_async();
}
