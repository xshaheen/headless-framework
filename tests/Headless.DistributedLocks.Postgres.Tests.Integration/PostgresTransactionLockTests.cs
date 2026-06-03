// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks.Postgres;
using Headless.Testing.Tests;
using Npgsql;

namespace Tests;

[Collection<PostgresDistributedLockFixture>]
public sealed class PostgresTransactionLockTests(PostgresDistributedLockFixture fixture) : TestBase
{
    [Fact]
    public async Task should_release_transaction_lock_when_transaction_commits()
    {
        var key = new PostgresAdvisoryLockKey(Faker.Random.Long());

        await using (var connection = await _OpenAsync())
        await using (var transaction = await connection.BeginTransactionAsync(AbortToken))
        {
            await PostgresDistributedLock.AcquireWithTransactionAsync(key, transaction, AbortToken);

            (await _CountAdvisoryLocksAsync(key)).Should().BeGreaterThan(0);

            await transaction.CommitAsync(AbortToken);
        }

        (await _CountAdvisoryLocksAsync(key)).Should().Be(0);
    }

    [Fact]
    public async Task should_release_transaction_lock_when_transaction_rolls_back()
    {
        var key = new PostgresAdvisoryLockKey(Faker.Random.Long());

        await using (var connection = await _OpenAsync())
        await using (var transaction = await connection.BeginTransactionAsync(AbortToken))
        {
            await PostgresDistributedLock.AcquireWithTransactionAsync(key, transaction, AbortToken);

            (await _CountAdvisoryLocksAsync(key)).Should().BeGreaterThan(0);

            await transaction.RollbackAsync(AbortToken);
        }

        (await _CountAdvisoryLocksAsync(key)).Should().Be(0);
    }

    [Fact]
    public async Task should_fail_try_acquire_when_held_by_another_transaction()
    {
        var key = new PostgresAdvisoryLockKey(Faker.Random.Long());

        await using var holderConnection = await _OpenAsync();
        await using var holderTransaction = await holderConnection.BeginTransactionAsync(AbortToken);
        await PostgresDistributedLock.AcquireWithTransactionAsync(key, holderTransaction, AbortToken);

        await using var contenderConnection = await _OpenAsync();
        await using var contenderTransaction = await contenderConnection.BeginTransactionAsync(AbortToken);

        var acquired = await PostgresDistributedLock.TryAcquireWithTransactionAsync(
            key,
            contenderTransaction,
            AbortToken
        );

        acquired.Should().BeFalse();

        // After the holder commits, a fresh transaction can take the lock.
        await holderTransaction.CommitAsync(AbortToken);

        await using var nextConnection = await _OpenAsync();
        await using var nextTransaction = await nextConnection.BeginTransactionAsync(AbortToken);

        (await PostgresDistributedLock.TryAcquireWithTransactionAsync(key, nextTransaction, AbortToken))
            .Should()
            .BeTrue();
    }

    [Fact]
    public async Task should_throw_when_transaction_has_no_connection()
    {
        await using var connection = await _OpenAsync();
        var transaction = await connection.BeginTransactionAsync(AbortToken);
        await transaction.CommitAsync(AbortToken);
        await transaction.DisposeAsync();

        var key = new PostgresAdvisoryLockKey(Faker.Random.Long());

        var act = async () => await PostgresDistributedLock.AcquireWithTransactionAsync(key, transaction, AbortToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private async Task<NpgsqlConnection> _OpenAsync()
    {
        var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);

        return connection;
    }

    private async Task<long> _CountAdvisoryLocksAsync(PostgresAdvisoryLockKey key)
    {
        var keys = key.Keys;

        await using var connection = await _OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM pg_catalog.pg_locks l
            JOIN pg_catalog.pg_database d ON d.oid = l.database
            WHERE l.locktype = 'advisory'
              AND l.granted
              AND d.datname = pg_catalog.current_database()
              AND l.classid = @classId
              AND l.objid = @objId
              AND l.objsubid = @objSubId
            """;
        command.Parameters.AddWithValue("classId", keys.Key1);
        command.Parameters.AddWithValue("objId", keys.Key2);
        command.Parameters.AddWithValue("objSubId", (short)(key.HasSingleKey ? 1 : 2));

        return (long)(await command.ExecuteScalarAsync(AbortToken) ?? 0L);
    }
}
