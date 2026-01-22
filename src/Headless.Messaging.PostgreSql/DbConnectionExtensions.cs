// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using System.Data;
using System.Data.Common;

namespace Headless.Messaging.PostgreSql;

#pragma warning disable CA2100 // This is wrapper
internal static class DbConnectionExtensions
{
    extension(DbConnection connection)
    {
        public async Task<int> ExecuteNonQueryAsync(
            string sql,
            DbTransaction? transaction = null,
            CancellationToken cancellationToken = default,
            params object[] sqlParams
        )
        {
            if (connection.State == ConnectionState.Closed)
            {
                await connection.OpenAsync(cancellationToken).AnyContext();
            }

            await using var command = connection.CreateCommand();
            command.CommandType = CommandType.Text;
            command.CommandText = sql;
            command.Parameters.AddRange(sqlParams);

            if (transaction is not null)
            {
                command.Transaction = transaction;
            }

            return await command.ExecuteNonQueryAsync(cancellationToken).AnyContext();
        }

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
                await connection.OpenAsync(cancellationToken).AnyContext();
            }

            await using var command = connection.CreateCommand();
            command.CommandType = CommandType.Text;
            command.CommandText = sql;
            command.Parameters.AddRange(sqlParams);

            if (transaction != null)
            {
                command.Transaction = transaction;
            }

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).AnyContext();

            T result = default!;

            if (readerFunc is not null)
            {
                result = await readerFunc(reader, cancellationToken).AnyContext();
            }

            return result;
        }

        public async Task<int> ExecuteScalarAsync(
            string sql,
            CancellationToken cancellationToken = default,
            params object[] sqlParams
        )
        {
            if (connection.State == ConnectionState.Closed)
            {
                await connection.OpenAsync(cancellationToken).AnyContext();
            }

            await using var command = connection.CreateCommand();
            command.CommandType = CommandType.Text;
            command.CommandText = sql;
            command.Parameters.AddRange(sqlParams);

            var objValue = await command.ExecuteScalarAsync(cancellationToken).AnyContext();

            return Convert.ToInt32(objValue, CultureInfo.InvariantCulture);
        }
    }
}
