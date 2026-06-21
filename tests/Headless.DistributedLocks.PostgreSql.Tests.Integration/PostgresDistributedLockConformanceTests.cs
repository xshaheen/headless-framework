// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.DistributedLocks.PostgreSql;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Runs the cross-provider lock conformance contract (<see cref="DistributedLockTestsBase"/>)
/// against the PostgreSQL advisory-lock provider. Backend-specific behavior (advisory keys,
/// LISTEN/NOTIFY, fencing sequence, transaction coupling, connection death) lives in the sibling
/// test classes; this class only wires the provider and exposes the portable scenarios as facts.
/// </summary>
[Collection<PostgresDistributedLockFixture>]
public sealed class PostgresDistributedLockConformanceTests : DistributedLockTestsBase
{
    private readonly ServiceProvider _services;
    private readonly IDistributedLock _provider;

    public PostgresDistributedLockConformanceTests(PostgresDistributedLockFixture fixture)
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

    protected override IDistributedLock GetLockProvider() => _provider;

    protected override async ValueTask DisposeAsyncCore()
    {
        await _services.DisposeAsync();
        await base.DisposeAsyncCore();
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
    public override Task should_acquire_and_release_locks_async() => base.should_acquire_and_release_locks_async();

    [Fact]
    public override Task should_acquire_one_at_a_time_parallel() => base.should_acquire_one_at_a_time_parallel();

    [Fact]
    public override Task should_acquire_locks_in_sync() => base.should_acquire_locks_in_sync();

    [Fact]
    public override Task should_acquire_locks_in_parallel() => base.should_acquire_locks_in_parallel();

    [Fact]
    public override Task should_lock_one_at_a_time_async() => base.should_lock_one_at_a_time_async();

    [Fact]
    public override Task should_return_null_expiration_when_not_locked() =>
        base.should_return_null_expiration_when_not_locked();

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

    // Intentionally not overridden (not portable to the connection-scoped provider):
    //  - should_get_expiration_for_locked_resource / should_get_lock_info_for_locked_resource:
    //    session-scoped locks have no lease, so expiration and TimeToLive are always null.
    //  - should_keep_lock_alive_when_auto_extend_is_enabled_smoke: there is no lease to auto-extend.
    //  - should_timeout_when_try_to_lock_acquired_resource: relies on TTL expiry freeing the first
    //    lock; session-scoped locks are held for the connection lifetime and never expire on TTL.
}
