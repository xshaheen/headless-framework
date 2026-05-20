// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.RateLimiting;
using Headless.Redis;

namespace Tests;

[Collection<RedisTestFixture>]
public sealed class RedisDistributedRateLimiterLeaseProviderTests(RedisTestFixture fixture)
    : DistributedRateLimiterTestsBase
{
    protected override IDistributedRateLimiterStorage GetRateLimiterStorage()
    {
        return fixture.RateLimiterStorage;
    }

    public override async ValueTask InitializeAsync()
    {
        await fixture.ConnectionMultiplexer.FlushAllAsync();
    }

    [Fact]
    public override Task should_rate_limit_calls_async()
    {
        return base.should_rate_limit_calls_async();
    }

    [Fact]
    public override Task should_rate_limit_concurrent_calls_async()
    {
        return base.should_rate_limit_concurrent_calls_async();
    }
}
