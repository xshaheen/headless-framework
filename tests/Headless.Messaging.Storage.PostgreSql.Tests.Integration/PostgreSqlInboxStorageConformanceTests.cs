// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;

namespace Tests;

[Collection<PostgreSqlTestFixture>]
public sealed class PostgreSqlInboxStorageConformanceTests(PostgreSqlTestFixture fixture) : InboxStorageConformanceTests
{
    protected override void ConfigureStorage(MessagingSetupBuilder setup)
    {
        setup.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.DurableDedupeOnly;
        setup.ConfigureStorage(storage => storage.Schema = $"inbox_conformance_{Guid.NewGuid():N}");
        setup.UsePostgreSql(fixture.ConnectionString);
    }
}
