// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.DistributedLocks;
using Headless.Redis;
using Headless.Redis.Testing;
using Microsoft.Extensions.Logging;

namespace Tests;

[Collection<RedisTestFixture>]
public sealed class RedisReaderWriterLockProviderTests(RedisTestFixture fixture) : DistributedReadWriteLockTestsBase
{
    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await fixture.ConnectionMultiplexer.FlushAllAsync();
    }

    protected override IDistributedReadWriteLock GetReaderWriterLockProvider(DistributedLockOptions? options = null)
    {
        return new DistributedReadWriteLock(
            fixture.ReaderWriterLockStorage,
            outboxBus: null,
            options ?? new DistributedLockOptions(),
            new SequentialGuidGenerator(SequentialGuidType.SqlServer),
            TimeProvider.System,
            LoggerFactory.CreateLogger<DistributedReadWriteLock>()
        );
    }

    protected override TimeProvider TimeProvider => TimeProvider.System;

    protected override async Task AdvanceTimeAsync(TimeSpan amount, CancellationToken cancellationToken)
    {
        await Task.Delay(amount + TimeSpan.FromMilliseconds(50), TimeProvider.System, cancellationToken);
    }

    protected override async Task WaitForWriterQueuedAsync(string resource, CancellationToken cancellationToken)
    {
        await EventuallyAsync(
            async () =>
            {
                var db = fixture.ConnectionMultiplexer.GetDatabase();
                var writerKeyValue = await db.StringGetAsync(
                    "{" + DistributedLockOptions.DefaultKeyPrefix + resource + "}:writer"
                );

                return writerKeyValue.HasValue;
            },
            cancellationToken: cancellationToken
        );
    }

    [Fact]
    public override Task should_allow_multiple_readers_and_release_on_dispose() =>
        base.should_allow_multiple_readers_and_release_on_dispose();

    [Fact]
    public override Task should_acquire_write_lock_exclusively() => base.should_acquire_write_lock_exclusively();

    [Fact]
    public override Task should_release_read_lock_and_allow_writer() =>
        base.should_release_read_lock_and_allow_writer();

    [Fact]
    public override Task should_queue_second_writer_and_unblock_after_first_releases() =>
        base.should_queue_second_writer_and_unblock_after_first_releases();

    [Fact]
    public override Task should_leave_lock_held_when_release_on_dispose_is_false() =>
        base.should_leave_lock_held_when_release_on_dispose_is_false();

    [Fact]
    public override Task should_be_idempotent_for_stale_release() => base.should_be_idempotent_for_stale_release();

    [Fact]
    public override Task should_throw_when_acquire_read_blocked_by_writer() =>
        base.should_throw_when_acquire_read_blocked_by_writer();

    [Fact]
    public override Task should_throw_when_acquire_write_blocked_by_reader() =>
        base.should_throw_when_acquire_write_blocked_by_reader();

    [Fact]
    public override Task should_prefer_queued_writer_over_new_reader() =>
        base.should_prefer_queued_writer_over_new_reader();

    [Fact]
    public override Task should_clear_writer_waiting_marker_when_try_acquire_write_times_out() =>
        base.should_clear_writer_waiting_marker_when_try_acquire_write_times_out();

    [Fact]
    public override Task should_clear_writer_waiting_marker_when_try_acquire_write_is_cancelled() =>
        base.should_clear_writer_waiting_marker_when_try_acquire_write_is_cancelled();

    [Fact]
    public override Task should_fire_handle_lost_token_when_read_lock_ttl_expires() =>
        base.should_fire_handle_lost_token_when_read_lock_ttl_expires();

    [Fact]
    public override Task should_fire_handle_lost_token_when_write_lock_ttl_expires() =>
        base.should_fire_handle_lost_token_when_write_lock_ttl_expires();

    [Fact]
    public override Task should_auto_extend_write_lock() => base.should_auto_extend_write_lock();

    // Redis-specific key-inspection guard: the shared cancel scenario asserts the marker is cleared via the
    // public API; this variant additionally inspects the raw writer-waiting key as a backend guard. Not
    // load-bearing — kept to catch Redis Lua/key regressions that the public-API assertion could not localize.
    [Fact]
    public async Task should_clear_writer_waiting_redis_key_when_try_acquire_write_is_cancelled()
    {
        var provider = GetReaderWriterLockProvider();
        var resource = $"rw:{Faker.Random.AlphaNumeric(10)}";
        await using var reader = await provider.AcquireReadLockAsync(resource, cancellationToken: AbortToken);
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(300));

        var act = async () =>
            await provider.TryAcquireWriteLockAsync(
                resource,
                new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromSeconds(5) },
                cts.Token
            );

        await act.Should().ThrowAsync<OperationCanceledException>();

        var db = fixture.ConnectionMultiplexer.GetDatabase();
        var writerKeyValue = await db.StringGetAsync(
            "{" + DistributedLockOptions.DefaultKeyPrefix + resource + "}:writer"
        );
        writerKeyValue.HasValue.Should().BeFalse();
    }

    [Fact]
    public override Task should_acquire_composite_read_write_set_in_canonical_order_and_collapse_modes() =>
        base.should_acquire_composite_read_write_set_in_canonical_order_and_collapse_modes();

    [Fact]
    public override Task should_not_deadlock_when_two_callers_request_opposite_mixed_orders_concurrently() =>
        base.should_not_deadlock_when_two_callers_request_opposite_mixed_orders_concurrently();

    [Fact]
    public override Task should_release_earlier_composite_children_when_later_resource_is_contended() =>
        base.should_release_earlier_composite_children_when_later_resource_is_contended();

    [Fact]
    public override Task should_renew_and_release_composite_read_write_lease() =>
        base.should_renew_and_release_composite_read_write_lease();

    [Fact]
    public override Task should_keep_composite_read_write_resources_when_disposed_without_release() =>
        base.should_keep_composite_read_write_resources_when_disposed_without_release();

    [Fact]
    public override Task should_return_child_lease_for_single_canonical_read_write_resource() =>
        base.should_return_child_lease_for_single_canonical_read_write_resource();
}
