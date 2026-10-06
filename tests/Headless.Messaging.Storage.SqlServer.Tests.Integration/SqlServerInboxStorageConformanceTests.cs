// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;

namespace Tests;

[Collection<SqlServerTestFixture>]
public sealed class SqlServerInboxStorageConformanceTests(SqlServerTestFixture fixture) : InboxStorageConformanceTests
{
    protected override void ConfigureStorage(MessagingSetupBuilder setup)
    {
        setup.Options.MinimumInboxGuarantee = InboxGuarantee.Durable;
        setup.ConfigureStorage(storage => storage.Schema = $"inbox_conformance_{Guid.NewGuid():N}");
        setup.UseSqlServer(fixture.ConnectionString);
    }
}
