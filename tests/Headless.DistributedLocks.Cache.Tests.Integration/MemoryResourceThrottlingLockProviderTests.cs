using Headless.Caching;
using Headless.DistributedLocks;
using Headless.DistributedLocks.Cache;

namespace Tests;

public sealed class MemoryResourceThrottlingLockProviderTests : ResourceThrottlingLockProviderTestsBase
{
    private readonly InMemoryCachingFoundatioAdapter _cache;

    public MemoryResourceThrottlingLockProviderTests()
    {
        _cache = new InMemoryCachingFoundatioAdapter(TimeProvider, new InMemoryCacheOptions());
    }

    protected override IThrottlingResourceLockStorage GetLockStorage()
    {
        return new CacheThrottlingResourceLockStorage(_cache);
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

    [Fact(Skip = "In-memory cache does not support concurrent operations as it is not thread-safe.")]
    public override Task should_throttle_concurrent_calls_async()
    {
        return base.should_throttle_concurrent_calls_async();
    }
}
