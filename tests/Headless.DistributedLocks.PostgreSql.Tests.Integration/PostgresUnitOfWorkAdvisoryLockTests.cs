// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.DistributedLocks.PostgreSql;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Tests;

[Collection<PostgreSqlDistributedLockFixture>]
public sealed class PostgresUnitOfWorkAdvisoryLockTests(PostgreSqlDistributedLockFixture fixture) : TestBase
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
        await using var unit = await factory.BeginAsync(connection, cancellationToken: AbortToken);

        // when
        await unit.AdvisoryLocks.AcquireAsync(resource, AbortToken);

        // then — held by the unit's transaction, and a session lock on the same logical name contends with it
        (await _CountAdvisoryLocksAsync(key))
            .Should()
            .BePositive();
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
        await using (var unit = await factory.BeginAsync(connection, cancellationToken: AbortToken))
        {
            await unit.AdvisoryLocks.AcquireAsync(resource, AbortToken);
            (await _CountAdvisoryLocksAsync(key)).Should().BePositive();
        }

        // then
        (await _CountAdvisoryLocksAsync(key))
            .Should()
            .Be(0);
    }

    [Fact]
    public async Task should_try_acquire_false_while_another_unit_holds_it_and_true_after_it_completes()
    {
        // given
        await using var provider = _BuildProvider();
        var factory = provider.GetRequiredService<IUnitOfWorkFactory>();
        var resource = _CreateResourceName();

        await using var holderConnection = new NpgsqlConnection(fixture.ConnectionString);
        await using var contenderConnection = new NpgsqlConnection(fixture.ConnectionString);

        await using var holder = await factory.BeginAsync(holderConnection, cancellationToken: AbortToken);
        await holder.AdvisoryLocks.AcquireAsync(resource, AbortToken);

        // when / then
        await using var contender = await factory.BeginAsync(contenderConnection, cancellationToken: AbortToken);
        (await contender.AdvisoryLocks.TryAcquireAsync(resource, AbortToken)).Should().BeFalse();

        await holder.CompleteAsync(AbortToken);

        (await contender.AdvisoryLocks.TryAcquireAsync(resource, AbortToken)).Should().BeTrue();
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
                await unit.AdvisoryLocks.AcquireAsync(resource, ct);
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
        var act = async () => await unit.AdvisoryLocks.AcquireAsync(_CreateResourceName(), AbortToken);

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
        await using var unit = await factory.BeginAsync(connection, cancellationToken: AbortToken);
        await unit.CompleteAsync(AbortToken);

        // when
        var act = () => unit.AdvisoryLocks;

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
        await using var unit = await factory.BeginAsync(connection, cancellationToken: AbortToken);
        var locks = unit.AdvisoryLocks;
        await unit.CompleteAsync(AbortToken);

        // when
        var act = async () => await locks.AcquireAsync(_CreateResourceName(), AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Completed*");
    }

    private ServiceProvider _BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessDistributedLocks(setup => setup.UsePostgreSql(fixture.ConnectionString));
        services.AddPostgreSqlUnitOfWork();

        return services.BuildServiceProvider();
    }

    private static string _CreateResourceName()
    {
        return "uow-advisory-lock-tests:" + Guid.NewGuid();
    }

    private static PostgreSqlAdvisoryLockKey _KeyFor(string resource)
    {
        // The feature encodes KeyPrefix + resource exactly as the session provider does.
        return PostgreSqlAdvisoryLockKey.FromString(
            DistributedLockOptions.DefaultKeyPrefix + resource,
            allowHashing: true
        );
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
