// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Dapper;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Proves the feature-owned schema reaches every SQL Server storage path: the initializer creates the
/// objects there, and the storage reads and writes them there rather than falling back to the default.
/// </summary>
[Collection<SqlServerTestFixture>]
public sealed class SqlServerCustomSchemaTests(SqlServerTestFixture fixture) : TestBase
{
    private const string _Schema = "msg_custom";

    [Fact]
    public async Task should_create_and_use_the_tables_in_the_configured_schema()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
        {
            setup.Options.Version = "v1";
            setup.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.DurableDedupeOnly;
            setup.ConfigureStorage(storage => storage.Schema = _Schema);
            setup.UseSqlServer(fixture.ConnectionString);
        });

        await using var provider = services.BuildServiceProvider();

        try
        {
            // when
            await provider.GetRequiredService<IStorageInitializer>().InitializeAsync(AbortToken);

            var storage = provider.GetRequiredService<IDataStorage>();
            var messageId = Guid.NewGuid().ToString("D");
            var message = new Message(
                new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = messageId },
                new { Data = "custom-schema" }
            );
            var stored = await storage.StoreMessageAsync("test.topic", message, cancellationToken: AbortToken);

            // then
            await using var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync(AbortToken);

            var tables = await connection.QueryAsync<string>(
                "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA=@Schema ORDER BY TABLE_NAME;",
                new { Schema = _Schema }
            );
            tables.Should().Contain(["Published", "Received"]);

            var rows = await connection.QueryFirstAsync<int>(
                $"SELECT COUNT(*) FROM [{_Schema}].[Published] WHERE [Id]=@Id;",
                new { Id = stored.StorageId }
            );
            rows.Should().Be(1, "the write must land in the configured schema, not the default one");
        }
        finally
        {
            await using var cleanup = new SqlConnection(fixture.ConnectionString);
            await cleanup.OpenAsync(AbortToken);
            await cleanup.ExecuteAsync(
                $"""
                DROP TABLE IF EXISTS [{_Schema}].[InboxAudit];
                DROP TABLE IF EXISTS [{_Schema}].[InboxOperationReceipts];
                DROP TABLE IF EXISTS [{_Schema}].[SchemaState];
                DROP TABLE IF EXISTS [{_Schema}].[Published];
                DROP TABLE IF EXISTS [{_Schema}].[Received];
                DROP TYPE IF EXISTS [{_Schema}].[HeadlessMessagingIdList];
                DROP TYPE IF EXISTS [{_Schema}].[HeadlessMessagingOwnerList];
                DROP TYPE IF EXISTS [{_Schema}].[HeadlessMessagingPoisonMessageList];
                DROP SCHEMA IF EXISTS [{_Schema}];
                """
            );
        }
    }
}
