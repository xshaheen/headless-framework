// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using System.Data;
using System.Data.Common;

namespace Framework.Messages;

internal static class DbConnectionExtensions
{
    public static async Task<int> ExecuteNonQueryAsync(
        this DbConnection connection,
        string sql,
        DbTransaction? transaction = null,
        params object[] sqlParams
    )
    {
        if (connection.State == ConnectionState.Closed)
        {
            await connection.OpenAsync().AnyContext();
        }

        var command = connection.CreateCommand();
        await using var _ = command;
        command.CommandType = CommandType.Text;
        command.CommandText = sql;
        foreach (var param in sqlParams)
        {
            command.Parameters.Add(param);
        }

        if (transaction != null)
        {
            command.Transaction = transaction;
        }

        return await command.ExecuteNonQueryAsync().AnyContext();
    }

    public static async Task<T> ExecuteReaderAsync<T>(
        this DbConnection connection,
        string sql,
        Func<DbDataReader, Task<T>>? readerFunc,
        DbTransaction? transaction = null,
        params object[] sqlParams
    )
    {
        if (connection.State == ConnectionState.Closed)
        {
            await connection.OpenAsync().AnyContext();
        }

        var command = connection.CreateCommand();
        await using var _ = command;
        command.CommandType = CommandType.Text;
        command.CommandText = sql;
        foreach (var param in sqlParams)
        {
            command.Parameters.Add(param);
        }

        if (transaction != null)
        {
            command.Transaction = transaction;
        }

        await using var reader = await command.ExecuteReaderAsync().AnyContext();

        T result = default!;
        if (readerFunc != null)
        {
            result = await readerFunc(reader).AnyContext();
        }

        return result;
    }

    public static async Task<T> ExecuteScalarAsync<T>(
        this DbConnection connection,
        string sql,
        params object[] sqlParams
    )
    {
        if (connection.State == ConnectionState.Closed)
        {
            await connection.OpenAsync().AnyContext();
        }

        var command = connection.CreateCommand();
        await using var _ = command;
        command.CommandType = CommandType.Text;
        command.CommandText = sql;
        foreach (var param in sqlParams)
        {
            command.Parameters.Add(param);
        }

        var objValue = await command.ExecuteScalarAsync().AnyContext();

        T result = default!;
        if (objValue != null)
        {
            var returnType = typeof(T);
            var converter = TypeDescriptor.GetConverter(returnType);
            if (converter.CanConvertFrom(objValue.GetType()))
            {
                result = (T)converter.ConvertFrom(objValue)!;
            }
            else
            {
                result = (T)Convert.ChangeType(objValue, returnType);
            }
        }

        return result;
    }
}
