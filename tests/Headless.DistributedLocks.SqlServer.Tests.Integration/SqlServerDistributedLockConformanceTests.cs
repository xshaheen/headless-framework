// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.DistributedLocks.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Runs the cross-provider lock conformance contract (<see cref="DistributedLockTestsBase"/>)
/// against the SQL Server application-lock provider. SQL Server-specific behavior lives in the sibling
/// test classes; this class only wires the portable scenarios as facts.
/// </summary>
[Collection<SqlServerDistributedLockFixture>]
public sealed class SqlServerDistributedLockConformanceTests : DistributedLockTestsBase
{
    private readonly ServiceProvider _services;
    private readonly IDistributedLock _provider;
    private readonly string _connectionString;

    public SqlServerDistributedLockConformanceTests(SqlServerDistributedLockFixture fixture)
    {
        _connectionString = fixture.ConnectionString;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessDistributedLocks(setup =>
            setup.UseSqlServer(options =>
            {
                options.ConnectionString = fixture.ConnectionString;
                options.EnableFencing = false;
                options.KeyPrefix = $"conformance:{Faker.Random.AlphaNumeric(6)}:";
            })
        );

        _services = services.BuildServiceProvider();
        _provider = _services.GetRequiredService<IDistributedLock>();
    }

    protected override IDistributedLock GetLockProvider() => _provider;

    /// <summary>
    /// Simulates silent connection death for the SQL Server provider by discovering the SPID of the session that
    /// holds the application lock and issuing <c>KILL</c> from a separate admin connection. The held session has no
    /// in-flight command at this point, so only the provider's active liveness probe can surface the loss.
    /// </summary>
    protected override async Task KillLockHoldingConnectionAsync(
        IDistributedLease handle,
        CancellationToken cancellationToken
    )
    {
        await using var admin = new SqlConnection(_connectionString);
        await admin.OpenAsync(cancellationToken);

        // The conformance collection disables parallelization and each test holds a single application lock, so the
        // only granted APPLICATION lock (other than this admin connection) belongs to the handle under test.
        await using var lookup = admin.CreateCommand();
        lookup.CommandText = """
            SELECT TOP (1) request_session_id
            FROM sys.dm_tran_locks
            WHERE resource_type = 'APPLICATION'
              AND request_status = 'GRANT'
              AND request_session_id <> @@SPID;
            """;

        var spidResult = await lookup.ExecuteScalarAsync(cancellationToken);
        spidResult.Should().NotBeNull("the lock-holding session must hold a granted application lock");

        var spid = Convert.ToInt32(spidResult, CultureInfo.InvariantCulture);

        await using var kill = admin.CreateCommand();
        // KILL does not accept a parameter; the spid is an int read from the server, so it is safe to inline.
        kill.CommandText = string.Create(CultureInfo.InvariantCulture, $"KILL {spid};");
        await kill.ExecuteNonQueryAsync(cancellationToken);
    }

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
    public override Task should_fire_handle_lost_token_when_lock_holding_connection_dies() =>
        base.should_fire_handle_lost_token_when_lock_holding_connection_dies();
}
