// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.DistributedLocks.Postgres;
using Headless.Testing.Tests;
using Npgsql;

namespace Tests;

[Collection<PostgresDistributedLockFixture>]
public sealed class PostgresTransactionLockTests(PostgresDistributedLockFixture fixture) : TestBase
{
    private const int _MonitoringCommandTimeoutSeconds = 30;

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

            // Assert release while the holding connection is still open. If we counted after the
            // `using` block disposed the connection, connection-close would release the lock
            // regardless of commit/rollback — this proves the xact-lock drops at COMMIT itself.
            (await _CountAdvisoryLocksAsync(key))
                .Should()
                .Be(0);
        }
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

            // Assert release while the holding connection is still open so connection-close cannot be
            // the thing that drops the lock — this proves the xact-lock is released at ROLLBACK itself.
            (await _CountAdvisoryLocksAsync(key))
                .Should()
                .Be(0);
        }
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

    [Fact]
    public async Task should_restore_timeout_settings_when_strategy_uses_visible_transaction()
    {
        var resourceName = _CreateResourceName();

        await using var connection = await _OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(AbortToken);
        await using var databaseConnection = new PostgresDatabaseConnection(
            transaction,
            TimeProvider.System,
            _MonitoringCommandTimeoutSeconds
        );

        await _ExecuteAsync(connection, "SET LOCAL lock_timeout = 1234", AbortToken);
        var expectedLockTimeout = await _CurrentSettingAsync(connection, "lock_timeout", AbortToken);

        var strategy = new PostgresAdvisoryLock(isShared: false, TimeProvider.System);

        var cookie = await strategy.TryAcquireAsync(
            databaseConnection,
            resourceName,
            TimeSpan.Zero,
            AbortToken
        );

        cookie.Should().NotBeNull();
        (await _CurrentSettingAsync(connection, "lock_timeout", AbortToken)).Should().Be(expectedLockTimeout);

        await transaction.RollbackAsync(AbortToken);
    }

    [Fact]
    public async Task should_restore_timeout_settings_when_strategy_uses_invisible_external_transaction()
    {
        var resourceName = _CreateResourceName();

        await using var connection = await _OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(AbortToken);
        await using var databaseConnection = new PostgresDatabaseConnection(
            connection,
            TimeProvider.System,
            _MonitoringCommandTimeoutSeconds
        );

        await _ExecuteAsync(connection, "SET LOCAL lock_timeout = 1234", AbortToken);
        var expectedLockTimeout = await _CurrentSettingAsync(connection, "lock_timeout", AbortToken);

        var strategy = new PostgresAdvisoryLock(isShared: false, TimeProvider.System);
        var cookie = await strategy.TryAcquireAsync(
            databaseConnection,
            resourceName,
            TimeSpan.Zero,
            AbortToken
        );

        try
        {
            cookie.Should().NotBeNull();
            (await _CurrentSettingAsync(connection, "lock_timeout", AbortToken)).Should().Be(expectedLockTimeout);
        }
        finally
        {
            if (cookie is not null)
            {
                await strategy.ReleaseAsync(databaseConnection, resourceName, cookie);
            }

            await transaction.RollbackAsync(AbortToken);
        }
    }

    [Fact]
    public async Task should_acquire_when_strategy_uses_external_connection_without_transaction()
    {
        var resourceName = _CreateResourceName();

        await using var connection = await _OpenAsync();
        await using var databaseConnection = new PostgresDatabaseConnection(
            connection,
            TimeProvider.System,
            _MonitoringCommandTimeoutSeconds
        );

        var strategy = new PostgresAdvisoryLock(isShared: false, TimeProvider.System);
        var cookie = await strategy.TryAcquireAsync(
            databaseConnection,
            resourceName,
            TimeSpan.Zero,
            AbortToken
        );

        try
        {
            cookie.Should().NotBeNull();
        }
        finally
        {
            if (cookie is not null)
            {
                await strategy.ReleaseAsync(databaseConnection, resourceName, cookie);
            }
        }
    }

    [Fact]
    public async Task should_acquire_when_strategy_uses_internally_owned_transaction()
    {
        var resourceName = _CreateResourceName();
        var key = PostgresAdvisoryLockKey.FromString(resourceName, allowHashing: true);

        await using (var databaseConnection = new PostgresDatabaseConnection(
            fixture.ConnectionString,
            TimeProvider.System,
            _MonitoringCommandTimeoutSeconds
        ))
        {
            await databaseConnection.OpenAsync(AbortToken);
            await databaseConnection.BeginTransactionAsync(AbortToken);

            var strategy = new PostgresAdvisoryLock(isShared: false, TimeProvider.System);
            var cookie = await strategy.TryAcquireAsync(
                databaseConnection,
                resourceName,
                TimeSpan.Zero,
                AbortToken
            );

            cookie.Should().NotBeNull();
            (await _CountAdvisoryLocksAsync(key)).Should().BeGreaterThan(0);
        }

        (await _CountAdvisoryLocksAsync(key)).Should().Be(0);
    }

    [Fact]
    public async Task should_propagate_failed_transaction_when_strategy_uses_invisible_external_transaction()
    {
        var resourceName = _CreateResourceName();

        await using var connection = await _OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(AbortToken);
        await using var databaseConnection = new PostgresDatabaseConnection(
            connection,
            TimeProvider.System,
            _MonitoringCommandTimeoutSeconds
        );

        await _CauseTransactionFailureAsync(connection);

        var strategy = new PostgresAdvisoryLock(isShared: false, TimeProvider.System);
        var act = async () =>
            await strategy.TryAcquireAsync(databaseConnection, resourceName, TimeSpan.Zero, AbortToken);

        await act
            .Should()
            .ThrowAsync<PostgresException>()
            .Where(x => x.SqlState == PostgresErrorCodes.InFailedSqlTransaction);

        await transaction.DisposeAsync();
    }

    [Fact]
    public async Task should_propagate_failed_transaction_when_savepoint_definition_fails()
    {
        var resourceName = _CreateResourceName();

        await using var databaseConnection = new PostgresDatabaseConnection(
            fixture.ConnectionString,
            TimeProvider.System,
            _MonitoringCommandTimeoutSeconds
        );
        await databaseConnection.OpenAsync(AbortToken);
        await databaseConnection.BeginTransactionAsync(AbortToken);
        await _CauseTransactionFailureAsync(databaseConnection);

        var strategy = new PostgresAdvisoryLock(isShared: false, TimeProvider.System);
        var act = async () =>
            await strategy.TryAcquireAsync(databaseConnection, resourceName, TimeSpan.Zero, AbortToken);

        await act
            .Should()
            .ThrowAsync<PostgresException>()
            .Where(x => x.SqlState == PostgresErrorCodes.InFailedSqlTransaction);
    }

    private async Task<NpgsqlConnection> _OpenAsync()
    {
        var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);

        return connection;
    }

    private string _CreateResourceName() => "postgres-transaction-lock-tests:" + Faker.Random.Guid();

    private static async Task<string> _CurrentSettingAsync(
        NpgsqlConnection connection,
        string settingName,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT current_setting('{settingName}')";

        return (string)(await command.ExecuteScalarAsync(cancellationToken) ?? string.Empty);
    }

    private static async Task _ExecuteAsync(
        NpgsqlConnection connection,
        string commandText,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task _ExecuteAsync(
        DatabaseConnection connection,
        string commandText,
        CancellationToken cancellationToken
    )
    {
        using var command = connection.CreateCommand();
        command.SetCommandText(commandText);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task _CauseTransactionFailureAsync(NpgsqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 / 0";

        try
        {
            await command.ExecuteNonQueryAsync(AbortToken);
        }
        catch (PostgresException)
        {
            return;
        }

        throw new InvalidOperationException("The test setup query should have failed the active transaction.");
    }

    private async Task _CauseTransactionFailureAsync(DatabaseConnection connection)
    {
        try
        {
            await _ExecuteAsync(connection, "SELECT 1 / 0", AbortToken);
        }
        catch (PostgresException)
        {
            return;
        }

        throw new InvalidOperationException("The test setup query should have failed the active transaction.");
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
