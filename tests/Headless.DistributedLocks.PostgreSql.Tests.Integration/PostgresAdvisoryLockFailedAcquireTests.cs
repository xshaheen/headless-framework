// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.DistributedLocks;
using Headless.DistributedLocks.PostgreSql;
using Headless.Testing.Tests;
using Npgsql;

namespace Tests;

/// <summary>
/// A session-scoped advisory acquire whose client gives up after the server already granted the lock (a cancel or a
/// client command timeout landing after <c>pg_try_advisory_lock</c> returned) must not leave the lock held on the
/// connection. Timing cannot reproduce that window reliably, so these tests run the real statement and then raise the
/// client-side failure from a connection wrapper.
/// </summary>
[Collection<PostgreSqlDistributedLockFixture>]
public sealed class PostgresAdvisoryLockFailedAcquireTests(PostgreSqlDistributedLockFixture fixture) : TestBase
{
    public enum ClientFailure
    {
        CommandTimeout,
        Cancellation,
    }

    [Theory]
    [InlineData(ClientFailure.CommandTimeout, false)]
    [InlineData(ClientFailure.CommandTimeout, true)]
    [InlineData(ClientFailure.Cancellation, false)]
    [InlineData(ClientFailure.Cancellation, true)]
    public async Task should_release_a_granted_session_lock_when_the_client_fails_the_try_acquire(
        ClientFailure failure,
        bool isShared
    )
    {
        var resourceName = _CreateResourceName();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        await using var inner = await _OpenAsync(fixture.ConnectionString);
        await using var connection = new FaultInjectingDatabaseConnection(inner);
        connection.InjectAfterAcquire(async () =>
        {
            if (failure == ClientFailure.CommandTimeout)
            {
                return new NpgsqlException("Exception while reading from stream", new TimeoutException());
            }

            // Npgsql's own shape when the token fires after the server already answered.
            await cancellation.CancelAsync();

            return new OperationCanceledException(cancellation.Token);
        });

        var strategy = new PostgresAdvisoryLock(isShared, TimeProvider.System);
        var act = async () =>
            await strategy.TryAcquireAsync(connection, resourceName, TimeSpan.Zero, cancellation.Token);

        if (failure == ClientFailure.CommandTimeout)
        {
            await act.Should().ThrowAsync<NpgsqlException>().WithInnerException<NpgsqlException, TimeoutException>();
        }
        else
        {
            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        connection.AcquireFaulted.Should().BeTrue("the fault must fire after the server ran the acquire");
        inner.State.Should().Be(ConnectionState.Open, "the failing connection stays usable, as a pooled one would");
        (await _TryAcquireExclusiveOnAnotherSessionAsync(strategy, resourceName)).Should().BeTrue();
    }

    [Fact]
    public async Task should_report_lock_cleanup_failure_when_the_held_check_fails_after_a_failed_try_acquire()
    {
        var resourceName = _CreateResourceName();
        await using var inner = await _OpenAsync(fixture.ConnectionString);
        await using var connection = new FaultInjectingDatabaseConnection(inner);
        var acquireFailure = new NpgsqlException("Exception while reading from stream", new TimeoutException());
        var cleanupFailure = new NpgsqlException("The held-lock check failed");
        connection.InjectAfterAcquire(() => ValueTask.FromResult<Exception>(acquireFailure));
        connection.InjectAfterHeldCheckFollowingAcquire(() => ValueTask.FromResult<Exception>(cleanupFailure));

        var strategy = new PostgresAdvisoryLock(isShared: false, TimeProvider.System);
        var act = async () => await strategy.TryAcquireAsync(connection, resourceName, TimeSpan.Zero, AbortToken);

        var thrown = await act.Should().ThrowAsync<LockCleanupFailedException>();
        thrown.Which.Failures.Should().Equal(acquireFailure, cleanupFailure);
        thrown.Which.Message.Should().Contain(resourceName);
    }

    [Fact]
    public async Task should_rethrow_the_acquire_failure_without_cleanup_when_the_connection_died()
    {
        var resourceName = _CreateResourceName();

        // Without pooling, closing the connection ends the backend, which is what a broken connection does.
        var connectionString = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Pooling = false };
        await using var inner = await _OpenAsync(connectionString.ConnectionString);
        await using var connection = new FaultInjectingDatabaseConnection(inner);
        var acquireFailure = new NpgsqlException("Exception while reading from stream", new TimeoutException());
        connection.InjectAfterAcquire(async () =>
        {
            await inner.CloseAsync();

            return acquireFailure;
        });

        var strategy = new PostgresAdvisoryLock(isShared: false, TimeProvider.System);
        var act = async () => await strategy.TryAcquireAsync(connection, resourceName, TimeSpan.Zero, AbortToken);

        (await act.Should().ThrowAsync<NpgsqlException>()).Which.Should().BeSameAs(acquireFailure);
        (await _TryAcquireExclusiveOnAnotherSessionAsync(strategy, resourceName)).Should().BeTrue();
    }

