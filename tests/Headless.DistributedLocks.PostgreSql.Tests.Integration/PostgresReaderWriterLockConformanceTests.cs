// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Runs the cross-provider lock conformance contract (<see cref="DistributedReadWriteLockTestsBase"/>)
/// against the PostgreSQL advisory-lock provider. Backend-specific behavior lives in the sibling
/// test classes; this class only wires the provider and exposes the portable scenarios as facts.
/// </summary>
[Collection<PostgreSqlDistributedLockFixture>]
public sealed class PostgresReaderWriterLockConformanceTests : DistributedReadWriteLockTestsBase
{
    private readonly ServiceProvider _services;
    private readonly IDistributedReadWriteLock _provider;

    public PostgresReaderWriterLockConformanceTests(PostgreSqlDistributedLockFixture fixture)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessDistributedLocks(setup =>
            setup.UsePostgreSql(options =>
            {
                options.ConnectionString = fixture.ConnectionString;
                options.KeyPrefix = $"conformance:rw:{Faker.Random.AlphaNumeric(6)}:";
            })
        );

        _services = services.BuildServiceProvider();
        _provider = _services.GetRequiredService<IDistributedReadWriteLock>();
    }

    protected override IDistributedReadWriteLock GetReaderWriterLockProvider(DistributedLockOptions? options = null)
    {
        return _provider;
    }

    protected override TimeProvider TimeProvider => TimeProvider.System;

    protected override async Task AdvanceTimeAsync(TimeSpan amount, CancellationToken cancellationToken)
    {
        // Postgres advisory locks are session-scoped and have no TTL lease, so there is no fake clock to
        // bridge. The acquire-timeout, however, is a real wall-clock timeout that fires on its own; the
        // exposed "throw when blocked" scenarios call this only to let that real timeout elapse, so a real
        // delay (plus slack) is the correct hook. TTL/auto-extend lease scenarios are not exposed here.
        await Task.Delay(amount + TimeSpan.FromMilliseconds(50), TimeProvider.System, cancellationToken);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await _services.DisposeAsync();
        await base.DisposeAsyncCore();
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

    // Intentionally not overridden (not portable to the connection-scoped provider):
    //  - should_prefer_queued_writer_over_new_reader / should_clear_writer_waiting_marker_when_try_acquire_write_times_out / should_clear_writer_waiting_marker_when_try_acquire_write_is_cancelled:
    //    Postgres advisory locks have no queue/waiting marker representation, so writer preference is not observable.
    //  - should_fire_handle_lost_token_when_read_lock_ttl_expires / should_fire_handle_lost_token_when_write_lock_ttl_expires:
    //    session-scoped locks have no lease, so expiration and TimeToLive are always null.
    //  - should_auto_extend_write_lock: there is no lease to auto-extend.

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
