// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;

namespace Headless.Messaging.SqlServer;

#pragma warning disable CA2100, CA1068 // Wrapper keeps SQL params last, so timeout sits after cancellation.
internal static class DbConnectionExtensions
{
    extension(DbConnection connection)
    {
        public async Task<int> ExecuteNonQueryAsync(
            string sql,
            DbTransaction? transaction = null,
            TimeSpan? commandTimeout = null,
            object?[]? sqlParams = null,
            CancellationToken cancellationToken = default
        )
        {
            sqlParams ??= [];

            if (connection.State == ConnectionState.Closed)
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            }

            await using var command = connection.CreateCommand();
            command.CommandType = CommandType.Text;
            command.CommandText = sql;
            if (commandTimeout.HasValue)
            {
                command.CommandTimeout = (int)commandTimeout.Value.TotalSeconds;
            }
            command.Parameters.AddRange(sqlParams);

            if (transaction != null)
            {
                command.Transaction = transaction;
            }

            return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<T> ExecuteReaderAsync<T>(
            string sql,
            Func<DbDataReader, CancellationToken, Task<T>>? readerFunc,
            DbTransaction? transaction = null,
            TimeSpan? commandTimeout = null,
            object?[]? sqlParams = null,
            CancellationToken cancellationToken = default
        )
        {
            sqlParams ??= [];

            if (connection.State == ConnectionState.Closed)
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            }

            await using var command = connection.CreateCommand();
            command.CommandType = CommandType.Text;
            command.CommandText = sql;
            if (commandTimeout.HasValue)
            {
                command.CommandTimeout = (int)commandTimeout.Value.TotalSeconds;
            }
            command.Parameters.AddRange(sqlParams);

            if (transaction != null)
            {
                command.Transaction = transaction;
            }

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            T result = default!;
            if (readerFunc != null)
            {
                result = await readerFunc(reader, cancellationToken).ConfigureAwait(false);
            }

            return result;
        }

        public async Task<long> ExecuteScalarAsync(
            string sql,
            TimeSpan? commandTimeout = null,
            object?[]? sqlParams = null,
            CancellationToken cancellationToken = default
        )
        {
            sqlParams ??= [];

            if (connection.State == ConnectionState.Closed)
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            }

            await using var command = connection.CreateCommand();
            command.CommandType = CommandType.Text;
            command.CommandText = sql;
            if (commandTimeout.HasValue)
            {
                command.CommandTimeout = (int)commandTimeout.Value.TotalSeconds;
            }
            command.Parameters.AddRange(sqlParams);

            var objValue = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

            return Convert.ToInt64(objValue, CultureInfo.InvariantCulture);
        }
    }
}
