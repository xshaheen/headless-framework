// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;
using Npgsql;

namespace Tests.Fixtures;

/// <summary>
/// The PostgreSQL store of a messaging stack suite: a database of its own that empties the outbox and inbox tables
/// between tests.
/// </summary>
public abstract class PostgreSqlStackComponent : HeadlessPostgreSqlFixture
{
    public async Task ResetAsync()
    {
        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                TRUNCATE TABLE headless.messaging_published;
                TRUNCATE TABLE headless.messaging_received;
                """;
            await command.ExecuteNonQueryAsync();
        }
        catch (PostgresException)
        {
            // Schema may not exist before the first storage initializer run.
        }
    }
}
