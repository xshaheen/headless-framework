// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Tests.Fixtures;

/// <summary>
/// The PostgreSQL store of a messaging stack suite: a fresh container that empties the outbox and inbox tables between
/// tests.
/// </summary>
public abstract class PostgreSqlStackComponent(string databaseName = "messages_test") : HeadlessPostgreSqlFixture
{
    public string ConnectionString => Container.GetConnectionString();

    protected override PostgreSqlBuilder Configure() =>
        base.Configure().WithDatabase(databaseName).WithUsername("postgres").WithPassword("postgres");

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
