// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.DistributedLocks.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

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
    private readonly string _connectionString;
    private readonly string _keyPrefix;

    public PostgreSqlDistributedLockConformanceTests(PostgreSqlDistributedLockFixture fixture)
    {
        _connectionString = fixture.ConnectionString;
        _keyPrefix = $"conformance:{Faker.Random.AlphaNumeric(6)}:";

        // Keepalive lets the provider notice a terminated backend while the held connection is idle; without it the
        // connection-death cases would wait for the next command that never comes.
        var connectionString = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { KeepAlive = 1 };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessDistributedLocks(setup =>
            setup.UsePostgreSql(options =>
            {
                options.ConnectionString = connectionString.ConnectionString;
                options.KeyPrefix = _keyPrefix;
            })
        );

        _services = services.BuildServiceProvider();
        _provider = _services.GetRequiredService<IDistributedLock>();
    }

    protected override IDistributedLock GetLockProvider()
    {
        return _provider;
    }

    protected override async Task KillLockHoldingConnectionAsync(
        IDistributedLease handle,
        CancellationToken cancellationToken
    )
    {
        // Resolve the exact advisory key so only the backend holding this handle's lock is terminated, not other
        // connections sharing the container.
        var key = PostgreSqlAdvisoryLockKey.FromString(_keyPrefix + handle.Resource, allowHashing: true);
        var (key1, key2) = key.Keys;

        await using var admin = new NpgsqlConnection(_connectionString);
        await admin.OpenAsync(cancellationToken);

        await using var command = admin.CreateCommand();
        command.CommandText = """
            SELECT pg_terminate_backend(l.pid)
            FROM pg_catalog.pg_locks l
            WHERE l.locktype = 'advisory'
              AND l.granted
              AND l.classid = @classId
              AND l.objid = @objId
              AND l.objsubid = @objSubId
              AND l.pid <> pg_backend_pid()
            """;
        command.Parameters.AddWithValue("classId", key1);
        command.Parameters.AddWithValue("objId", key2);
        command.Parameters.AddWithValue("objSubId", (short)(key.HasSingleKey ? 1 : 2));

        var terminated = await command.ExecuteScalarAsync(cancellationToken);
        terminated.Should().Be(true, "the lock-holding backend should be found and terminated");
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await _services.DisposeAsync();
        await base.DisposeAsyncCore();
    }

    [Fact]
    public override Task should_renew_held_lease()
    {
        return base.should_renew_held_lease();
    }

    [Fact]
    public override Task should_not_renew_released_lease()
    {
        return base.should_not_renew_released_lease();
    }

    [Fact]
    public override Task should_not_renew_unknown_lease_id()
    {
        return base.should_not_renew_unknown_lease_id();
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

    [Fact]
    public override Task should_not_fire_handle_lost_token_on_clean_release()
    {
        return base.should_not_fire_handle_lost_token_on_clean_release();
    }

    // Cases a connection-scoped lock cannot satisfy are allow-listed, with reasons, in
    // tests/Headless.Testing.Tests.Unit/Conformance/ConformanceCaseAllowList.cs.

    [Fact]
    public override Task should_keep_lock_alive_when_auto_extend_is_enabled_smoke()
    {
        return base.should_keep_lock_alive_when_auto_extend_is_enabled_smoke();
    }

    [Fact]
    public override Task should_fire_handle_lost_token_when_lock_holding_connection_dies()
    {
        return base.should_fire_handle_lost_token_when_lock_holding_connection_dies();
    }

    [Fact]
    public override Task should_not_renew_after_lock_holding_connection_dies()
    {
        return base.should_not_renew_after_lock_holding_connection_dies();
    }
}
