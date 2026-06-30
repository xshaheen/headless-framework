// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections;
using System.Data;
using System.Data.Common;
using Headless.DistributedLocks;
using Headless.DistributedLocks.PostgreSql;
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

            (await _CountAdvisoryLocksAsync(key)).Should().BePositive();

            await transaction.CommitAsync(AbortToken);

            // then release while the holding connection is still open. If we counted after the
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

            (await _CountAdvisoryLocksAsync(key)).Should().BePositive();

            await transaction.RollbackAsync(AbortToken);

            // then release while the holding connection is still open so connection-close cannot be
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

        var cookie = await strategy.TryAcquireAsync(databaseConnection, resourceName, TimeSpan.Zero, AbortToken);

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
        var cookie = await strategy.TryAcquireAsync(databaseConnection, resourceName, TimeSpan.Zero, AbortToken);

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
        var cookie = await strategy.TryAcquireAsync(databaseConnection, resourceName, TimeSpan.Zero, AbortToken);

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

        await using (
            var databaseConnection = new PostgresDatabaseConnection(
                fixture.ConnectionString,
                TimeProvider.System,
                _MonitoringCommandTimeoutSeconds
            )
        )
        {
            await databaseConnection.OpenAsync(AbortToken);
            await databaseConnection.BeginTransactionAsync(AbortToken);

            var strategy = new PostgresAdvisoryLock(isShared: false, TimeProvider.System);
            var cookie = await strategy.TryAcquireAsync(databaseConnection, resourceName, TimeSpan.Zero, AbortToken);

            cookie.Should().NotBeNull();
            (await _CountAdvisoryLocksAsync(key)).Should().BePositive();
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

        await act.Should()
            .ThrowAsync<PostgresException>()
            .Where(x => x.SqlState == PostgresErrorCodes.InFailedSqlTransaction);

        await transaction.DisposeAsync();
    }

    [Fact]
    public async Task should_propagate_external_savepoint_failure_when_sqlstate_is_not_no_active_transaction()
    {
        await using var databaseConnection = new ThrowingSavePointDatabaseConnection();

        var strategy = new PostgresAdvisoryLock(isShared: false, TimeProvider.System);
        var act = async () =>
            await strategy.TryAcquireAsync(databaseConnection, _CreateResourceName(), TimeSpan.Zero, AbortToken);

        await act.Should()
            .ThrowAsync<PostgresException>()
            .Where(x => x.SqlState == PostgresErrorCodes.InFailedSqlTransaction);
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

        await act.Should()
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

    private sealed class ThrowingSavePointDatabaseConnection()
        : DatabaseConnection(new ThrowingSavePointDbConnection(), isExternallyOwned: true, TimeProvider.System)
    {
        public override bool ShouldPrepareCommands => false;

        public override bool IsCommandCancellationException(Exception exception) => false;

        public override Task SleepAsync(
            TimeSpan sleepTime,
            Func<DatabaseCommand, CancellationToken, ValueTask<int>> executor,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();
    }

    private sealed class ThrowingSavePointDbConnection : DbConnection
    {
        [AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;

        public override string Database => "fake";

        public override string DataSource => "fake";

        public override string ServerVersion => "fake";

        public override ConnectionState State => ConnectionState.Open;

        public override void ChangeDatabase(string databaseName) { }

        public override void Close() { }

        public override void Open() { }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
            throw new NotSupportedException();

        protected override DbCommand CreateDbCommand() => new ThrowingSavePointDbCommand(this);
    }

    private sealed class ThrowingSavePointDbCommand(DbConnection connection) : DbCommand
    {
        private readonly FakeDbParameterCollection _parameters = new();

        [AllowNull]
        public override string CommandText { get; set; } = string.Empty;

        public override int CommandTimeout { get; set; }

        public override CommandType CommandType { get; set; }

        public override bool DesignTimeVisible { get; set; }

        public override UpdateRowSource UpdatedRowSource { get; set; }

        protected override DbConnection? DbConnection { get; set; } = connection;

        protected override DbParameterCollection DbParameterCollection => _parameters;

        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel() { }

        public override int ExecuteNonQuery() => throw _CreateSavePointException();

        public override object ExecuteScalar() => 0L;

        public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken) =>
            CommandText?.StartsWith("SAVEPOINT ", StringComparison.Ordinal) == true
                ? Task.FromException<int>(_CreateSavePointException())
                : Task.FromResult(0);

        public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken) =>
            Task.FromResult<object?>(0L);

        public override void Prepare() { }

        protected override DbParameter CreateDbParameter() => new FakeDbParameter();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) =>
            throw new NotSupportedException();

        private static PostgresException _CreateSavePointException() =>
            new("current transaction is aborted", "ERROR", "ERROR", PostgresErrorCodes.InFailedSqlTransaction);
    }

    private sealed class FakeDbParameter : DbParameter
    {
        public override DbType DbType { get; set; }

        public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;

        public override bool IsNullable { get; set; }

        [AllowNull]
        public override string ParameterName { get; set; } = string.Empty;

        [AllowNull]
        public override string SourceColumn { get; set; } = string.Empty;

        public override object? Value { get; set; }

        public override bool SourceColumnNullMapping { get; set; }

        public override int Size { get; set; }

        public override void ResetDbType() { }
    }

    private sealed class FakeDbParameterCollection : DbParameterCollection
    {
        private readonly List<DbParameter> _parameters = [];

        public override int Count => _parameters.Count;

        public override object SyncRoot => ((ICollection)_parameters).SyncRoot;

        public override int Add(object value)
        {
            _parameters.Add((DbParameter)value);

            return _parameters.Count - 1;
        }

        public override void AddRange(Array values)
        {
            foreach (var value in values)
            {
                Add(value);
            }
        }

        public override void Clear() => _parameters.Clear();

        public override bool Contains(object value) => _parameters.Contains((DbParameter)value);

        public override bool Contains(string value) =>
            _parameters.Exists(parameter => string.Equals(parameter.ParameterName, value, StringComparison.Ordinal));

        public override void CopyTo(Array array, int index) => ((ICollection)_parameters).CopyTo(array, index);

        public override IEnumerator GetEnumerator() => _parameters.GetEnumerator();

        public override int IndexOf(object value) => _parameters.IndexOf((DbParameter)value);

        public override int IndexOf(string parameterName) =>
            _parameters.FindIndex(parameter =>
                string.Equals(parameter.ParameterName, parameterName, StringComparison.Ordinal)
            );

        public override void Insert(int index, object value) => _parameters.Insert(index, (DbParameter)value);

        public override void Remove(object value) => _parameters.Remove((DbParameter)value);

        public override void RemoveAt(int index) => _parameters.RemoveAt(index);

        public override void RemoveAt(string parameterName)
        {
            var index = IndexOf(parameterName);

            if (index >= 0)
            {
                RemoveAt(index);
            }
        }

        protected override DbParameter GetParameter(int index) => _parameters[index];

        protected override DbParameter GetParameter(string parameterName) => _parameters[IndexOf(parameterName)];

        protected override void SetParameter(int index, DbParameter value) => _parameters[index] = value;

        protected override void SetParameter(string parameterName, DbParameter value)
        {
            var index = IndexOf(parameterName);

            if (index >= 0)
            {
                _parameters[index] = value;
            }
            else
            {
                _parameters.Add(value);
            }
        }
    }
}
