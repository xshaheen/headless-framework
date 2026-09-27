// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Dapper;
using Headless.Messaging.Configuration;
using Headless.Messaging.Persistence;
using Headless.Messaging.Storage.SqlServer;
using Headless.Testing.Tests;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

[Collection<SqlServerTestFixture>]
public sealed class SqlServerStorageInitializerTests(SqlServerTestFixture fixture) : TestBase
{
    [Fact]
    public async Task should_create_schema_if_not_exists()
    {
        // given
        const string customSchema = "custom_test_schema";
        var initializer = _CreateInitializer(customSchema, useStorageLock: false);

        // when
        await initializer.InitializeAsync(AbortToken);

        // then
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);

        var schemaExists = await connection.QueryFirstOrDefaultAsync<int>(
            new CommandDefinition(
                "SELECT 1 FROM sys.schemas WHERE name = @Schema",
                new { Schema = customSchema },
                cancellationToken: AbortToken
            )
        );

        schemaExists.Should().Be(1);

        // cleanup
        await connection.ExecuteAsync(
            new CommandDefinition(
                $"DROP TABLE IF EXISTS [{customSchema}].MessagingInboxAudit; DROP TABLE IF EXISTS [{customSchema}].MessagingInboxOperationReceipts; DROP TABLE IF EXISTS [{customSchema}].MessagingSchemaState; DROP TABLE IF EXISTS [{customSchema}].MessagingPublished; DROP TABLE IF EXISTS [{customSchema}].MessagingReceived; DROP TYPE IF EXISTS [{customSchema}].[HeadlessMessagingIdList]; DROP TYPE IF EXISTS [{customSchema}].[HeadlessMessagingOwnerList]; DROP TYPE IF EXISTS [{customSchema}].[HeadlessMessagingPoisonMessageList]; DROP SCHEMA IF EXISTS [{customSchema}]",
                cancellationToken: AbortToken
            )
        );
    }

    [Fact]
    public async Task should_create_published_table_with_correct_structure()
    {
        // given
        const string schema = "structure_test";
        var initializer = _CreateInitializer(schema, useStorageLock: false);

        // when
        await initializer.InitializeAsync(AbortToken);

        // then
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);

        var columns = await connection.QueryAsync<string>(
            """
            SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = @Schema AND TABLE_NAME = 'MessagingPublished'
            """,
            new { Schema = schema }
        );

        columns
            .Should()
            .Contain(["Id", "Version", "Name", "Content", "Retries", "Added", "ExpiresAt", "StatusName", "MessageId"]);

        // cleanup
        await connection.ExecuteAsync(
            new CommandDefinition(
                $"DROP TABLE IF EXISTS [{schema}].MessagingInboxAudit; DROP TABLE IF EXISTS [{schema}].MessagingInboxOperationReceipts; DROP TABLE IF EXISTS [{schema}].MessagingSchemaState; DROP TABLE IF EXISTS [{schema}].MessagingPublished; DROP TABLE IF EXISTS [{schema}].MessagingReceived; DROP TYPE IF EXISTS [{schema}].[HeadlessMessagingIdList]; DROP TYPE IF EXISTS [{schema}].[HeadlessMessagingOwnerList]; DROP TYPE IF EXISTS [{schema}].[HeadlessMessagingPoisonMessageList]; DROP SCHEMA IF EXISTS [{schema}]",
                cancellationToken: AbortToken
            )
        );
    }

    [Fact]
    public async Task should_create_received_table_with_correct_structure()
    {
        // given
        const string schema = "received_test";
        var initializer = _CreateInitializer(schema, useStorageLock: false);

        // when
        await initializer.InitializeAsync(AbortToken);

        // then
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);

        var columns = await connection.QueryAsync<string>(
            """
            SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = @Schema AND TABLE_NAME = 'MessagingReceived'
            """,
            new { Schema = schema }
        );

        columns
            .Should()
            .Contain([
                "Id",
                "Version",
                "Name",
                "Group",
                "Content",
                "Retries",
                "Added",
                "ExpiresAt",
                "StatusName",
                "MessageId",
            ]);

        // cleanup
        await connection.ExecuteAsync(
            new CommandDefinition(
                $"DROP TABLE IF EXISTS [{schema}].MessagingInboxAudit; DROP TABLE IF EXISTS [{schema}].MessagingInboxOperationReceipts; DROP TABLE IF EXISTS [{schema}].MessagingSchemaState; DROP TABLE IF EXISTS [{schema}].MessagingPublished; DROP TABLE IF EXISTS [{schema}].MessagingReceived; DROP TYPE IF EXISTS [{schema}].[HeadlessMessagingIdList]; DROP TYPE IF EXISTS [{schema}].[HeadlessMessagingOwnerList]; DROP TYPE IF EXISTS [{schema}].[HeadlessMessagingPoisonMessageList]; DROP SCHEMA IF EXISTS [{schema}]",
                cancellationToken: AbortToken
            )
        );
    }

    [Fact]
    public async Task should_create_indexes_on_published_table()
    {
        // given
        const string schema = "index_test";
        var initializer = _CreateInitializer(schema, useStorageLock: false);

        // when
        await initializer.InitializeAsync(AbortToken);

        // then
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);

        var indexes = await connection.QueryAsync<string>(
            new CommandDefinition(
                """
                SELECT i.name
                FROM sys.indexes i
                INNER JOIN sys.tables t ON i.object_id = t.object_id
                INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                WHERE s.name = @Schema AND t.name = 'MessagingPublished' AND i.name IS NOT NULL
                """,
                new { Schema = schema },
                cancellationToken: AbortToken
            )
        );

        indexes.Should().HaveCountGreaterThanOrEqualTo(2); // At least PK + some indexes

        // cleanup
        await connection.ExecuteAsync(
            new CommandDefinition(
                $"DROP TABLE IF EXISTS [{schema}].MessagingInboxAudit; DROP TABLE IF EXISTS [{schema}].MessagingInboxOperationReceipts; DROP TABLE IF EXISTS [{schema}].MessagingSchemaState; DROP TABLE IF EXISTS [{schema}].MessagingPublished; DROP TABLE IF EXISTS [{schema}].MessagingReceived; DROP TYPE IF EXISTS [{schema}].[HeadlessMessagingIdList]; DROP TYPE IF EXISTS [{schema}].[HeadlessMessagingOwnerList]; DROP TYPE IF EXISTS [{schema}].[HeadlessMessagingPoisonMessageList]; DROP SCHEMA IF EXISTS [{schema}]",
                cancellationToken: AbortToken
            )
        );
    }

    [Theory]
    [InlineData("MessagingPublished", "IX_MessagingPublished_Version_NextRetryAt")]
    [InlineData("MessagingReceived", "IX_MessagingReceived_Version_NextRetryAt")]
    public async Task should_key_retry_pickup_index_on_version_lane_then_next_retry_at(
        string table,
        string indexNamePattern
    )
    {
        // Pin every equality predicate before the NextRetryAt range so each lane gets an isolated seek.
        var schema = $"index_shape_test_{Guid.NewGuid():N}";
        var initializer = _CreateInitializer(schema, useStorageLock: false);

        await initializer.InitializeAsync(AbortToken);

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);

        var indexName = indexNamePattern.Replace("{schema}", schema, StringComparison.Ordinal);

        await _AssertRetryIndexShapeAsync(connection, schema, table, indexName);

        // cleanup
        await connection.ExecuteAsync(
            new CommandDefinition(
                $"DROP TABLE IF EXISTS [{schema}].MessagingInboxAudit; DROP TABLE IF EXISTS [{schema}].MessagingInboxOperationReceipts; DROP TABLE IF EXISTS [{schema}].MessagingSchemaState; DROP TABLE IF EXISTS [{schema}].MessagingPublished; DROP TABLE IF EXISTS [{schema}].MessagingReceived; DROP TYPE IF EXISTS [{schema}].[HeadlessMessagingIdList]; DROP TYPE IF EXISTS [{schema}].[HeadlessMessagingOwnerList]; DROP TYPE IF EXISTS [{schema}].[HeadlessMessagingPoisonMessageList]; DROP SCHEMA IF EXISTS [{schema}]",
                cancellationToken: AbortToken
            )
        );
    }

    [Fact]
    public async Task should_serialize_concurrent_fresh_initialization()
    {
        var schema = $"concurrent_init_test_{Guid.NewGuid():N}";

        var initializationTasks = Enumerable
            .Range(0, 8)
            .Select(_ => _CreateInitializer(schema, useStorageLock: false).InitializeAsync(AbortToken))
            .ToArray();

        await Task.WhenAll(initializationTasks);

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);

        await _AssertRetryIndexShapeAsync(
            connection,
            schema,
            "MessagingPublished",
            $"IX_MessagingPublished_Version_NextRetryAt"
        );
        await _AssertRetryIndexShapeAsync(
            connection,
            schema,
            "MessagingReceived",
            $"IX_MessagingReceived_Version_NextRetryAt"
        );

        // cleanup
        await connection.ExecuteAsync(
            new CommandDefinition(
                $"DROP TABLE IF EXISTS [{schema}].MessagingInboxAudit; DROP TABLE IF EXISTS [{schema}].MessagingInboxOperationReceipts; DROP TABLE IF EXISTS [{schema}].MessagingSchemaState; DROP TABLE IF EXISTS [{schema}].MessagingPublished; DROP TABLE IF EXISTS [{schema}].MessagingReceived; DROP TYPE IF EXISTS [{schema}].[HeadlessMessagingIdList]; DROP TYPE IF EXISTS [{schema}].[HeadlessMessagingOwnerList]; DROP TYPE IF EXISTS [{schema}].[HeadlessMessagingPoisonMessageList]; DROP SCHEMA IF EXISTS [{schema}]",
                cancellationToken: AbortToken
            )
        );
    }

    private static async Task _AssertRetryIndexShapeAsync(
        SqlConnection connection,
        string schema,
        string table,
        string indexName
    )
    {
        var keyColumns = (
            await connection.QueryAsync<string>(
                new CommandDefinition(
                    """
                    SELECT c.name
                    FROM sys.indexes i
                    INNER JOIN sys.tables t ON i.object_id = t.object_id
                    INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                    INNER JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                    INNER JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                    WHERE s.name = @Schema
                      AND t.name = @Table
                      AND i.name = @IndexName
                      AND ic.is_included_column = 0
                    ORDER BY ic.key_ordinal
                    """,
                    new
                    {
                        Schema = schema,
                        Table = table,
                        IndexName = indexName,
                    },
                    cancellationToken: AbortToken
                )
            )
        ).ToList();

        keyColumns.Should().BeEquivalentTo(["Version", "IntentType", "NextRetryAt"], opts => opts.WithStrictOrdering());

        var filter = await connection.QueryFirstOrDefaultAsync<string>(
            new CommandDefinition(
                """
                SELECT i.filter_definition
                FROM sys.indexes i
                INNER JOIN sys.tables t ON i.object_id = t.object_id
                INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                WHERE s.name = @Schema AND t.name = @Table AND i.name = @IndexName
                """,
                new
                {
                    Schema = schema,
                    Table = table,
                    IndexName = indexName,
                },
                cancellationToken: AbortToken
            )
        );

        filter.Should().Be("([NextRetryAt] IS NOT NULL)");
    }

    [Theory]
    [InlineData("MessagingPublished")]
    [InlineData("MessagingReceived")]
    public async Task should_create_status_added_composite_index(string table)
    {
        // #508 — the initializer creates the final ([StatusName],[Added]) dashboard index directly.
        const string schema = "status_added_index_test";
        var initializer = _CreateInitializer(schema, useStorageLock: false);

        await initializer.InitializeAsync(AbortToken);

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);

        var compositeKeyColumns = (
            await connection.QueryAsync<string>(
                new CommandDefinition(
                    """
                    SELECT c.name
                    FROM sys.indexes i
                    INNER JOIN sys.tables t ON i.object_id = t.object_id
                    INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                    INNER JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                    INNER JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                    WHERE s.name = @Schema AND t.name = @Table AND i.name = @IndexName AND ic.is_included_column = 0
                    ORDER BY ic.key_ordinal
                    """,
                    new
                    {
                        Schema = schema,
                        Table = table,
                        IndexName = $"IX_{table}_StatusName_Added",
                    },
                    cancellationToken: AbortToken
                )
            )
        ).ToList();

        compositeKeyColumns.Should().BeEquivalentTo(["StatusName", "Added"], opts => opts.WithStrictOrdering());

        // cleanup
        await connection.ExecuteAsync(
            new CommandDefinition(
                $"DROP TABLE IF EXISTS [{schema}].MessagingInboxAudit; DROP TABLE IF EXISTS [{schema}].MessagingInboxOperationReceipts; DROP TABLE IF EXISTS [{schema}].MessagingSchemaState; DROP TABLE IF EXISTS [{schema}].MessagingPublished; DROP TABLE IF EXISTS [{schema}].MessagingReceived; DROP TYPE IF EXISTS [{schema}].[HeadlessMessagingIdList]; DROP TYPE IF EXISTS [{schema}].[HeadlessMessagingOwnerList]; DROP TYPE IF EXISTS [{schema}].[HeadlessMessagingPoisonMessageList]; DROP SCHEMA IF EXISTS [{schema}]",
                cancellationToken: AbortToken
            )
        );
    }

    [Fact]
    public async Task should_be_idempotent()
    {
        // given
        const string schema = "idempotent_test";
        var initializer = _CreateInitializer(schema, useStorageLock: false);

        // when - run twice
        await initializer.InitializeAsync(AbortToken);
        await initializer.InitializeAsync(AbortToken); // Should not throw

        // then
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);

        var tableCount = await connection.QueryFirstOrDefaultAsync<int>(
            """
            SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_SCHEMA = @Schema AND TABLE_NAME IN ('MessagingPublished', 'MessagingReceived')
            """,
            new { Schema = schema }
        );

        tableCount.Should().Be(2);

        // cleanup
        await connection.ExecuteAsync(
            new CommandDefinition(
                $"DROP TABLE IF EXISTS [{schema}].MessagingInboxAudit; DROP TABLE IF EXISTS [{schema}].MessagingInboxOperationReceipts; DROP TABLE IF EXISTS [{schema}].MessagingSchemaState; DROP TABLE IF EXISTS [{schema}].MessagingPublished; DROP TABLE IF EXISTS [{schema}].MessagingReceived; DROP TYPE IF EXISTS [{schema}].[HeadlessMessagingIdList]; DROP TYPE IF EXISTS [{schema}].[HeadlessMessagingOwnerList]; DROP TYPE IF EXISTS [{schema}].[HeadlessMessagingPoisonMessageList]; DROP SCHEMA IF EXISTS [{schema}]",
                cancellationToken: AbortToken
            )
        );
    }

    [Fact]
    public async Task should_recreate_missing_indexes_when_tables_already_exist()
    {
        // given
        const string schema = "index_repair_test";
        var initializer = _CreateInitializer(schema, useStorageLock: false);

        await initializer.InitializeAsync(AbortToken);

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);

        await connection.ExecuteAsync(
            new CommandDefinition(
                $"""
                DROP INDEX IF EXISTS [UX_MessagingReceived_InboxRootKey] ON [{schema}].[MessagingReceived];
                DROP INDEX IF EXISTS [UX_MessagingReceived_InboxLifecycleGeneration] ON [{schema}].[MessagingReceived];
                DROP INDEX IF EXISTS [IX_MessagingReceived_Version_ExpiresAt_StatusName] ON [{schema}].[MessagingReceived];
                DROP INDEX IF EXISTS [IX_MessagingReceived_ExpiresAt_StatusName] ON [{schema}].[MessagingReceived];
                DROP INDEX IF EXISTS [IX_MessagingReceived_Version_NextRetryAt] ON [{schema}].[MessagingReceived];
                DROP INDEX IF EXISTS [IX_MessagingPublished_Version_ExpiresAt_StatusName] ON [{schema}].[MessagingPublished];
                DROP INDEX IF EXISTS [IX_MessagingPublished_ExpiresAt_StatusName] ON [{schema}].[MessagingPublished];
                DROP INDEX IF EXISTS [IX_MessagingPublished_Version_NextRetryAt] ON [{schema}].[MessagingPublished];
                """,
                cancellationToken: AbortToken
            )
        );

        // when
        await initializer.InitializeAsync(AbortToken);

        // then
        var indexCount = await connection.QuerySingleAsync<int>(
            new CommandDefinition(
                """
                SELECT COUNT(*)
                FROM sys.indexes i
                INNER JOIN sys.tables t ON i.object_id = t.object_id
                INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                WHERE s.name = @Schema
                  AND i.name IN (
                    @ReceivedUnique,
                    @ReceivedLifecycle,
                    @ReceivedVersionExpires,
                    @ReceivedExpires,
                    @ReceivedRetry,
                    @PublishedVersionExpires,
                    @PublishedExpires,
                    @PublishedRetry
                  )
                """,
                new
                {
                    Schema = schema,
                    ReceivedUnique = $"UX_MessagingReceived_InboxRootKey",
                    ReceivedLifecycle = $"UX_MessagingReceived_InboxLifecycleGeneration",
                    ReceivedVersionExpires = $"IX_MessagingReceived_Version_ExpiresAt_StatusName",
                    ReceivedExpires = $"IX_MessagingReceived_ExpiresAt_StatusName",
                    ReceivedRetry = $"IX_MessagingReceived_Version_NextRetryAt",
                    PublishedVersionExpires = $"IX_MessagingPublished_Version_ExpiresAt_StatusName",
                    PublishedExpires = $"IX_MessagingPublished_ExpiresAt_StatusName",
                    PublishedRetry = $"IX_MessagingPublished_Version_NextRetryAt",
                },
                cancellationToken: AbortToken
            )
        );

        indexCount.Should().Be(8);

        // cleanup
        await connection.ExecuteAsync(
            new CommandDefinition(
                $"DROP TABLE IF EXISTS [{schema}].MessagingInboxAudit; DROP TABLE IF EXISTS [{schema}].MessagingInboxOperationReceipts; DROP TABLE IF EXISTS [{schema}].MessagingSchemaState; DROP TABLE IF EXISTS [{schema}].MessagingPublished; DROP TABLE IF EXISTS [{schema}].MessagingReceived; DROP TYPE IF EXISTS [{schema}].[HeadlessMessagingIdList]; DROP TYPE IF EXISTS [{schema}].[HeadlessMessagingOwnerList]; DROP TYPE IF EXISTS [{schema}].[HeadlessMessagingPoisonMessageList]; DROP SCHEMA IF EXISTS [{schema}]",
                cancellationToken: AbortToken
            )
        );
    }

    [Fact]
    public async Task should_name_constraints_after_the_table_in_every_schema()
    {
        // given - the shared default schema plus a second schema in the same database, so the
        // schema-scoped constraint names must not collide across them
        const string otherSchema = "constraint_names";
        await _CreateInitializer("headless", useStorageLock: false).InitializeAsync(AbortToken);
        await _CreateInitializer(otherSchema, useStorageLock: false).InitializeAsync(AbortToken);

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);

        try
        {
            foreach (var schema in new[] { "headless", otherSchema })
            {
                // when
                var names = (
                    await connection.QueryAsync<string>(
                        new CommandDefinition(
                            """
                            SELECT o.name FROM sys.objects o JOIN sys.tables t ON t.object_id = o.parent_object_id
                            JOIN sys.schemas s ON s.schema_id = t.schema_id
                            WHERE s.name = @Schema AND o.type IN ('PK','UQ','F','C','D')
                            UNION ALL
                            SELECT i.name FROM sys.indexes i JOIN sys.tables t ON t.object_id = i.object_id
                            JOIN sys.schemas s ON s.schema_id = t.schema_id
                            WHERE s.name = @Schema AND i.name IS NOT NULL
                            """,
                            new { Schema = schema },
                            cancellationToken: AbortToken
                        )
                    )
                ).ToList();

                // then
                names
                    .Should()
                    .Contain([
                        "PK_MessagingReceived",
                        "PK_MessagingPublished",
                        "CK_MessagingReceived_InboxIdentity",
                        "UX_MessagingReceived_InboxRootKey",
                        "FK_MessagingInboxAudit_Operation",
                        "IX_MessagingInboxOperationReceipts_Type_CreatedAt",
                    ]);
                names.Should().OnlyContain(name => name.Contains("_Messaging", StringComparison.Ordinal));
                names.Should().NotContain(name => name.Contains($"_{schema}_", StringComparison.Ordinal));
            }
        }
        finally
        {
            await connection.ExecuteAsync(
                new CommandDefinition(
                    $"DROP TABLE IF EXISTS [{otherSchema}].MessagingInboxAudit; DROP TABLE IF EXISTS [{otherSchema}].MessagingInboxOperationReceipts; DROP TABLE IF EXISTS [{otherSchema}].MessagingSchemaState; DROP TABLE IF EXISTS [{otherSchema}].MessagingPublished; DROP TABLE IF EXISTS [{otherSchema}].MessagingReceived; DROP TYPE IF EXISTS [{otherSchema}].[HeadlessMessagingIdList]; DROP TYPE IF EXISTS [{otherSchema}].[HeadlessMessagingOwnerList]; DROP TYPE IF EXISTS [{otherSchema}].[HeadlessMessagingPoisonMessageList]; DROP SCHEMA IF EXISTS [{otherSchema}]",
                    cancellationToken: AbortToken
                )
            );
        }
    }

    [Fact]
    public void should_return_correct_table_names()
    {
        // given
        const string schema = "table_names";
        var initializer = _CreateInitializer(schema, useStorageLock: true);

        // when & then
        initializer.GetPublishedTableName().Should().Be($"{schema}.MessagingPublished");
        initializer.GetReceivedTableName().Should().Be($"{schema}.MessagingReceived");
    }

    [Fact]
    public async Task should_handle_cancellation()
    {
        // given
        const string schema = "cancel_test";
        var initializer = _CreateInitializer(schema, useStorageLock: false);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // when
        await initializer.InitializeAsync(cts.Token);

        // then - should return early without error
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);

        var schemaExists = await connection.QueryFirstOrDefaultAsync<int?>(
            new CommandDefinition(
                "SELECT 1 FROM sys.schemas WHERE name = @Schema",
                new { Schema = schema },
                cancellationToken: AbortToken
            )
        );

        schemaExists.Should().BeNull();
    }

    private IStorageInitializer _CreateInitializer(string schema, bool useStorageLock)
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddLogging();
        services.Configure<SqlServerOptions>(x => x.ConnectionString = fixture.ConnectionString);
        services.Configure<MessagingStorageOptions>(x => x.Schema = schema);
        services.Configure<MessagingOptions>(x =>
        {
            x.Version = "v1";
            x.UseStorageLock = useStorageLock;
        });
        services.AddSingleton<IStorageInitializer, SqlServerStorageInitializer>();

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IStorageInitializer>();
    }
}
