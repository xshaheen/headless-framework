// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.DistributedLocks;
using Headless.DistributedLocks.InMemory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class InMemoryReaderWriterLockProviderTests : DistributedReadWriteLockTestsBase
{
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly IGuidGenerator _guidGenerator = new SequentialGuidGenerator(SequentialGuidType.Version7);

    protected override IDistributedReadWriteLock GetReaderWriterLockProvider(DistributedLockOptions? options = null)
    {
        return new DistributedReadWriteLock(
            new InMemoryDistributedReadWriteLockStorage(_timeProvider),
            outboxBus: null,
            options ?? new DistributedLockOptions(),
            _guidGenerator,
            _timeProvider,
            LoggerFactory.CreateLogger<DistributedReadWriteLock>()
        );
    }

    protected override TimeProvider TimeProvider => _timeProvider;

    protected override async Task AdvanceTimeAsync(TimeSpan amount, CancellationToken cancellationToken)
    {
        var step = TimeSpan.FromMilliseconds(100);
        var remaining = amount;

        while (remaining > TimeSpan.Zero)
        {
            var currentStep = remaining < step ? remaining : step;
            _timeProvider.Advance(currentStep);
            remaining -= currentStep;

            await DrainUntilAsync(() => false, cancellationToken);
        }
    }

    [Fact]
    public override Task should_allow_multiple_readers_and_release_on_dispose()
    {
        return base.should_allow_multiple_readers_and_release_on_dispose();
    }

    [Fact]
    public override Task should_acquire_write_lock_exclusively()
    {
        return base.should_acquire_write_lock_exclusively();
    }

    [Fact]
    public override Task should_release_read_lock_and_allow_writer()
    {
        return base.should_release_read_lock_and_allow_writer();
    }

    [Fact]
    public override Task should_queue_second_writer_and_unblock_after_first_releases()
    {
        return base.should_queue_second_writer_and_unblock_after_first_releases();
    }

    [Fact]
    public override Task should_leave_lock_held_when_release_on_dispose_is_false()
    {
        return base.should_leave_lock_held_when_release_on_dispose_is_false();
    }

    [Fact]
    public override Task should_be_idempotent_for_stale_release()
    {
        return base.should_be_idempotent_for_stale_release();
    }

    [Fact]
    public override Task should_throw_when_acquire_read_blocked_by_writer()
    {
        return base.should_throw_when_acquire_read_blocked_by_writer();
    }

    [Fact]
    public override Task should_throw_when_acquire_write_blocked_by_reader()
    {
        return base.should_throw_when_acquire_write_blocked_by_reader();
    }

    [Fact]
    public override Task should_prefer_queued_writer_over_new_reader()
    {
        return base.should_prefer_queued_writer_over_new_reader();
    }

    [Fact]
    public override Task should_clear_writer_waiting_marker_when_try_acquire_write_times_out()
    {
        return base.should_clear_writer_waiting_marker_when_try_acquire_write_times_out();
    }

    [Fact]
    public override Task should_clear_writer_waiting_marker_when_try_acquire_write_is_cancelled()
    {
        return base.should_clear_writer_waiting_marker_when_try_acquire_write_is_cancelled();
    }

    [Fact]
    public override Task should_fire_handle_lost_token_when_read_lock_ttl_expires()
    {
        return base.should_fire_handle_lost_token_when_read_lock_ttl_expires();
    }

    [Fact]
    public override Task should_fire_handle_lost_token_when_write_lock_ttl_expires()
    {
        return base.should_fire_handle_lost_token_when_write_lock_ttl_expires();
    }

    [Fact]
    public override Task should_auto_extend_write_lock()
    {
        return base.should_auto_extend_write_lock();
    }

    [Fact]
    public override Task should_acquire_composite_read_write_set_in_canonical_order_and_collapse_modes()
    {
        return base.should_acquire_composite_read_write_set_in_canonical_order_and_collapse_modes();
    }

    [Fact]
    public override Task should_not_deadlock_when_two_callers_request_opposite_mixed_orders_concurrently()
    {
        return base.should_not_deadlock_when_two_callers_request_opposite_mixed_orders_concurrently();
    }

    [Fact]
    public override Task should_release_earlier_composite_children_when_later_resource_is_contended()
    {
        return base.should_release_earlier_composite_children_when_later_resource_is_contended();
    }

    [Fact]
    public override Task should_renew_and_release_composite_read_write_lease()
    {
        return base.should_renew_and_release_composite_read_write_lease();
    }

    [Fact]
    public override Task should_keep_composite_read_write_resources_when_disposed_without_release()
    {
        return base.should_keep_composite_read_write_resources_when_disposed_without_release();
    }

    [Fact]
    public override Task should_return_child_lease_for_single_canonical_read_write_resource()
    {
        return base.should_return_child_lease_for_single_canonical_read_write_resource();
    }
}
