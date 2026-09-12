// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;

namespace Tests;

[Collection<SqlServerTestFixture>]
public sealed class SqlServerInboxOperationPolicyTests(SqlServerTestFixture fixture)
    : InboxOperationPolicyConformanceTests
{
    protected override void ConfigureStorage(MessagingSetupBuilder setup)
    {
        setup.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.DurableDedupeOnly;
        setup.UseSqlServer(options =>
        {
            options.ConnectionString = fixture.ConnectionString;
            options.Schema = $"inbox_policy_{Guid.NewGuid():N}";
        });
    }
}
