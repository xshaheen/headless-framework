using Framework.ResourceLocks;
using Framework.ResourceLocks.Storage.RegularLocks;
using Framework.ResourceLocks.Storage.ThrottlingLocks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tests.TestSetup;

namespace Tests;

[Collection(nameof(ResourceLockTestFixture))]
public sealed class LocalResourceThrottlingLockProviderTests(ResourceLockTestFixture fixture, ITestOutputHelper output)
    : ResourceThrottlingLockProviderTestsBase(output)
{
    protected override IResourceThrottlingLockProvider GetLockProvider(int maxHits, TimeSpan period)
    {
        var option = new ThrottlingResourceLockOptions
        {
            KeyPrefix = "storage:",
            ThrottlingPeriod = period,
            MaxHitsPerPeriod = maxHits,
        };

        var optionWrapper = new OptionsWrapper<ThrottlingResourceLockOptions>(option);

        return new StorageThrottlingResourceLockProvider(
            fixture.ThrottlingResourceLockStorage,
            TimeProvider.System,
            LoggerFactory.CreateLogger<StorageResourceLockProvider>(),
            optionWrapper
        );
    }

    [Fact]
    public override Task should_throttle_calls_async()
    {
        return base.should_throttle_calls_async();
    }
}
