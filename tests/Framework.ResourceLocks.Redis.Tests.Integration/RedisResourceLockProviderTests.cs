using Framework.Abstractions;
using Framework.Messaging;
using Framework.ResourceLocks;
using Framework.ResourceLocks.Storage.RegularLocks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tests.TestSetup;

namespace Tests;

[Collection(nameof(RedisTestFixture))]
public sealed class RedisResourceLockProviderTests(RedisTestFixture fixture, ITestOutputHelper output)
    : ResourceLockProviderTestsBase(output)
{
#pragma warning disable CA2213 // Disposable fields should be disposed
    private readonly IMessageBus _messageBus = Substitute.For<IMessageBus>();
#pragma warning restore CA2213

    protected override IResourceLockProvider GetLockProvider()
    {
        var option = new ResourceLockOptions { KeyPrefix = "test:" };
        var optionWrapper = new OptionsWrapper<ResourceLockOptions>(option);

        return new StorageResourceLockProvider(
            fixture.ResourceLockStorage,
            _messageBus,
            new SnowflakeIdLongIdGenerator(1),
            TimeProvider.System,
            LoggerFactory.CreateLogger<StorageResourceLockProvider>(),
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
    public override async Task should_release_lock_multiple_times()
    {
        await base.should_release_lock_multiple_times();
        await _messageBus.Received().PublishAsync(Arg.Any<Arg.AnyType>());
    }

    [Fact]
    public override Task should_timeout_when_try_to_lock_acquired_resource()
    {
        return base.should_timeout_when_try_to_lock_acquired_resource();
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
    public override Task should_lock_one_at_a_time_async()
    {
        return base.should_lock_one_at_a_time_async();
    }
}
