// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Tests.Fixtures;

/// <summary>
/// The PostgreSQL store of a messaging stack suite: a reused container that starts each run from an empty messaging
/// schema and empties the outbox and inbox tables between tests.
/// </summary>
/// <remarks>
/// Each test project derives its own sealed subclass. The container's reuse label is the assembly of the runtime type,
/// so a subclass in the consuming project keeps that project on its own container; using this type from one shared
/// assembly would put every suite on one container, where one suite's schema drop breaks another running beside it.
/// </remarks>
public abstract class PostgreSqlStackComponent(string databaseName = "messages_test")
    : HeadlessPostgreSqlFixture,
        IAsyncLifetime
{
    public string ConnectionString => Container.GetConnectionString();

    protected override PostgreSqlBuilder Configure() =>
        base.Configure().WithDatabase(databaseName).WithUsername("postgres").WithPassword("postgres");

    async ValueTask IAsyncLifetime.InitializeAsync()
    {
        await InitializeAsync();

        // The container is reused across runs, and the schema runner trusts the history it finds: tables an older
        // build created would be kept in their older shape. Start every run from an empty messaging schema.
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """DROP SCHEMA IF EXISTS "headless" CASCADE;""";
        await command.ExecuteNonQueryAsync();
    }

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
