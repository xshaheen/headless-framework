// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.DistributedLocks;
using Headless.DistributedLocks.PostgreSql;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Tests;

[Collection<PostgreSqlDistributedLockFixture>]
public sealed class PostgresUnitOfWorkTransactionLockTests(PostgreSqlDistributedLockFixture fixture) : TestBase
{
    [Fact]
    public async Task should_hold_the_lock_inside_the_unit_and_release_it_on_complete()
    {
        // given
        await using var provider = _BuildProvider();
        var factory = provider.GetRequiredService<IUnitOfWorkFactory>();
        var resource = _CreateResourceName();
        var key = _KeyFor(resource);

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await using var unit = await factory.BeginAsync(connection, AbortToken);

        // when
        var handle = await unit.TransactionLocks.AcquireAsync(resource, cancellationToken: AbortToken);

        // then — held by the unit's transaction, and a session lock on the same logical name contends with it
        handle.Resource.Should().Be(resource);
        (await _CountAdvisoryLocksAsync(key)).Should().BePositive();
        (await _TrySessionLockAsync(key)).Should().BeFalse();

        await unit.CompleteAsync(AbortToken);

        (await _CountAdvisoryLocksAsync(key)).Should().Be(0);
        (await _TrySessionLockAsync(key)).Should().BeTrue();
    }

    [Fact]
    public async Task should_release_the_lock_when_the_unit_rolls_back()
    {
        // given
        await using var provider = _BuildProvider();
        var factory = provider.GetRequiredService<IUnitOfWorkFactory>();
        var resource = _CreateResourceName();
        var key = _KeyFor(resource);

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);

        // when — dispose without complete is an implicit rollback
        await using (var unit = await factory.BeginAsync(connection, AbortToken))
        {
            await unit.TransactionLocks.AcquireAsync(resource, cancellationToken: AbortToken);
            (await _CountAdvisoryLocksAsync(key)).Should().BePositive();
        }

