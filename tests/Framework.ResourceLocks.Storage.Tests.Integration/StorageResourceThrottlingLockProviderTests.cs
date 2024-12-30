using Framework.ResourceLocks;
using Microsoft.Extensions.Logging;
using Tests.TestSetup;

namespace Tests;

[Collection(nameof(ResourceLockTestFixture))]
public sealed class StorageResourceThrottlingLockProviderTests(
    ResourceLockTestFixture fixture,
    ITestOutputHelper output
) : ResourceThrottlingLockProviderTestsBase(output)
{
    protected override IResourceThrottlingLockProvider GetLockProvider(int maxHits, TimeSpan period)
    {
        var option = new ThrottlingResourceLockOptions
        {
            KeyPrefix = "storage:",
            ThrottlingPeriod = period,
            MaxHitsPerPeriod = maxHits,
        };

        return new ResourceThrottlingLockProvider(
            fixture.ThrottlingResourceLockStorage,
            option,
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
