// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.RateLimiting;
using Headless.RateLimiting.Cache;

namespace Tests;

public sealed class MemoryDistributedRateLimiterLeaseProviderTests : DistributedRateLimiterTestsBase
{
    private readonly InMemoryCache _cache = new(TimeProvider.System, new InMemoryCacheOptions());

    protected override IDistributedRateLimiterStorage GetRateLimiterStorage() =>
        new CacheDistributedRateLimiterStorage(_cache);

    protected override ValueTask DisposeAsyncCore()
    {
        _cache.Dispose();
        return base.DisposeAsyncCore();
    }

    [Fact]
    public override Task should_rate_limit_calls_async() => base.should_rate_limit_calls_async();

    [Fact(Skip = "In-memory cache does not support concurrent operations as it is not thread-safe.")]
    public override Task should_rate_limit_concurrent_calls_async() => base.should_rate_limit_concurrent_calls_async();
}
