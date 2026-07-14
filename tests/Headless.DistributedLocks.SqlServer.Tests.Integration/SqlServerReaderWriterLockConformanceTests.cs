// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Runs the cross-provider reader-writer conformance contract
/// (<see cref="DistributedReadWriteLockTestsBase"/>) against the SQL Server application-lock provider.
/// Backend-specific behavior lives in the sibling test classes; this class only wires the portable scenarios as facts.
/// </summary>
[Collection<SqlServerDistributedLockFixture>]
public sealed class SqlServerReaderWriterLockConformanceTests : DistributedReadWriteLockTestsBase
{
    private readonly ServiceProvider _services;
    private readonly IDistributedReadWriteLock _provider;

    public SqlServerReaderWriterLockConformanceTests(SqlServerDistributedLockFixture fixture)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessDistributedLocks(setup =>
            setup.UseSqlServer(options =>
            {
                options.ConnectionString = fixture.ConnectionString;
                options.EnableFencing = false;
                options.KeyPrefix = $"conformance:rw:{Faker.Random.AlphaNumeric(6)}:";
            })
        );

        _services = services.BuildServiceProvider();
        _provider = _services.GetRequiredService<IDistributedReadWriteLock>();
    }

    protected override IDistributedReadWriteLock GetReaderWriterLockProvider(DistributedLockOptions? options = null) =>
        _provider;

    protected override TimeProvider TimeProvider => TimeProvider.System;

    protected override async Task AdvanceTimeAsync(TimeSpan amount, CancellationToken cancellationToken)
    {
        // SQL Server application locks are session-scoped and have no TTL lease, so there is no fake clock to
        // bridge. Acquire-timeout scenarios use wall-clock SQL waits; sleep long enough for those to elapse.
        await Task.Delay(amount + TimeSpan.FromMilliseconds(50), TimeProvider.System, cancellationToken);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await _services.DisposeAsync();
        await base.DisposeAsyncCore();
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

    // Intentionally not overridden (not portable to the connection-scoped provider):
    //  - should_prefer_queued_writer_over_new_reader / should_clear_writer_waiting_marker_when_try_acquire_write_times_out / should_clear_writer_waiting_marker_when_try_acquire_write_is_cancelled:
    //    SQL Server application locks have no Headless queue/waiting marker representation, so writer preference is not observable.
    //  - should_fire_handle_lost_token_when_read_lock_ttl_expires / should_fire_handle_lost_token_when_write_lock_ttl_expires:
    //    session-scoped locks have no lease, so expiration and TimeToLive are always null.
    //  - should_auto_extend_write_lock: there is no lease to auto-extend.
}
