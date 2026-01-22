// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using System.Data;
using System.Data.Common;

namespace Headless.Messaging.SqlServer;

#pragma warning disable CA2100
internal static class DbConnectionExtensions
{
    public static async Task<int> ExecuteNonQueryAsync(
        this DbConnection connection,
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

        if (transaction != null)
        {
            command.Transaction = transaction;
        }

        return await command.ExecuteNonQueryAsync(cancellationToken).AnyContext();
    }

    public static async Task<T> ExecuteReaderAsync<T>(
        this DbConnection connection,
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
        if (readerFunc != null)
        {
            result = await readerFunc(reader, cancellationToken).AnyContext();
        }

        return result;
    }

    public static async Task<int> ExecuteScalarAsync(
        this DbConnection connection,
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
