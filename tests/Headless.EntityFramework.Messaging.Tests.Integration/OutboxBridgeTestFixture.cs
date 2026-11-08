// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;

namespace Tests;

/// <summary>
/// PostgreSQL database shared by the outbox-bridge integration tests. EF business tables and the messaging
/// outbox tables live in the same database so an integration-event write enlists in the EF save transaction.
/// </summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class OutboxBridgeTestFixture : HeadlessPostgreSqlFixture, ICollectionFixture<OutboxBridgeTestFixture>;
