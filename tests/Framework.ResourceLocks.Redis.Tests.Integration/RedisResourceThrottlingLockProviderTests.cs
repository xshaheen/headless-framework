using Framework.Redis;
using Framework.ResourceLocks;

namespace Tests;

[Collection<RedisTestFixture>]
public sealed class RedisResourceThrottlingLockProviderTests(RedisTestFixture fixture)
    : ResourceThrottlingLockProviderTestsBase
{
    protected override IThrottlingResourceLockStorage GetLockStorage()
    {
        return fixture.ThrottlingLockStorage;
    }

    public override async ValueTask InitializeAsync()
    {
        await fixture.ConnectionMultiplexer.FlushAllAsync();
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
