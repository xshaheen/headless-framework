// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;
using Testcontainers.PostgreSql;

namespace Tests;

/// <summary>
/// PostgreSQL container shared by the outbox-bridge integration tests. EF business tables and the messaging
/// outbox tables live in the same database so an integration-event write enlists in the EF save transaction.
/// </summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class OutboxBridgeTestFixture : HeadlessPostgreSqlFixture, ICollectionFixture<OutboxBridgeTestFixture>
{
    public string ConnectionString => Container.GetConnectionString();

    protected override PostgreSqlBuilder Configure()
    {
        return base.Configure().WithDatabase("outbox_bridge_test").WithUsername("postgres").WithPassword("postgres");
    }
}
