// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.DistributedLocks.SqlServer;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Runs the cross-provider lock conformance contract (<see cref="DistributedLockProviderTestsBase"/>)
/// against the SQL Server application-lock provider. SQL Server-specific behavior lives in the sibling
/// test classes; this class only wires the portable scenarios as facts.
/// </summary>
[Collection<SqlServerDistributedLockFixture>]
public sealed class SqlServerDistributedLockConformanceTests : DistributedLockProviderTestsBase
{
    private readonly ServiceProvider _services;
    private readonly IDistributedLockProvider _provider;

    public SqlServerDistributedLockConformanceTests(SqlServerDistributedLockFixture fixture)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSqlServerDistributedLocks(options =>
        {
            options.ConnectionString = fixture.ConnectionString;
            options.EnableFencing = false;
            options.KeyPrefix = $"conformance:{Faker.Random.AlphaNumeric(6)}:";
        });

        _services = services.BuildServiceProvider();
        _provider = _services.GetRequiredService<IDistributedLockProvider>();
    }

    protected override IDistributedLockProvider GetLockProvider() => _provider;

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
}
