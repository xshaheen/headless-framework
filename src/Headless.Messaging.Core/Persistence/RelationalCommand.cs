// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;

namespace Headless.Messaging.Persistence;

#pragma warning disable CA2100 // SQL text is rendered by the storage from dialect output and fixed fragments; every value is a parameter.

/// <summary>Runs one command on a connection, opening it first when it is closed.</summary>
internal static class RelationalCommand
{
    public static async Task<int> ExecuteNonQueryAsync(
        DbConnection connection,
        DbTransaction? transaction,
        string sql,
        int timeoutSeconds,
        Action<DbCommand>? bind,
        CancellationToken cancellationToken
    )
    {
        await using var command = await _CreateAsync(
                connection,
                transaction,
                sql,
                timeoutSeconds,
                bind,
                cancellationToken
            )
            .ConfigureAwait(false);

        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<T> ExecuteReaderAsync<T>(
        DbConnection connection,
        DbTransaction? transaction,
        string sql,
        int timeoutSeconds,
        Action<DbCommand>? bind,
        Func<DbDataReader, CancellationToken, Task<T>> read,
        CancellationToken cancellationToken
    )
    {
        await using var command = await _CreateAsync(
                connection,
                transaction,
                sql,
                timeoutSeconds,
                bind,
                cancellationToken
            )
            .ConfigureAwait(false);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        return await read(reader, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<object?> ExecuteScalarAsync(
        DbConnection connection,
        DbTransaction? transaction,
        string sql,
        int timeoutSeconds,
        Action<DbCommand>? bind,
        CancellationToken cancellationToken
    )
    {
        await using var command = await _CreateAsync(
                connection,
                transaction,
                sql,
                timeoutSeconds,
                bind,
                cancellationToken
            )
            .ConfigureAwait(false);

        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Advances <paramref name="reader"/> past the result sets of the statements that precede the one being read, such
    /// as the locking read a fenced transition follows.
    /// </summary>
    public static async Task SkipResultsAsync(DbDataReader reader, int count, CancellationToken cancellationToken)
    {
        for (var i = 0; i < count; i++)
        {
            if (!await reader.NextResultAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("The batch returned fewer result sets than it has statements.");
            }
        }
    }

    private static async Task<DbCommand> _CreateAsync(
        DbConnection connection,
        DbTransaction? transaction,
        string sql,
        int timeoutSeconds,
        Action<DbCommand>? bind,
        CancellationToken cancellationToken
    )
    {
        if (connection.State == ConnectionState.Closed)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        var command = connection.CreateCommand();
        command.CommandType = CommandType.Text;
        command.CommandText = sql;
        command.CommandTimeout = timeoutSeconds;
        command.Transaction = transaction;
        bind?.Invoke(command);

        return command;
    }
}