        // then
        (await _CountAdvisoryLocksAsync(key))
            .Should()
            .Be(0);
    }

    [Fact]
    public async Task should_try_acquire_null_while_another_unit_holds_it_and_a_handle_after_it_completes()
    {
        // given
        await using var provider = _BuildProvider();
        var factory = provider.GetRequiredService<IUnitOfWorkFactory>();
        var resource = _CreateResourceName();

        await using var holderConnection = new NpgsqlConnection(fixture.ConnectionString);
        await using var contenderConnection = new NpgsqlConnection(fixture.ConnectionString);

        await using var holder = await factory.BeginAsync(holderConnection, AbortToken);
        await holder.TransactionLocks.AcquireAsync(resource, cancellationToken: AbortToken);

        // when / then
        await using var contender = await factory.BeginAsync(contenderConnection, AbortToken);
        (await contender.TransactionLocks.TryAcquireAsync(resource, cancellationToken: AbortToken)).Should().BeNull();

        await holder.CompleteAsync(AbortToken);

        (await contender.TransactionLocks.TryAcquireAsync(resource, cancellationToken: AbortToken))
            .Should()
            .Be(new TransactionLockHandle(resource));
    }

    [Fact]
    public async Task should_time_out_a_bounded_acquire_and_leave_the_unit_usable()
    {
        // given
        await using var provider = _BuildProvider();
        var factory = provider.GetRequiredService<IUnitOfWorkFactory>();
        var resource = _CreateResourceName();

        await using var holderConnection = new NpgsqlConnection(fixture.ConnectionString);
        await using var contenderConnection = new NpgsqlConnection(fixture.ConnectionString);

        await using var holder = await factory.BeginAsync(holderConnection, AbortToken);
        await holder.TransactionLocks.AcquireAsync(resource, cancellationToken: AbortToken);
        await using var contender = await factory.BeginAsync(contenderConnection, AbortToken);

        // when
        var stopwatch = Stopwatch.StartNew();
        var act = async () =>
            await contender.TransactionLocks.AcquireAsync(resource, TimeSpan.FromMilliseconds(300), AbortToken);

        // then — the wait is server-bounded, not unbounded, and the timeout did not abort the transaction
        await act.Should().ThrowAsync<LockAcquisitionTimeoutException>();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
        (await contender.TransactionLocks.TryAcquireAsync(_CreateResourceName(), cancellationToken: AbortToken))
            .Should()
            .NotBeNull();

        // and the lock_timeout did not leak past the acquire: a later statement in the same unit is unbounded
        (await _CurrentLockTimeoutAsync(contenderConnection, contender))
            .Should()
            .Be("0");
    }

    [Fact]
    public async Task should_not_leak_the_lock_timeout_after_a_bounded_acquire_succeeds()
    {
        // given
        await using var provider = _BuildProvider();
        var factory = provider.GetRequiredService<IUnitOfWorkFactory>();
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await using var unit = await factory.BeginAsync(connection, AbortToken);

        // when
        await unit.TransactionLocks.AcquireAsync(_CreateResourceName(), TimeSpan.FromSeconds(5), AbortToken);

        // then — a later statement in the unit runs under the unit's own lock_timeout, not the acquire's
        (await _CurrentLockTimeoutAsync(connection, unit))
            .Should()
            .Be("0");
    }

    [Fact]
    public async Task should_leave_the_unit_usable_when_a_bounded_acquire_is_cancelled()
    {
        // given
        await using var provider = _BuildProvider();
        var factory = provider.GetRequiredService<IUnitOfWorkFactory>();
        var resource = _CreateResourceName();

        await using var holderConnection = new NpgsqlConnection(fixture.ConnectionString);
        await using var contenderConnection = new NpgsqlConnection(fixture.ConnectionString);

        await using var holder = await factory.BeginAsync(holderConnection, AbortToken);
        await holder.TransactionLocks.AcquireAsync(resource, cancellationToken: AbortToken);
        await using var contender = await factory.BeginAsync(contenderConnection, AbortToken);

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(300));

        // when
        var act = async () =>
            await contender.TransactionLocks.AcquireAsync(resource, TimeSpan.FromSeconds(30), cancellation.Token);

        // then — the cancelled wait neither aborted the unit's transaction nor left the acquire's timeout behind
        await act.Should().ThrowAsync<OperationCanceledException>();
        (await contender.TransactionLocks.TryAcquireAsync(_CreateResourceName(), cancellationToken: AbortToken))
            .Should()
            .NotBeNull();
        (await _CurrentLockTimeoutAsync(contenderConnection, contender)).Should().Be("0");
        await contender.CompleteAsync(AbortToken);
    }

    [Fact]
    public async Task should_succeed_when_the_unit_already_holds_the_lock()
    {
        // given
        await using var provider = _BuildProvider();
        var factory = provider.GetRequiredService<IUnitOfWorkFactory>();
        var resource = _CreateResourceName();
        var key = _KeyFor(resource);

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await using var unit = await factory.BeginAsync(connection, AbortToken);
        await unit.TransactionLocks.AcquireAsync(resource, cancellationToken: AbortToken);

        // when — the unit's transaction is the owner, so acquiring again is granted on every wait shape
        var bounded = await unit.TransactionLocks.AcquireAsync(resource, TimeSpan.FromSeconds(5), AbortToken);
        var unbounded = await unit.TransactionLocks.AcquireAsync(resource, Timeout.InfiniteTimeSpan, AbortToken);
        var tryOnce = await unit.TransactionLocks.TryAcquireAsync(resource, cancellationToken: AbortToken);

        // then
        bounded.Should().Be(new TransactionLockHandle(resource));
        unbounded.Should().Be(bounded);
        tryOnce.Should().Be(bounded);

        await unit.CompleteAsync(AbortToken);
        (await _CountAdvisoryLocksAsync(key)).Should().Be(0);
    }

    [Fact]
    public async Task should_try_acquire_with_a_bounded_wait_and_succeed_once_released()
    {
        // given
        await using var provider = _BuildProvider();
        var factory = provider.GetRequiredService<IUnitOfWorkFactory>();
        var resource = _CreateResourceName();

        await using var holderConnection = new NpgsqlConnection(fixture.ConnectionString);
        await using var contenderConnection = new NpgsqlConnection(fixture.ConnectionString);

        await using var holder = await factory.BeginAsync(holderConnection, AbortToken);
        await holder.TransactionLocks.AcquireAsync(resource, cancellationToken: AbortToken);
        await using var contender = await factory.BeginAsync(contenderConnection, AbortToken);

        // when — release the holder while the contender is inside its bounded wait
        var release = Task.Run(
            async () =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200), AbortToken);
                await holder.CompleteAsync(AbortToken);
            },
            AbortToken
        );
        var handle = await contender.TransactionLocks.TryAcquireAsync(resource, TimeSpan.FromSeconds(10), AbortToken);
        await release;

        // then
        handle.Should().Be(new TransactionLockHandle(resource));
    }

    [Fact]
    public async Task should_run_inside_run_async_and_release_after_the_block()
    {
        // given
        await using var provider = _BuildProvider();
        var factory = provider.GetRequiredService<IUnitOfWorkFactory>();
        var resource = _CreateResourceName();
        var key = _KeyFor(resource);

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);

        // when
        await factory.RunAsync(
            connection,
            async (unit, ct) =>
            {
                await unit.TransactionLocks.AcquireAsync(resource, cancellationToken: ct);
                (await _CountAdvisoryLocksAsync(key)).Should().BePositive();
            },
            cancellationToken: AbortToken
        );

        // then
        (await _CountAdvisoryLocksAsync(key))
            .Should()
            .Be(0);
    }

    [Fact]
    public async Task should_refuse_a_unit_with_no_relational_resource()
    {
        // given
        await using var provider = _BuildProvider();
        var factory = provider.GetRequiredService<IUnitOfWorkFactory>();
        await using var unit = await factory.BeginAsync(cancellationToken: AbortToken);

        // when
        var act = async () =>
            await unit.TransactionLocks.AcquireAsync(_CreateResourceName(), cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no relational resource*");
    }

    [Fact]
    public async Task should_refuse_the_accessor_on_a_completed_unit()
    {
        // given
        await using var provider = _BuildProvider();
        var factory = provider.GetRequiredService<IUnitOfWorkFactory>();
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await using var unit = await factory.BeginAsync(connection, AbortToken);
        await unit.CompleteAsync(AbortToken);

        // when
        var act = () => unit.TransactionLocks;

        // then — GetOrAdd is a registration and a terminal unit refuses registrations
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task should_refuse_a_binding_kept_past_completion()
    {
        // given
        await using var provider = _BuildProvider();
        var factory = provider.GetRequiredService<IUnitOfWorkFactory>();
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await using var unit = await factory.BeginAsync(connection, AbortToken);
        var locks = unit.TransactionLocks;
        await unit.CompleteAsync(AbortToken);

        // when
        var act = async () => await locks.AcquireAsync(_CreateResourceName(), cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Completed*");
    }

    [Fact]
    public async Task should_hold_every_resource_of_a_set_and_release_them_on_complete()
    {
        // given
        await using var provider = _BuildProvider();
        var factory = provider.GetRequiredService<IUnitOfWorkFactory>();
        var (first, second) = _CreateOrderedPair();

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await using var unit = await factory.BeginAsync(connection, AbortToken);

        // when — unsorted, with a duplicate
        var handles = await unit.TransactionLocks.AcquireAllAsync(
            [second, first, second],
            cancellationToken: AbortToken
        );

        // then
        handles.Should().Equal(new TransactionLockHandle(first), new TransactionLockHandle(second));
        (await _CountAdvisoryLocksAsync(_KeyFor(first))).Should().BePositive();
        (await _CountAdvisoryLocksAsync(_KeyFor(second))).Should().BePositive();

        await unit.CompleteAsync(AbortToken);

        (await _CountAdvisoryLocksAsync(_KeyFor(first))).Should().Be(0);
        (await _CountAdvisoryLocksAsync(_KeyFor(second))).Should().Be(0);
    }

    [Fact]
    public async Task should_take_nothing_from_a_contended_set_and_keep_what_the_unit_held_before()
    {
        // given — the contender already holds "earlier"; another unit holds "blocked", which sorts last
        await using var provider = _BuildProvider();
        var factory = provider.GetRequiredService<IUnitOfWorkFactory>();
        var prefix = "uow-transaction-lock-set:" + Guid.NewGuid() + ":";
        var earlier = prefix + "a";
        var free = prefix + "b";
        var blocked = prefix + "c";

        await using var holderConnection = new NpgsqlConnection(fixture.ConnectionString);
        await using var contenderConnection = new NpgsqlConnection(fixture.ConnectionString);
        await using var holder = await factory.BeginAsync(holderConnection, AbortToken);
        await holder.TransactionLocks.AcquireAsync(blocked, cancellationToken: AbortToken);
        await using var contender = await factory.BeginAsync(contenderConnection, AbortToken);
        await contender.TransactionLocks.AcquireAsync(earlier, cancellationToken: AbortToken);

        // when
        var handles = await contender.TransactionLocks.TryAcquireAllAsync(
            [blocked, free, earlier],
            TimeSpan.FromMilliseconds(300),
            AbortToken
        );

        // then — refused as a whole: the free resource it took is released, the earlier hold survives
        handles.Should().BeNull();
        (await _CountAdvisoryLocksAsync(_KeyFor(free))).Should().Be(0);
        (await _CountAdvisoryLocksAsync(_KeyFor(earlier))).Should().BePositive();
        (await _CurrentLockTimeoutAsync(contenderConnection, contender)).Should().Be("0");

        // and the unit's transaction is still usable
        (await contender.TransactionLocks.TryAcquireAsync(free, cancellationToken: AbortToken))
            .Should()
            .NotBeNull();
        await contender.CompleteAsync(AbortToken);
    }

    [Fact]
    public async Task should_bound_the_whole_set_by_one_budget_and_name_the_joined_set_on_timeout()
    {
        // given — both resources are held elsewhere, so a per-resource budget would wait twice as long
        await using var provider = _BuildProvider();
        var factory = provider.GetRequiredService<IUnitOfWorkFactory>();
        var (first, second) = _CreateOrderedPair();

        await using var holderConnection = new NpgsqlConnection(fixture.ConnectionString);
        await using var contenderConnection = new NpgsqlConnection(fixture.ConnectionString);
        await using var holder = await factory.BeginAsync(holderConnection, AbortToken);
        await holder.TransactionLocks.AcquireAllAsync([first, second], cancellationToken: AbortToken);
        await using var contender = await factory.BeginAsync(contenderConnection, AbortToken);

        // when
        var stopwatch = Stopwatch.StartNew();
        var act = async () =>
            await contender.TransactionLocks.AcquireAllAsync([second, first], TimeSpan.FromSeconds(1), AbortToken);

        // then
        (await act.Should().ThrowAsync<LockAcquisitionTimeoutException>())
            .Which.Resource.Should()
            .Be(first + "+" + second);
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
        (await _CurrentLockTimeoutAsync(contenderConnection, contender)).Should().Be("0");
    }

    [Fact]
    public async Task should_spend_one_budget_across_the_set_not_one_per_resource()
    {
        // given — one holder per resource; the first is released halfway through the contender's budget
        await using var provider = _BuildProvider();
        var factory = provider.GetRequiredService<IUnitOfWorkFactory>();
        var (first, second) = _CreateOrderedPair();

        await using var firstHolderConnection = new NpgsqlConnection(fixture.ConnectionString);
        await using var secondHolderConnection = new NpgsqlConnection(fixture.ConnectionString);
        await using var contenderConnection = new NpgsqlConnection(fixture.ConnectionString);
        await using var firstHolder = await factory.BeginAsync(firstHolderConnection, AbortToken);
        await firstHolder.TransactionLocks.AcquireAsync(first, cancellationToken: AbortToken);
        await using var secondHolder = await factory.BeginAsync(secondHolderConnection, AbortToken);
        await secondHolder.TransactionLocks.AcquireAsync(second, cancellationToken: AbortToken);
        await using var contender = await factory.BeginAsync(contenderConnection, AbortToken);

        var releaseFirst = Task.Run(
            async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(1), AbortToken);
                await firstHolder.CompleteAsync(AbortToken);
            },
            AbortToken
        );

        // when
        var stopwatch = Stopwatch.StartNew();
        var handles = await contender.TransactionLocks.TryAcquireAllAsync(
            [first, second],
            TimeSpan.FromSeconds(2),
            AbortToken
        );
        var elapsed = stopwatch.Elapsed;
        await releaseFirst;

        // then — the second resource waited only for the second left over (about 2 s in all), not a fresh 2 s (3 s)
        handles.Should().BeNull();
        elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1.8));
        elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2.7));
        (await _CountAdvisoryLocksAsync(_KeyFor(first))).Should().Be(0);
    }

    [Fact]
    public async Task should_acquire_a_set_in_ordinal_order_whatever_order_the_caller_lists_it()
    {
        // given — a holder takes only the first resource
        await using var provider = _BuildProvider();
        var factory = provider.GetRequiredService<IUnitOfWorkFactory>();
        var (first, second) = _CreateOrderedPair();

        await using var holderConnection = new NpgsqlConnection(fixture.ConnectionString);
        await using var contenderConnection = new NpgsqlConnection(fixture.ConnectionString);
        await using var holder = await factory.BeginAsync(holderConnection, AbortToken);
        await holder.TransactionLocks.AcquireAsync(first, cancellationToken: AbortToken);
        await using var contender = await factory.BeginAsync(contenderConnection, AbortToken);

        // when — the contender lists the free resource first; it must still wait on the first one before the second
        var acquire = contender
            .TransactionLocks.AcquireAllAsync([second, first], Timeout.InfiniteTimeSpan, AbortToken)
            .AsTask();
        await _WaitUntilWaitingOnAdvisoryLockAsync(contenderConnection);

        // then — while it waits on the first, it has not taken the second
        (await _CountAdvisoryLocksAsync(_KeyFor(second)))
            .Should()
            .Be(0);

        await holder.CompleteAsync(AbortToken);
        var handles = await acquire;

        handles.Select(h => h.Resource).Should().Equal(first, second);
        await contender.CompleteAsync(AbortToken);
    }

    [Fact]
    public async Task should_release_what_a_cancelled_set_took_and_leave_the_unit_usable()
    {
        // given
        await using var provider = _BuildProvider();
        var factory = provider.GetRequiredService<IUnitOfWorkFactory>();
        var (first, second) = _CreateOrderedPair();

        await using var holderConnection = new NpgsqlConnection(fixture.ConnectionString);
        await using var contenderConnection = new NpgsqlConnection(fixture.ConnectionString);
        await using var holder = await factory.BeginAsync(holderConnection, AbortToken);
        await holder.TransactionLocks.AcquireAsync(second, cancellationToken: AbortToken);
        await using var contender = await factory.BeginAsync(contenderConnection, AbortToken);

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(300));

        // when — the first resource is granted, then the wait on the second is cancelled
        var act = async () =>
            await contender.TransactionLocks.AcquireAllAsync(
                [first, second],
                TimeSpan.FromSeconds(30),
                cancellation.Token
            );

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
        (await _CountAdvisoryLocksAsync(_KeyFor(first))).Should().Be(0);
        (await contender.TransactionLocks.TryAcquireAsync(first, cancellationToken: AbortToken)).Should().NotBeNull();
        (await _CurrentLockTimeoutAsync(contenderConnection, contender)).Should().Be("0");
        await contender.CompleteAsync(AbortToken);
    }

    [Fact]
    public async Task should_acquire_synchronously_with_the_same_wait_shapes()
    {
        // given
        await using var provider = _BuildProvider();
        var factory = provider.GetRequiredService<IUnitOfWorkFactory>();
        var (first, second) = _CreateOrderedPair();
        var contended = _CreateResourceName();

        await using var holderConnection = new NpgsqlConnection(fixture.ConnectionString);
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await using var holder = await factory.BeginAsync(holderConnection, AbortToken);
        await holder.TransactionLocks.AcquireAsync(contended, cancellationToken: AbortToken);
        await using var unit = await factory.BeginAsync(connection, AbortToken);
        var locks = unit.TransactionLocks;

        // when
        var single = locks.Acquire(first);
        var set = locks.AcquireAll([second, first], TimeSpan.FromSeconds(5));
        var stopwatch = Stopwatch.StartNew();
        var bounded = locks.TryAcquire(contended, TimeSpan.FromMilliseconds(300));
        var elapsed = stopwatch.Elapsed;
        var throwing = () => locks.AcquireAll([first, contended], TimeSpan.FromMilliseconds(300));

        // then — a bounded synchronous wait expires into null, not an exception, and leaves the unit usable
        single.Should().Be(new TransactionLockHandle(first));
        set.Select(h => h.Resource).Should().Equal(first, second);
        bounded.Should().BeNull();
        elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(250));
        throwing.Should().Throw<LockAcquisitionTimeoutException>();
        (await _CountAdvisoryLocksAsync(_KeyFor(first))).Should().BePositive();
        (await _CurrentLockTimeoutAsync(connection, unit)).Should().Be("0");

        await unit.CompleteAsync(AbortToken);
        (await _CountAdvisoryLocksAsync(_KeyFor(first))).Should().Be(0);
        (await _CountAdvisoryLocksAsync(_KeyFor(second))).Should().Be(0);
    }

    [Fact]
    public async Task should_contend_with_a_session_lock_on_a_typed_key()
    {
        // given
        await using var provider = _BuildProvider();
        var factory = provider.GetRequiredService<IUnitOfWorkFactory>();
        var lockProvider = provider.GetRequiredService<IDistributedLock>();
        var resource = LockKey.For<PostgresUnitOfWorkTransactionLockTests>(Guid.NewGuid());

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await using var unit = await factory.BeginAsync(connection, AbortToken);

        // when
        await unit.TransactionLocks.AcquireAsync(resource, cancellationToken: AbortToken);

        // then — the session provider derives the same advisory key from the same typed name
        var lease = await lockProvider.TryAcquireAsync(
            resource,
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero },
            AbortToken
        );
        lease.Should().BeNull();
    }

    private static (string First, string Second) _CreateOrderedPair()
    {
        var prefix = "uow-transaction-lock-set:" + Guid.NewGuid() + ":";

        return (prefix + "a", prefix + "b");
    }

    private async Task _WaitUntilWaitingOnAdvisoryLockAsync(NpgsqlConnection waitingConnection)
    {
        var pid = waitingConnection.ProcessID;

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);

        for (var attempt = 0; attempt < 200; attempt++)
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT COUNT(*) FROM pg_catalog.pg_locks WHERE locktype = 'advisory' AND NOT granted AND pid = @pid";
            command.Parameters.AddWithValue("pid", pid);

            if ((long)(await command.ExecuteScalarAsync(AbortToken) ?? 0L) > 0)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), AbortToken);
        }

        throw new TimeoutException("The contender never started waiting on an advisory lock.");
    }

    private ServiceProvider _BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessDistributedLocks(setup => setup.UsePostgreSql(fixture.ConnectionString));
        services.AddUnitOfWork();

        return services.BuildServiceProvider();
    }

    private static string _CreateResourceName()
    {
        return "uow-transaction-lock-tests:" + Guid.NewGuid();
    }

    private static PostgreSqlAdvisoryLockKey _KeyFor(string resource)
    {
        // The feature encodes KeyPrefix + resource exactly as the session provider does.
        return PostgreSqlAdvisoryLockKey.FromString(
            DistributedLockOptions.DefaultKeyPrefix + resource,
            allowHashing: true
        );
    }

    private async Task<string?> _CurrentLockTimeoutAsync(NpgsqlConnection connection, IUnitOfWork unit)
    {
        var transaction = (NpgsqlTransaction)((IRelationalUnitOfWorkResource)unit.Resource!).Transaction;
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT current_setting('lock_timeout')";

        return (string?)await command.ExecuteScalarAsync(AbortToken);
    }

    private async Task<bool> _TrySessionLockAsync(PostgreSqlAdvisoryLockKey key)
    {
        // A session-scoped advisory lock bound in the key's own form (bigint or int pair; PostgreSQL treats the
        // two as separate lock spaces): false while a transaction-scoped holder exists. The connection closes at
        // the end of this method, which releases a session lock this probe did take.
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT pg_catalog.pg_try_advisory_lock({key.AddKeyParameters(command)})";

        return (bool)(await command.ExecuteScalarAsync(AbortToken) ?? false);
    }

    private async Task<long> _CountAdvisoryLocksAsync(PostgreSqlAdvisoryLockKey key)
    {
        var (key1, key2) = key.Keys;

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM pg_catalog.pg_locks l
            JOIN pg_catalog.pg_database d ON d.oid = l.database
            WHERE l.locktype = 'advisory'
              AND l.granted
              AND d.datname = pg_catalog.current_database()
              AND l.classid = @k1
              AND l.objid = @k2
            """;
        command.Parameters.AddWithValue("k1", key1);
        command.Parameters.AddWithValue("k2", key2);

        return (long)(await command.ExecuteScalarAsync(AbortToken) ?? 0L);
    }
}
