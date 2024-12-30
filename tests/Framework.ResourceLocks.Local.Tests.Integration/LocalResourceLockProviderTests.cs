using Framework.Abstractions;
using Framework.ResourceLocks;
using Framework.ResourceLocks.Local;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class LocalResourceLockProviderTests(ITestOutputHelper output) : ResourceLockProviderTestsBase(output)
{
    protected override IResourceLockProvider GetLockProvider()
    {
        var option = new ResourceLockOptions { KeyPrefix = "test:" };
        var optionWrapper = new OptionsWrapper<ResourceLockOptions>(option);

        return new LocalResourceLockProvider(
            new SnowflakeIdLongIdGenerator(1),
            TimeProvider.System,
            LoggerFactory.CreateLogger<LocalResourceLockProvider>(),
            optionWrapper
        );
    }

    [Fact]
    public override Task should_lock_with_try_acquire()
    {
        return base.should_lock_with_try_acquire();
    }

    [Fact]
    public override Task should_not_acquire_when_already_locked()
    {
        return base.should_not_acquire_when_already_locked();
    }

    [Fact]
    public override Task should_obtain_multiple_locks()
    {
        return base.should_obtain_multiple_locks();
    }

    [Fact]
    public override Task should_acquire_and_release_locks_async()
    {
        return base.should_acquire_and_release_locks_async();
    }

    [Fact]
    public override Task should_acquire_locks_in_parallel()
    {
        return base.should_acquire_locks_in_parallel();
    }

    [Fact]
    public override Task should_release_lock_multiple_times()
    {
        return base.should_release_lock_multiple_times();
    }

    [Fact]
    public override Task should_timeout_when_try_to_lock_acquired_resource()
    {
        return base.should_timeout_when_try_to_lock_acquired_resource();
    }

    [Fact]
    public override Task should_lock_one_at_a_time_async()
    {
        return base.should_lock_one_at_a_time_async();
    }
}
