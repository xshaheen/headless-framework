// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Storage.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

[Collection<SqlServerTestFixture>]
public sealed class SqlServerScheduledDeliveryOperationTests(SqlServerTestFixture fixture)
    : ScheduledDeliveryOperationConformanceTests
{
    protected override async Task AgeHistoryAsync(ServiceProvider provider, TimeSpan age)
    {
        var schema = provider.GetRequiredService<IOptions<MessagingStorageOptions>>().Value.Schema;
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new SqlCommand(
            $"""
            UPDATE [{schema}].[InboxOperationReceipts] SET [CreatedAt]=DATEADD(second,-@Age,[CreatedAt]);
            UPDATE [{schema}].[InboxAudit] SET [CreatedAt]=DATEADD(second,-@Age,[CreatedAt]);
            """,
            connection
        );
        command.Parameters.Add(new SqlParameter("@Age", (int)age.TotalSeconds));
        await command.ExecuteNonQueryAsync(AbortToken);
    }

    protected override void ConfigureStorage(MessagingSetupBuilder setup)
    {
        setup.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.DurableDedupeOnly;
        setup.ConfigureStorage(storage => storage.Schema = $"scheduled_policy_{Guid.NewGuid():N}");
        setup.UseSqlServer(fixture.ConnectionString);
    }
}