    [Fact]
    public async Task should_report_not_acquired_when_a_bounded_wait_hits_its_lock_timeout()
    {
        var resourceName = _CreateResourceName();
        var strategy = new PostgresAdvisoryLock(isShared: false, TimeProvider.System);
        var key = (PostgreSqlAdvisoryLockKey)strategy.GetHeldLockIdentity(resourceName);

        await using var holder = await _OpenAsync(fixture.ConnectionString);
        await _ExecuteScalarAsync(holder, key, "pg_advisory_lock");

        await using var inner = await _OpenAsync(fixture.ConnectionString);
        await using var connection = new FaultInjectingDatabaseConnection(inner);

        var result = await strategy.TryAcquireAsync(
            connection,
            resourceName,
            TimeSpan.FromMilliseconds(100),
            AbortToken
        );

        result.Should().BeNull();
        inner.State.Should().Be(ConnectionState.Open);
    }

    [Fact]
    public async Task should_keep_the_lock_when_a_bounded_wait_reports_lock_timeout_after_the_grant()
    {
        var resourceName = _CreateResourceName();
        await using var inner = await _OpenAsync(fixture.ConnectionString);
        await using var connection = new FaultInjectingDatabaseConnection(inner);
        connection.InjectAfterAcquire(() =>
            ValueTask.FromResult<Exception>(
                new PostgresException(
                    "canceling statement due to lock timeout",
                    "ERROR",
                    "ERROR",
                    PostgresErrorCodes.LockNotAvailable
                )
            )
        );

        var strategy = new PostgresAdvisoryLock(isShared: false, TimeProvider.System);
        var cookie = await strategy.TryAcquireAsync(connection, resourceName, TimeSpan.FromSeconds(5), AbortToken);

        cookie.Should().NotBeNull("the lock_timeout lost the race to the grant, so the lock is held");
        (await _TryAcquireExclusiveOnAnotherSessionAsync(strategy, resourceName)).Should().BeFalse();

        await strategy.ReleaseAsync(connection, resourceName, cookie!);
        (await _TryAcquireExclusiveOnAnotherSessionAsync(strategy, resourceName)).Should().BeTrue();
    }

    [Fact]
    public async Task should_release_a_granted_session_lock_when_the_acquire_reports_a_deadlock()
    {
        var resourceName = _CreateResourceName();
        await using var inner = await _OpenAsync(fixture.ConnectionString);
        await using var connection = new FaultInjectingDatabaseConnection(inner);
        var deadlock = new PostgresException(
            "deadlock detected",
            "ERROR",
            "ERROR",
            PostgresErrorCodes.DeadlockDetected
        );
        connection.InjectAfterAcquire(() => ValueTask.FromResult<Exception>(deadlock));

        var strategy = new PostgresAdvisoryLock(isShared: false, TimeProvider.System);
        var act = async () =>
            await strategy.TryAcquireAsync(connection, resourceName, TimeSpan.FromSeconds(5), AbortToken);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithInnerException<PostgresException>();
        (await _TryAcquireExclusiveOnAnotherSessionAsync(strategy, resourceName)).Should().BeTrue();
    }

    private async Task<bool> _TryAcquireExclusiveOnAnotherSessionAsync(
        PostgresAdvisoryLock strategy,
        string resourceName
    )
    {
        var key = (PostgreSqlAdvisoryLockKey)strategy.GetHeldLockIdentity(resourceName);
        await using var other = await _OpenAsync(fixture.ConnectionString);

        if (!(bool)(await _ExecuteScalarAsync(other, key, "pg_try_advisory_lock"))!)
        {
            return false;
        }

        await _ExecuteScalarAsync(other, key, "pg_advisory_unlock");

        return true;
    }

    private static async Task<object?> _ExecuteScalarAsync(
        NpgsqlConnection connection,
        PostgreSqlAdvisoryLockKey key,
        string function
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT pg_catalog.{function}({key.AddKeyParameters(command)})";

        return await command.ExecuteScalarAsync(AbortToken);
    }

    private static async Task<NpgsqlConnection> _OpenAsync(string connectionString)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(AbortToken);

        return connection;
    }

    private string _CreateResourceName()
    {
        return "postgres-advisory-lock-failed-acquire-tests:" + Faker.Random.Guid();
    }

#pragma warning disable CA2000 // The wrapper is externally owned, so DatabaseConnection never disposes it; the test disposes the inner connection.
    /// <summary>
    /// An externally-owned connection over a real <see cref="NpgsqlConnection"/> that raises a client-side failure
    /// after the server executed the acquire statement, and optionally after the first held-lock check that follows.
    /// </summary>
    private sealed class FaultInjectingDatabaseConnection(NpgsqlConnection inner)
        : DatabaseConnection(new FaultInjectingDbConnection(inner), isExternallyOwned: true, TimeProvider.System)
