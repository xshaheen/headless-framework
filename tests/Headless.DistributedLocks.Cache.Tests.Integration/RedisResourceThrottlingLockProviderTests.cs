using Headless.Caching;
using Headless.DistributedLocks;
using Headless.DistributedLocks.Cache;
using Headless.Redis;
using Headless.Serializer;
using Tests.TestSetup;

namespace Tests;

[Collection<CacheTestFixture>]
public sealed class RedisResourceThrottlingLockProviderTests(CacheTestFixture fixture)
    : ResourceThrottlingLockProviderTestsBase
{
    protected override IThrottlingResourceLockStorage GetLockStorage()
    {
        var cache = new RedisCache(
            new SystemJsonSerializer(),
            TimeProvider,
            new RedisCacheOptions { ConnectionMultiplexer = fixture.ConnectionMultiplexer },
            fixture.ScriptsLoader
        );

        return new CacheThrottlingResourceLockStorage(cache);
    }

    public override async ValueTask InitializeAsync()
    {
        await fixture.ConnectionMultiplexer.FlushAllAsync();
    }

    [Fact]
    public override Task should_throttle_calls_async() => base.should_throttle_calls_async();

    [Fact]
    public override Task should_throttle_concurrent_calls_async() => base.should_throttle_concurrent_calls_async();
}
