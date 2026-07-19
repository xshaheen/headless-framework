// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Runs the cross-provider lock conformance contract (<see cref="DistributedLockTestsBase"/>)
/// against the PostgreSQL advisory-lock provider. Backend-specific behavior (advisory keys,
/// LISTEN/NOTIFY, fencing sequence, transaction coupling, connection death) lives in the sibling
/// test classes; this class only wires the provider and exposes the portable scenarios as facts.
/// </summary>
[Collection<PostgreSqlDistributedLockFixture>]
public sealed class PostgreSqlDistributedLockConformanceTests : DistributedLockTestsBase
{
    private readonly ServiceProvider _services;
    private readonly IDistributedLock _provider;

    public PostgreSqlDistributedLockConformanceTests(PostgreSqlDistributedLockFixture fixture)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessDistributedLocks(setup =>
            setup.UsePostgreSql(options =>
            {
                options.ConnectionString = fixture.ConnectionString;
                options.KeyPrefix = $"conformance:{Faker.Random.AlphaNumeric(6)}:";
            })
        );

        _services = services.BuildServiceProvider();
        _provider = _services.GetRequiredService<IDistributedLock>();
    }

    protected override IDistributedLock GetLockProvider()
    {
        return _provider;
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await _services.DisposeAsync();
        await base.DisposeAsyncCore();
    }

    [Fact]
    public override Task should_lock_with_try_acquire()
    {
        return base.should_lock_with_try_acquire();
    }

    [Fact]
    public override Task should_lock_with_acquire()
    {
        return base.should_lock_with_acquire();
    }

    [Fact]
    public override Task should_not_acquire_when_already_locked()
    {
        return base.should_not_acquire_when_already_locked();
    }

    [Fact]
    public override Task should_throw_timeout_with_acquire_when_already_locked()
    {
        return base.should_throw_timeout_with_acquire_when_already_locked();
    }

    [Fact]
    public override Task should_obtain_multiple_locks()
    {
        return base.should_obtain_multiple_locks();
    }

    [Fact]
    public override Task should_acquire_composite_in_canonical_order_and_deduplicate()
    {
        return base.should_acquire_composite_in_canonical_order_and_deduplicate();
    }

    [Fact]
    public override Task should_acquire_opposite_composite_orders_sequentially()
    {
        return base.should_acquire_opposite_composite_orders_sequentially();
    }

    [Fact]
    public override Task should_release_earlier_composite_children_when_later_resource_is_contended()
    {
        return base.should_release_earlier_composite_children_when_later_resource_is_contended();
    }

    [Fact]
    public override Task should_renew_and_release_composite_lease()
    {
        return base.should_renew_and_release_composite_lease();
    }

    [Fact]
    public override Task should_dispatch_composite_renew_and_release_through_provider()
    {
        return base.should_dispatch_composite_renew_and_release_through_provider();
    }

    [Fact]
    public override Task should_keep_composite_resources_when_disposed_without_release()
    {
        return base.should_keep_composite_resources_when_disposed_without_release();
    }

    [Fact]
    public override Task should_release_lock_multiple_times()
    {
        return base.should_release_lock_multiple_times();
    }

    [Fact]
    public override Task should_keep_lock_when_disposed_with_release_on_dispose_false()
    {
        return base.should_keep_lock_when_disposed_with_release_on_dispose_false();
    }

    [Fact]
    public override Task should_release_explicitly_when_release_on_dispose_false()
    {
        return base.should_release_explicitly_when_release_on_dispose_false();
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

    [Fact]
    public override Task should_lock_one_at_a_time_async()
    {
        return base.should_lock_one_at_a_time_async();
    }

    [Fact]
    public override Task should_return_null_expiration_when_not_locked()
    {
        return base.should_return_null_expiration_when_not_locked();
    }

    [Fact]
    public override Task should_return_null_lock_info_when_not_locked()
    {
        return base.should_return_null_lock_info_when_not_locked();
    }

    [Fact]
    public override Task should_list_active_locks()
    {
        return base.should_list_active_locks();
    }

    [Fact]
    public override Task should_get_active_locks_count()
    {
        return base.should_get_active_locks_count();
    }

    [Fact]
    public override Task should_expose_none_handle_lost_token_without_monitoring()
    {
        return base.should_expose_none_handle_lost_token_without_monitoring();
    }

    // Intentionally not overridden (not portable to the connection-scoped provider):
    //  - should_get_expiration_for_locked_resource / should_get_lock_info_for_locked_resource:
    //    session-scoped locks have no lease, so expiration and TimeToLive are always null.
    //  - should_keep_lock_alive_when_auto_extend_is_enabled_smoke: there is no lease to auto-extend.
    //  - should_timeout_when_try_to_lock_acquired_resource: relies on TTL expiry freeing the first
    //    lock; session-scoped locks are held for the connection lifetime and never expire on TTL.
}