#pragma warning restore CA2000
    {
        private FaultInjectingDbConnection Faults => (FaultInjectingDbConnection)InnerConnection;

        public bool AcquireFaulted => Faults.AcquireFaulted;

        public override bool ShouldPrepareCommands => true;

        public void InjectAfterAcquire(Func<ValueTask<Exception>> failure)
        {
            Faults.AfterAcquire = failure;
        }

        public void InjectAfterHeldCheckFollowingAcquire(Func<ValueTask<Exception>> failure)
        {
            Faults.AfterHeldCheck = failure;
        }

        public override bool IsCommandCancellationException(Exception exception)
        {
            return exception is PostgresException { SqlState: PostgresErrorCodes.QueryCanceled };
        }

        public override Task SleepAsync(
            TimeSpan sleepTime,
            Func<DatabaseCommand, CancellationToken, ValueTask<int>> executor,
            CancellationToken cancellationToken
        )
        {
            throw new NotSupportedException();
        }

        private sealed class FaultInjectingDbConnection(NpgsqlConnection inner) : DbConnection
        {
            public Func<ValueTask<Exception>>? AfterAcquire { get; set; }

            public Func<ValueTask<Exception>>? AfterHeldCheck { get; set; }

            public bool AcquireFaulted { get; private set; }

            public async ValueTask ThrowIfFaultInjectedAsync(string commandText)
            {
                if (!AcquireFaulted && AfterAcquire is { } afterAcquire && _IsAdvisoryAcquire(commandText))
                {
                    AcquireFaulted = true;

                    throw await afterAcquire();
                }

                if (
                    AcquireFaulted
                    && AfterHeldCheck is { } afterHeldCheck
                    && commandText.Contains("pg_locks", StringComparison.Ordinal)
                )
                {
                    AfterHeldCheck = null;

                    throw await afterHeldCheck();
                }
            }

            private static bool _IsAdvisoryAcquire(string commandText)
            {
                return commandText.Contains("pg_try_advisory_lock", StringComparison.Ordinal)
                    || commandText.Contains("pg_advisory_lock", StringComparison.Ordinal);
            }

            [AllowNull]
            public override string ConnectionString
            {
                get => inner.ConnectionString;
                set => inner.ConnectionString = value;
            }

            public override string Database => inner.Database;

            public override string DataSource => inner.DataSource;

            public override string ServerVersion => inner.ServerVersion;

            public override ConnectionState State => inner.State;

            public override void ChangeDatabase(string databaseName)
            {
                inner.ChangeDatabase(databaseName);
            }

            public override void Close()
            {
                inner.Close();
            }

            public override void Open()
            {
                inner.Open();
            }

            protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
            {
                throw new NotSupportedException();
            }

            protected override DbCommand CreateDbCommand()
            {
                return new FaultInjectingDbCommand(this, inner.CreateCommand());
            }
        }

        private sealed class FaultInjectingDbCommand(FaultInjectingDbConnection connection, NpgsqlCommand inner)
            : DbCommand
        {
            [AllowNull]
            public override string CommandText
            {
                get => inner.CommandText;
                set => inner.CommandText = value;
            }

            public override int CommandTimeout
            {
                get => inner.CommandTimeout;
                set => inner.CommandTimeout = value;
            }

            public override CommandType CommandType
            {
                get => inner.CommandType;
                set => inner.CommandType = value;
            }

            public override bool DesignTimeVisible
            {
                get => inner.DesignTimeVisible;
                set => inner.DesignTimeVisible = value;
            }

            public override UpdateRowSource UpdatedRowSource
            {
                get => inner.UpdatedRowSource;
                set => inner.UpdatedRowSource = value;
            }

            protected override DbConnection? DbConnection
            {
                get => connection;
                set => throw new NotSupportedException();
            }

            protected override DbParameterCollection DbParameterCollection => inner.Parameters;

            protected override DbTransaction? DbTransaction
            {
                get => inner.Transaction;
                set => inner.Transaction = (NpgsqlTransaction?)value;
            }

            public override void Cancel()
            {
                inner.Cancel();
            }

            public override int ExecuteNonQuery()
            {
                throw new NotSupportedException();
            }

            public override object? ExecuteScalar()
            {
                throw new NotSupportedException();
            }

            public override async Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
            {
                var result = await inner.ExecuteNonQueryAsync(cancellationToken);
                await connection.ThrowIfFaultInjectedAsync(inner.CommandText);

                return result;
            }

            public override async Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
            {
                var result = await inner.ExecuteScalarAsync(cancellationToken);
                await connection.ThrowIfFaultInjectedAsync(inner.CommandText);

                return result;
            }

            public override void Prepare()
            {
                inner.Prepare();
            }

            public override Task PrepareAsync(CancellationToken cancellationToken = default)
            {
                return inner.PrepareAsync(cancellationToken);
            }

            protected override DbParameter CreateDbParameter()
            {
                return inner.CreateParameter();
            }

            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
            {
                throw new NotSupportedException();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    inner.Dispose();
                }

                base.Dispose(disposing);
            }
        }
    }
}
