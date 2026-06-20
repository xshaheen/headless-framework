// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.Checks;
using Npgsql;

namespace Headless.DistributedLocks.Postgres;

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
    public PostgresDatabaseConnection(
        NpgsqlConnection connection,
        TimeProvider timeProvider,
        int monitoringCommandTimeoutSeconds
    )
        : base(connection, isExternallyOwned: true, timeProvider, monitoringCommandTimeoutSeconds) { }

    /// <summary>Constructs an instance over an externally-owned <see cref="NpgsqlTransaction"/>.</summary>
    public PostgresDatabaseConnection(
        NpgsqlTransaction transaction,
        TimeProvider timeProvider,
        int monitoringCommandTimeoutSeconds
    )
        : base(transaction, isExternallyOwned: true, timeProvider, monitoringCommandTimeoutSeconds) { }

    /// <summary>Constructs an internally-owned instance from <paramref name="dataSource"/>.</summary>
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

    public override bool IsCommandCancellationException(Exception exception) =>
        exception is PostgresException { SqlState: _QueryCanceledSqlState };

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
