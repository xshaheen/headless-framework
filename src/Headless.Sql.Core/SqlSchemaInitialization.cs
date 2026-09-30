// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;

namespace Headless.Sql;

/// <summary>
/// The one protocol every storage initializer runs: the dialect's schema script inside a transaction, under the
/// feature's initialization lock, rerun once when a creator outside that lock committed the same object first.
/// </summary>
[PublicAPI]
public static class SqlSchemaInitialization
{
    /// <summary>Runs <paramref name="script" /> on <paramref name="connection" />, opening it when closed.</summary>
    /// <param name="dialect">The engine's dialect.</param>
    /// <param name="connection">A connection to the database the objects belong in.</param>
    /// <param name="script">The objects to create.</param>
    /// <param name="lockResource">
    /// The feature's lock resource, keyed on the objects it creates, so replicas starting together serialize their DDL
    /// and two configurations that point at different schemas never wait on each other.
    /// </param>
    /// <param name="commandTimeoutSeconds">The command timeout.</param>
    /// <param name="onRaceAbsorbed">Called with the failure a first attempt absorbed before its rerun.</param>
    /// <param name="cancellationToken">Token used to cancel the initialization.</param>
    public static async Task RunAsync(
        ISqlDialect dialect,
        DbConnection connection,
        SqlSchemaScript script,
        string lockResource,
        int commandTimeoutSeconds,
        Action<Exception>? onRaceAbsorbed,
        CancellationToken cancellationToken
    )
    {
        var sql = dialect.Render(script);

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        // The locks serialize only the initializers that take them; a foreign creator (a consumer's migration) can
        // still commit the same CREATE first. That fails this transaction, and the rollback takes every object it
        // created with it. The foreign creator has committed by then, so one rerun in a fresh transaction passes the
        // existence guards and recreates what the rollback discarded. A second failure is not a race and propagates,
        // so the initializer never reports success with an object missing.
        for (var attempt = 1; ; attempt++)
        {
            await using var transaction = await connection
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            try
            {
                await using var command = connection.CreateCommand();
#pragma warning disable CA2100 // Rendered from validated identifiers and the dialect's own statements; values are parameters.
                command.CommandText = sql;
#pragma warning restore CA2100
                command.Transaction = transaction;
                command.CommandTimeout = commandTimeoutSeconds;
                dialect.AddParameter(command, script.LockResourceParameter, SqlColumnType.Text(255), lockResource);

                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                return;
            }
            catch (Exception e)
                when (attempt == 1
                    && dialect.Classify(e) is SqlErrorKind.DuplicateObject or SqlErrorKind.UniqueViolation
                )
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                onRaceAbsorbed?.Invoke(e);
            }
        }
    }
}
