// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.Checks;
using Npgsql;

namespace Headless.DistributedLocks.PostgreSql;

/// <summary>
/// <see cref="DatabaseConnection"/> over Npgsql. Commands are prepared (per the Npgsql preparation guidance), the
/// PostgreSQL cancellation SqlState (<c>57014</c>) is mapped to <see cref="OperationCanceledException"/>, and the
/// monitoring probe is a server-side <c>pg_sleep</c>.
/// </summary>
internal sealed class PostgresDatabaseConnection : DatabaseConnection
{
    /// <summary>The PostgreSQL <c>query_canceled</c> SqlState (https://www.postgresql.org/docs/current/errcodes-appendix.html).</summary>
    private const string _QueryCanceledSqlState = "57014";

    /// <summary>Constructs an instance over an externally-owned <see cref="NpgsqlConnection"/>.</summary>
    /// <param name="connection">The caller-owned connection. Not disposed by this instance.</param>
    /// <param name="timeProvider">Time source used by the base class for monitoring sleeps.</param>
    /// <param name="monitoringCommandTimeoutSeconds">Timeout in seconds for monitoring probe commands.</param>
    public PostgresDatabaseConnection(
        NpgsqlConnection connection,
        TimeProvider timeProvider,
        int monitoringCommandTimeoutSeconds
    )
        : base(connection, isExternallyOwned: true, timeProvider, monitoringCommandTimeoutSeconds) { }

    /// <summary>Constructs an instance over an externally-owned <see cref="NpgsqlTransaction"/>.</summary>
    /// <param name="transaction">The caller-owned transaction. Not disposed by this instance.</param>
    /// <param name="timeProvider">Time source used by the base class for monitoring sleeps.</param>
    /// <param name="monitoringCommandTimeoutSeconds">Timeout in seconds for monitoring probe commands.</param>
    public PostgresDatabaseConnection(
        NpgsqlTransaction transaction,
        TimeProvider timeProvider,
        int monitoringCommandTimeoutSeconds
    )
        : base(transaction, isExternallyOwned: true, timeProvider, monitoringCommandTimeoutSeconds) { }

    /// <summary>Constructs an internally-owned instance from <paramref name="dataSource"/>.</summary>
    /// <param name="dataSource">
    /// The data source used to create the connection. Must not be <see langword="null"/>. The created
    /// connection is owned and disposed by the base class.
    /// </param>
    /// <param name="timeProvider">Time source used by the base class for monitoring sleeps.</param>
    /// <param name="monitoringCommandTimeoutSeconds">Timeout in seconds for monitoring probe commands.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="dataSource"/> is <see langword="null"/>.</exception>
    public PostgresDatabaseConnection(
        NpgsqlDataSource dataSource,
        TimeProvider timeProvider,
        int monitoringCommandTimeoutSeconds
    )
        : base(
            Argument.IsNotNull(dataSource).CreateConnection(),
            isExternallyOwned: false,
            timeProvider,
            monitoringCommandTimeoutSeconds
        ) { }

    /// <summary>Constructs an internally-owned instance from <paramref name="connectionString"/>.</summary>
    /// <param name="connectionString">
    /// The Npgsql connection string. The created <see cref="NpgsqlConnection"/> is owned and disposed
    /// by the base class.
    /// </param>
    /// <param name="timeProvider">Time source used by the base class for monitoring sleeps.</param>
    /// <param name="monitoringCommandTimeoutSeconds">Timeout in seconds for monitoring probe commands.</param>
    // The base DatabaseConnection takes ownership of the connection and disposes it.
#pragma warning disable CA2000
    public PostgresDatabaseConnection(
        string connectionString,
        TimeProvider timeProvider,
        int monitoringCommandTimeoutSeconds
    )
        : base(
            new NpgsqlConnection(connectionString),
            isExternallyOwned: false,
            timeProvider,
            monitoringCommandTimeoutSeconds
        ) { }
#pragma warning restore CA2000

    // See https://www.npgsql.org/doc/prepare.html
    public override bool ShouldPrepareCommands => true;

    public override bool IsCommandCancellationException(Exception exception)
    {
        return exception is PostgresException { SqlState: _QueryCanceledSqlState };
    }

    /// <summary>
    /// Implements the monitoring sleep by executing <c>SELECT pg_catalog.pg_sleep(@sleepTimeSeconds)</c>
    /// so that Npgsql's <c>StateChange</c> event fires promptly when the connection drops during the
    /// sleep. When the connection currently has an active transaction a savepoint is established before
    /// the sleep command and rolled back afterwards so a cancellation does not abort the outer transaction.
    /// </summary>
    /// <param name="sleepTime">How long to sleep server-side. Must be ≥ <see cref="TimeSpan.Zero"/>.</param>
    /// <param name="executor">
    /// Delegate used to execute each monitoring command (savepoint, sleep, rollback) through the
    /// multiplexing engine's command dispatcher.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the sleep command.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="sleepTime"/> is less than <see cref="TimeSpan.Zero"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> fires during the <c>pg_sleep</c> command; the
    /// savepoint rollback runs with <see cref="CancellationToken.None"/> to avoid leaving the transaction
    /// in an aborted state.
    /// </exception>
    public override async Task SleepAsync(
        TimeSpan sleepTime,
        Func<DatabaseCommand, CancellationToken, ValueTask<int>> executor,
        CancellationToken cancellationToken
    )
    {
        Argument.IsGreaterThanOrEqualTo(sleepTime, TimeSpan.Zero);

        // Inside a transaction, establish a savepoint so a cancelled sleep rolls back without aborting the whole
        // transaction.
        const string savePointName = "headless_distributed_locks_postgres_connection_sleep";

        var hasTransaction = HasTransaction;

        if (hasTransaction)
        {
            using var setSavePointCommand = CreateCommand();
            setSavePointCommand.SetCommandText("SAVEPOINT " + savePointName);
            await executor(setSavePointCommand, CancellationToken.None).ConfigureAwait(false);
        }

        try
        {
            using var sleepCommand = CreateCommand();
            sleepCommand.SetCommandText("SELECT pg_catalog.pg_sleep(@sleepTimeSeconds)");
            sleepCommand.AddParameter("sleepTimeSeconds", sleepTime.TotalSeconds, DbType.Double);
            sleepCommand.SetTimeout(sleepTime);
            await executor(sleepCommand, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (hasTransaction)
            {
                using var rollBackSavePointCommand = CreateCommand();
                rollBackSavePointCommand.SetCommandText("ROLLBACK TO SAVEPOINT " + savePointName);
                await executor(rollBackSavePointCommand, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }
}
