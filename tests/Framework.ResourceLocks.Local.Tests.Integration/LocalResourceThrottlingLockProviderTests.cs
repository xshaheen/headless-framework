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

        return new LocalResourceThrottlingLockProvider(
            TimeProvider.System,
            options,
            LoggerFactory.CreateLogger<LocalResourceThrottlingLockProvider>()
        );
    }
}
