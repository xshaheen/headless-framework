using Framework.Caching;
using Framework.ResourceLocks;
using Framework.ResourceLocks.Cache;

namespace Tests;

public sealed class MemoryResourceThrottlingLockProviderTests : ResourceThrottlingLockProviderTestsBase
{
    private readonly InMemoryCachingFoundatioAdapter _cache;

    public MemoryResourceThrottlingLockProviderTests(ITestOutputHelper output)
        : base(output)
    {
        _cache = new InMemoryCachingFoundatioAdapter(TimeProvider, new InMemoryCacheOptions());
    }

    protected override IThrottlingResourceLockStorage GetLockStorage()
    {
        return new CacheThrottlingResourceLockStorage(_cache);
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

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _cache.Dispose();
        }
    }
}
