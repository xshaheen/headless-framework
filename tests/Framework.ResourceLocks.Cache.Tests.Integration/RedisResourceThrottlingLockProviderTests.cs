using Framework.Caching;
using Framework.Redis;
using Framework.ResourceLocks;
using Framework.ResourceLocks.Cache;
using Framework.Serializer;
using Tests.TestSetup;

namespace Tests;

[Collection<CacheTestFixture>]
public sealed class RedisResourceThrottlingLockProviderTests : ResourceThrottlingLockProviderTestsBase
{
    private readonly CacheTestFixture _fixture;
    private readonly RedisCachingFoundatioAdapter _cache;

    public RedisResourceThrottlingLockProviderTests(CacheTestFixture fixture)
    {
        _cache = new RedisCachingFoundatioAdapter(
            new SystemJsonSerializer(),
            TimeProvider,
            new RedisCacheOptions { ConnectionMultiplexer = fixture.ConnectionMultiplexer }
        );

        _fixture = fixture;
    }

    protected override IThrottlingResourceLockStorage GetLockStorage()
    {
        return new CacheThrottlingResourceLockStorage(_cache);
    }

    public override async ValueTask InitializeAsync()
    {
        await _fixture.ConnectionMultiplexer.FlushAllAsync();
    }

    protected override ValueTask DisposeAsyncCore()
    {
        _cache.Dispose();
        return base.DisposeAsyncCore();
    }

    [Fact]
    public override Task should_throttle_calls_async()
    {
        return base.should_throttle_calls_async();
    }

    [Fact]
    public override Task should_throttle_concurrent_calls_async()
    {
        return base.should_throttle_concurrent_calls_async();
    }
}
