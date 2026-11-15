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

    /// <summary>
    /// Runs <paramref name="sql"/> once per entry of <paramref name="binds"/>, in order, sending the statements together
    /// as a <see cref="DbBatch"/> where the provider supports one, and reads each statement's result set with
    /// <paramref name="read"/> and the entry's index.
    /// </summary>
    /// <remarks>
    /// A batch never carries more than <paramref name="maxParameters"/> parameters, so a long run is split into as
    /// many batches as that takes. A provider without batching (SQLite), or a run of one entry, runs one command per entry instead.
    /// </remarks>
    public static async Task ExecuteBatchReaderAsync(
        DbConnection connection,
        DbTransaction? transaction,
        string sql,
        int timeoutSeconds,
        IReadOnlyList<Action<DbCommand>> binds,
        int maxParameters,
        Func<DbDataReader, int, CancellationToken, Task> read,
        CancellationToken cancellationToken
    )
    {
        // A single statement gains nothing from a batch, so it runs as the plain command a single store sends.
        if (binds.Count == 1 || !connection.CanCreateBatch)
        {
            for (var i = 0; i < binds.Count; i++)
            {
                var index = i;
                await ExecuteReaderAsync(
                        connection,
                        transaction,
                        sql,
                        timeoutSeconds,
                        binds[i],
                        async (reader, ct) =>
                        {
                            await read(reader, index, ct).ConfigureAwait(false);

                            return true;
                        },
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }

            return;
        }

        if (connection.State == ConnectionState.Closed)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        var next = 0;
        while (next < binds.Count)
        {
            var first = next;
            await using var batch = connection.CreateBatch();
            batch.Transaction = transaction;
            batch.Timeout = timeoutSeconds;

            // The dialect binds parameters onto a DbCommand, so each statement is bound on a scratch command and its
            // parameters are moved to the batch command.
            await using (var binder = connection.CreateCommand())
            {
                var parameters = 0;
                while (next < binds.Count)
                {
                    binder.Parameters.Clear();
                    binds[next](binder);
                    if (next > first && parameters + binder.Parameters.Count > maxParameters)
                    {
                        break;
                    }

                    var statement = batch.CreateBatchCommand();
                    statement.CommandType = CommandType.Text;
                    statement.CommandText = sql;
                    var bound = new DbParameter[binder.Parameters.Count];
                    binder.Parameters.CopyTo(bound, 0);
                    binder.Parameters.Clear();
                    statement.Parameters.AddRange(bound);
                    batch.BatchCommands.Add(statement);
                    parameters += bound.Length;
                    next++;
                }

                binder.Parameters.Clear();
            }

            await using var reader = await batch.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            for (var i = first; i < next; i++)
            {
                if (i > first && !await reader.NextResultAsync(cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidOperationException("The batch returned fewer result sets than it has statements.");
                }

                await read(reader, i, cancellationToken).ConfigureAwait(false);
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
