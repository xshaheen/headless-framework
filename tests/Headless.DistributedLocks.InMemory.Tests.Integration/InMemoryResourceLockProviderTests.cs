using Headless.Caching;
using Headless.DistributedLocks;
using Headless.DistributedLocks.Cache;
using Headless.Messaging;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Tests;

public sealed class InMemoryDistributedLockProviderTests : DistributedLockProviderTestsBase
{
    private readonly InMemoryCache _cache = new(TimeProvider.System, new InMemoryCacheOptions());

    protected override IDistributedLockProvider GetLockProvider()
    {
        var storage = new CacheDistributedLockStorage(_cache);
        var outboxPublisher = Substitute.For<IOutboxPublisher>();
        return new DistributedLockProvider(
            storage,
            outboxPublisher,
            Options,
            LongGenerator,
            TimeProvider,
            LoggerFactory.CreateLogger<DistributedLockProvider>()
        );
    }

    protected override ValueTask DisposeAsyncCore()
    {
        _cache.Dispose();
        return base.DisposeAsyncCore();
    }

    [Fact]
    public override Task should_lock_with_try_acquire() => base.should_lock_with_try_acquire();

    [Fact]
    public override Task should_not_acquire_when_already_locked() => base.should_not_acquire_when_already_locked();

    [Fact]
    public override Task should_obtain_multiple_locks() => base.should_obtain_multiple_locks();

    [Fact]
    public override Task should_release_lock_multiple_times() => base.should_release_lock_multiple_times();

    [Fact]
    public override Task should_timeout_when_try_to_lock_acquired_resource() =>
        base.should_timeout_when_try_to_lock_acquired_resource();

    [Fact]
    public override Task should_acquire_and_release_locks_async() => base.should_acquire_and_release_locks_async();

    [Fact(Skip = "In-memory cache does not support concurrent operations as it is not thread-safe.")]
    public override Task should_acquire_one_at_a_time_parallel() => base.should_acquire_one_at_a_time_parallel();

    [Fact]
    public override Task should_acquire_locks_in_sync() => base.should_acquire_locks_in_sync();

    [Fact(Skip = "In-memory cache does not support concurrent operations as it is not thread-safe.")]
    public override Task should_acquire_locks_in_parallel() => base.should_acquire_locks_in_parallel();

    [Fact(Skip = "In-memory cache does not support concurrent operations as it is not thread-safe.")]
    public override Task should_lock_one_at_a_time_async() => base.should_lock_one_at_a_time_async();

    [Fact]
    public override Task should_get_expiration_for_locked_resource() =>
        base.should_get_expiration_for_locked_resource();

    [Fact]
    public override Task should_return_null_expiration_when_not_locked() =>
        base.should_return_null_expiration_when_not_locked();

    [Fact]
    public override Task should_get_lock_info_for_locked_resource() => base.should_get_lock_info_for_locked_resource();

    [Fact]
    public override Task should_return_null_lock_info_when_not_locked() =>
        base.should_return_null_lock_info_when_not_locked();

    [Fact]
    public override Task should_list_active_locks() => base.should_list_active_locks();

    [Fact]
    public override Task should_get_active_locks_count() => base.should_get_active_locks_count();
}
