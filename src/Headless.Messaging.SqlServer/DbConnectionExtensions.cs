// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;

namespace Headless.Messaging.SqlServer;

#pragma warning disable CA2100
internal static class DbConnectionExtensions
{
    extension(DbConnection connection)
    {
        public async Task<int> ExecuteNonQueryAsync(
            string sql,
            DbTransaction? transaction = null,
            CancellationToken cancellationToken = default,
            params object?[] sqlParams
        )
        {
            if (connection.State == ConnectionState.Closed)
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            }

            await using var command = connection.CreateCommand();
            command.CommandType = CommandType.Text;
            command.CommandText = sql;
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
            CancellationToken cancellationToken = default,
            params object?[] sqlParams
        )
        {
            if (connection.State == ConnectionState.Closed)
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            }

            await using var command = connection.CreateCommand();
            command.CommandType = CommandType.Text;
            command.CommandText = sql;
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
            CancellationToken cancellationToken = default,
            params object?[] sqlParams
        )
        {
            if (connection.State == ConnectionState.Closed)
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            }

            await using var command = connection.CreateCommand();
            command.CommandType = CommandType.Text;
            command.CommandText = sql;
            command.Parameters.AddRange(sqlParams);

            var objValue = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

            return Convert.ToInt64(objValue, CultureInfo.InvariantCulture);
        }
    }
}
