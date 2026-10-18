// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;

namespace Tests;

[Collection<SqlServerTestFixture>]
public sealed class SqlServerDeliverWithinRelayTests(SqlServerTestFixture fixture) : DeliverWithinRelayConformanceTests
{
    protected override void UseStorage(MessagingSetupBuilder setup) => setup.UseSqlServer(fixture.ConnectionString);
}
