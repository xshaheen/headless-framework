using Framework.BuildingBlocks.Abstractions;
using Framework.ResourceLocks;
using Framework.ResourceLocks.Local;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class LocalResourceLockProviderTests : ResourceLockProviderTestsBase
{
    protected override IResourceLockProvider GetLockProvider()
    {
        var logger = Substitute.For<ILogger<LocalResourceLockProvider>>();
        var option = new ResourceLockOptions { KeyPrefix = "test:" };
        var optionWrapper = new OptionsWrapper<ResourceLockOptions>(option);

        return new LocalResourceLockProvider(
            new SnowFlakIdUniqueLongGenerator(1),
            TimeProvider.System,
            logger,
            optionWrapper
        );
    }

    [Fact]
    public override Task CanAcquireAndReleaseLockAsync()
    {
        return base.CanAcquireAndReleaseLockAsync();
    }
}
