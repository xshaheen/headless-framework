using Framework.ResourceLocks;
using Framework.ResourceLocks.Local;
using Microsoft.Extensions.Logging;

namespace Tests;

public sealed class LocalResourceThrottlingLockProviderTests(ITestOutputHelper output)
    : ResourceThrottlingLockProviderTestsBase(output)
{
    protected override IResourceThrottlingLockProvider GetLockProvider(int maxHits, TimeSpan period)
    {
        var options = new ThrottlingResourceLockOptions
        {
            ThrottlingPeriod = period,
            MaxHitsPerPeriod = maxHits,
            KeyPrefix = string.Empty,
        };

        return new ResourceThrottlingLockProvider(
#pragma warning disable CA2000 // It already disposed inside
            new LocalResourceThrottlingLockStorage(),
#pragma warning restore CA2000
            options,
            TimeProvider.System,
            LoggerFactory.CreateLogger<ResourceThrottlingLockProvider>()
        );
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
