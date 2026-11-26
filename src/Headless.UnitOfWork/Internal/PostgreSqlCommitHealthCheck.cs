// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;

namespace Headless.UnitOfWork.Internal;

/// <summary>Refuses to commit a PostgreSQL transaction the server has already marked as failed.</summary>
internal static class PostgreSqlCommitHealthCheck
{
    public static void Verify(DbTransaction transaction)
    {
        using var command = _CreateCommand(transaction);
        command?.ExecuteNonQuery();
    }

    public static async ValueTask VerifyAsync(DbTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = _CreateCommand(transaction);

        if (command is not null)
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static DbCommand? _CreateCommand(DbTransaction transaction)
    {
        if (!string.Equals(transaction.GetType().FullName, "Npgsql.NpgsqlTransaction", StringComparison.Ordinal))
        {
            return null;
        }

        var connection =
            transaction.Connection ?? throw new InvalidOperationException("The PostgreSQL transaction is closed.");
        var command = connection.CreateCommand();
        // PostgreSQL answers COMMIT on an aborted transaction with ROLLBACK, a command tag Npgsql's commit API
        // discards. A statement on that transaction surfaces SQLSTATE 25P02 before a COMMIT can be sent. Remove this
        // check once the Npgsql in use throws from Commit instead: https://github.com/npgsql/npgsql/issues/6775
        command.CommandText = "SELECT 1";
        command.Transaction = transaction;

        return command;
    }
}
