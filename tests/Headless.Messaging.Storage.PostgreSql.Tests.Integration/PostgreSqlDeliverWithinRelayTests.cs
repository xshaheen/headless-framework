// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;

namespace Tests;

[Collection<PostgreSqlTestFixture>]
public sealed class PostgreSqlDeliverWithinRelayTests(PostgreSqlTestFixture fixture)
    : DeliverWithinRelayConformanceTests
{
    protected override void UseStorage(MessagingSetupBuilder setup) => setup.UsePostgreSql(fixture.ConnectionString);
}
