using Framework.ResourceLocks;
using Framework.ResourceLocks.Local;

namespace Tests;

public sealed class LocalThrottlingResourceLockProviderTests(ITestOutputHelper output)
    : ResourceThrottlingLockProviderTestsBase(output)
{
    protected override IThrottlingResourceLockStorage GetLockStorage()
    {
        return new LocalThrottlingResourceLockStorage();
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
