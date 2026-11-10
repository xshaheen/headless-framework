// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Npgsql;

namespace Tests;

/// <summary>Raw statements against a test database, for assertions that must not go through the code under test.</summary>
internal static class PostgreSqlTestDatabase
{
    public static async Task ExecuteAsync(string connectionString, string sql, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task<T> ScalarAsync<T>(string connectionString, string sql, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);

        return (T)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public static async Task<List<string>> ListAsync(
        string connectionString,
        string sql,
        CancellationToken cancellationToken
    )
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var values = new List<string>();

        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    /// <summary>Returns the names of the partitions of <paramref name="table"/>, in name order.</summary>
    public static Task<List<string>> PartitionsAsync(
        string connectionString,
        string schema,
        string table,
        CancellationToken cancellationToken
    )
    {
        return ListAsync(
            connectionString,
            $"""
            SELECT c.relname::text
            FROM pg_inherits AS i
            JOIN pg_class AS c ON c.oid = i.inhrelid
            WHERE i.inhparent = '"{schema}"."{table}"'::regclass
            ORDER BY c.relname;
            """,
            cancellationToken
        );
    }
}
