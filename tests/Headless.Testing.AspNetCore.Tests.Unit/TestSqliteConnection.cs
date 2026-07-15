// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Data.Sqlite;

namespace Tests;

internal static class TestSqliteConnection
{
    public static async Task<SqliteConnection> CreateAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE Dummy (Id INT PRIMARY KEY);";
        await command.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }
}
