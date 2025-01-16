using Framework.Redis;
using Framework.ResourceLocks;
using Tests.TestSetup;

namespace Tests;

[Collection(nameof(RedisTestFixture))]
public sealed class RedisResourceThrottlingLockProviderTests(RedisTestFixture fixture, ITestOutputHelper output)
    : ResourceThrottlingLockProviderTestsBase(output),
        IAsyncLifetime
{
    protected override IThrottlingResourceLockStorage GetLockStorage()
    {
        return fixture.ThrottlingLockStorage;
    }

    public async Task InitializeAsync()
    {
        await fixture.ConnectionMultiplexer.FlushAllAsync();
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
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
