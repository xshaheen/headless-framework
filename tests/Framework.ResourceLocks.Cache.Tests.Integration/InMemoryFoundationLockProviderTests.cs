using Foundatio.Messaging;
using Framework.Caching;
using Framework.Messaging;
using Framework.ResourceLocks;
using Framework.ResourceLocks.Cache;
using Microsoft.Extensions.Logging;

namespace Tests;

public class InMemoryFoundationLockProviderTests : ResourceLockProviderTestsBase
{
    private readonly InMemoryMessageBus _foundatioMessageBus;
    private readonly MessageBusFoundatioAdapter _messageBus;
    private readonly InMemoryCachingFoundatioAdapter _cache;
    private readonly CacheResourceLockStorage _storage;

    public InMemoryFoundationLockProviderTests(ITestOutputHelper output)
        : base(output)
    {
        _foundatioMessageBus = new InMemoryMessageBus(o => o.Topic("test-lock").LoggerFactory(LoggerFactory));
        _messageBus = new(_foundatioMessageBus, GuidGenerator);
        _cache = new InMemoryCachingFoundatioAdapter(TimeProvider, new InMemoryCacheOptions());
        _storage = new CacheResourceLockStorage(_cache);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _foundatioMessageBus?.Dispose();
            _messageBus?.Dispose();
            _cache?.Dispose();
        }
    }

    protected override IResourceLockProvider GetLockProvider()
    {
        return new ResourceLockProvider(
            _storage,
            _messageBus,
            Options,
            LongGenerator,
            TimeProvider,
            LoggerFactory.CreateLogger<ResourceLockProvider>()
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
    public override Task should_acquire_locks_in_sync()
    {
        return base.should_acquire_locks_in_sync();
    }

    [Fact]
    public override Task should_acquire_locks_in_parallel()
    {
        return base.should_acquire_locks_in_parallel();
    }
}
