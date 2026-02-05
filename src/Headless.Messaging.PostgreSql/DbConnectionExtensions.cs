// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;

namespace Headless.Messaging.PostgreSql;

#pragma warning disable CA2100 // This is wrapper

/// <summary>
/// Internal extension methods for <see cref="DbConnection"/> to simplify ADO.NET operations.
/// </summary>
internal static class DbConnectionExtensions
{
    extension(DbConnection connection)
    {
        /// <summary>
        /// Executes a non-query SQL command and returns the number of affected rows.
        /// </summary>
        public async Task<int> ExecuteNonQueryAsync(
            string sql,
            DbTransaction? transaction = null,
            CancellationToken cancellationToken = default,
            params object[] sqlParams
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

            if (transaction is not null)
            {
                command.Transaction = transaction;
            }

            return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Executes a SQL query and processes results through the provided reader function.
        /// </summary>
        public async Task<T> ExecuteReaderAsync<T>(
            string sql,
            Func<DbDataReader, CancellationToken, Task<T>>? readerFunc,
            DbTransaction? transaction = null,
            CancellationToken cancellationToken = default,
            params object[] sqlParams
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

            if (readerFunc is not null)
            {
                result = await readerFunc(reader, cancellationToken).ConfigureAwait(false);
            }

            return result;
        }

        /// <summary>
        /// Executes a SQL query and returns a single integer result.
        /// </summary>
        public async Task<int> ExecuteScalarAsync(
            string sql,
            CancellationToken cancellationToken = default,
            params object[] sqlParams
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

            return Convert.ToInt32(objValue, CultureInfo.InvariantCulture);
        }
    }
}
