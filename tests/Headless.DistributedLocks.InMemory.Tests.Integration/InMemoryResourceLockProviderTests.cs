using Headless.Caching;
using Headless.DistributedLocks;
using Headless.DistributedLocks.Cache;
using Headless.Messaging;
using Microsoft.Extensions.Logging;

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
    public override Task should_lock_with_acquire() => base.should_lock_with_acquire();

    [Fact]
    public override Task should_not_acquire_when_already_locked() => base.should_not_acquire_when_already_locked();

    [Fact]
    public override Task should_throw_timeout_with_acquire_when_already_locked() =>
        base.should_throw_timeout_with_acquire_when_already_locked();

    [Fact]
    public override Task should_obtain_multiple_locks() => base.should_obtain_multiple_locks();

    [Fact]
    public override Task should_release_lock_multiple_times() => base.should_release_lock_multiple_times();

    [Fact]
    public override Task should_keep_lock_when_disposed_with_release_on_dispose_false() =>
        base.should_keep_lock_when_disposed_with_release_on_dispose_false();

    [Fact]
    public override Task should_release_explicitly_when_release_on_dispose_false() =>
        base.should_release_explicitly_when_release_on_dispose_false();

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

    [Fact]
    public override Task should_expose_none_handle_lost_token_without_monitoring() =>
        base.should_expose_none_handle_lost_token_without_monitoring();

    [Fact]
    public override Task should_cancel_handle_lost_token_after_ttl_without_auto_extend() =>
        base.should_cancel_handle_lost_token_after_ttl_without_auto_extend();

    [Fact]
    public override Task should_keep_lock_past_ttl_when_auto_extend_is_enabled() =>
        base.should_keep_lock_past_ttl_when_auto_extend_is_enabled();
}
