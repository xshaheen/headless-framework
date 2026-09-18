// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Storage.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Tests;

[Collection<PostgreSqlTestFixture>]
public sealed class PostgreSqlScheduledDeliveryOperationTests(PostgreSqlTestFixture fixture)
    : ScheduledDeliveryOperationConformanceTests
{
    protected override async Task AgeHistoryAsync(ServiceProvider provider, TimeSpan age)
    {
        var schema = provider.GetRequiredService<IOptions<MessagingStorageOptions>>().Value.Schema;
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new NpgsqlCommand(
            $"""
            UPDATE "{schema}"."inbox_operation_receipts" SET "CreatedAt"="CreatedAt"-@Age;
            UPDATE "{schema}"."inbox_audit" SET "CreatedAt"="CreatedAt"-@Age;
            """,
            connection
        );
        command.Parameters.AddWithValue("@Age", age);
        await command.ExecuteNonQueryAsync(AbortToken);
    }

    protected override void ConfigureStorage(MessagingSetupBuilder setup)
    {
        setup.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.DurableDedupeOnly;
        setup.ConfigureStorage(storage => storage.Schema = $"scheduled_policy_{Guid.NewGuid():N}");
        setup.UsePostgreSql(fixture.ConnectionString);
    }
}
