// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Testing.Testcontainers;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Tests;

/// <summary>
/// PostgreSQL container shared by the outbox-bridge integration tests. EF business tables and the messaging
/// outbox tables live in the same database so an integration-event write enlists in the EF save transaction.
/// </summary>
/// <remarks>
/// The container is reused across runs of this checkout, so it starts by dropping the default storage schema, history
/// included. The tests apply the schema without dropping it, and the runner trusts its history: a step changed since
/// an older run would otherwise fail its checksum.
/// </remarks>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class OutboxBridgeTestFixture
    : HeadlessPostgreSqlFixture,
        ICollectionFixture<OutboxBridgeTestFixture>,
        IAsyncLifetime
{
    public string ConnectionString => Container.GetConnectionString();

    protected override PostgreSqlBuilder Configure()
    {
        return base.Configure().WithDatabase("outbox_bridge_test").WithUsername("postgres").WithPassword("postgres");
    }

    async ValueTask IAsyncLifetime.InitializeAsync()
    {
        await InitializeAsync();
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"""DROP SCHEMA IF EXISTS "{HeadlessStorageDefaults.Schema}" CASCADE;""",
            connection
        );
        await command.ExecuteNonQueryAsync();
    }
}
