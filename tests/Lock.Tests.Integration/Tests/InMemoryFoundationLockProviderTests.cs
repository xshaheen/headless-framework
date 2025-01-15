using Foundatio.Caching;
using Foundatio.Messaging;
using Framework.Messaging;
using Tests.Lock;
using Tests.Storage;

namespace Tests.Tests;

public class InMemoryFoundationLockProviderTests : ResourceLockProviderTestsBase
{
    private readonly InMemoryCacheClient _inMemoryCacheClient = new();
    private readonly InMemoryMessageBus _inMemoryMessageBus;
    private readonly FoundationLockStorageAdapter _inMemoryStorage;

    public InMemoryFoundationLockProviderTests(ITestOutputHelper output)
        : base(output)
    {
        _inMemoryStorage = new(_inMemoryCacheClient);
        _inMemoryMessageBus = new(builder =>
            builder.Topic("test-lock").LoggerFactory(LoggerFactory).Serializer(FoundationHelper.JsonSerializer)
        );
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _inMemoryCacheClient?.Dispose();
            _inMemoryMessageBus?.Dispose();
            _inMemoryStorage?.Dispose();
        }
    }

    protected override ILockProvider GetLockProvider()
    {
        return new CacheLockProvider(LongGenerator, _inMemoryStorage, _inMemoryMessageBus, TimeProvider, LoggerFactory);
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

    [Fact]
    public override Task should_acquire_and_release_locks_async()
    {
        return base.should_acquire_and_release_locks_async();
    }

    [Fact]
    public override Task should_acquire_one_at_a_time_parallel()
    {
        return base.should_acquire_one_at_a_time_parallel();
    }

    [Fact]
    public override Task should_acquire_locks_in_parallel()
    {
        return base.should_acquire_locks_in_parallel();
    }
}
