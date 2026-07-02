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
                $"DROP TABLE IF EXISTS [{customSchema}].Published; DROP TABLE IF EXISTS [{customSchema}].Received; DROP TYPE IF EXISTS [{customSchema}].[HeadlessMessagingIdList]; DROP TYPE IF EXISTS [{customSchema}].[HeadlessMessagingOwnerList]; DROP SCHEMA IF EXISTS [{customSchema}]",
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
            WHERE TABLE_SCHEMA = @Schema AND TABLE_NAME = 'Published'
            """,
            new { Schema = schema }
        );

        columns
            .Should()
            .Contain(["Id", "Version", "Name", "Content", "Retries", "Added", "ExpiresAt", "StatusName", "MessageId"]);

        // cleanup
        await connection.ExecuteAsync(
            new CommandDefinition(
                $"DROP TABLE IF EXISTS [{schema}].Published; DROP TABLE IF EXISTS [{schema}].Received; DROP TYPE IF EXISTS [{schema}].[HeadlessMessagingIdList]; DROP TYPE IF EXISTS [{schema}].[HeadlessMessagingOwnerList]; DROP SCHEMA IF EXISTS [{schema}]",
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
            WHERE TABLE_SCHEMA = @Schema AND TABLE_NAME = 'Received'
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
                $"DROP TABLE IF EXISTS [{schema}].Published; DROP TABLE IF EXISTS [{schema}].Received; DROP TYPE IF EXISTS [{schema}].[HeadlessMessagingIdList]; DROP TYPE IF EXISTS [{schema}].[HeadlessMessagingOwnerList]; DROP SCHEMA IF EXISTS [{schema}]",
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
                WHERE s.name = @Schema AND t.name = 'Published' AND i.name IS NOT NULL
                """,
                new { Schema = schema },
                cancellationToken: AbortToken
            )
        );

        indexes.Should().HaveCountGreaterThanOrEqualTo(2); // At least PK + some indexes

        // cleanup
        await connection.ExecuteAsync(
            new CommandDefinition(
                $"DROP TABLE IF EXISTS [{schema}].Published; DROP TABLE IF EXISTS [{schema}].Received; DROP TYPE IF EXISTS [{schema}].[HeadlessMessagingIdList]; DROP TYPE IF EXISTS [{schema}].[HeadlessMessagingOwnerList]; DROP SCHEMA IF EXISTS [{schema}]",
                cancellationToken: AbortToken
            )
        );
    }

    [Theory]
    [InlineData("Published", "IX_{schema}_Published_Version_NextRetryAt")]
    [InlineData("Received", "IX_{schema}_Received_Version_NextRetryAt")]
    public async Task should_key_retry_pickup_index_on_version_then_next_retry_at(string table, string indexNamePattern)
    {
        // Pin the retry-pickup index shape: Version must be the leading key column so it is a
        // seek predicate, not a residual filter. Regression here would silently fan the planner
        // out to both versions during a rolling upgrade and discard rows post-fetch.
        const string schema = "index_shape_test";
        var initializer = _CreateInitializer(schema, useStorageLock: false);

        await initializer.InitializeAsync(AbortToken);

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);

        var indexName = indexNamePattern.Replace("{schema}", schema, StringComparison.Ordinal);

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

        keyColumns.Should().BeEquivalentTo(["Version", "NextRetryAt"], opts => opts.WithStrictOrdering());

        // Filtered predicate must be `[NextRetryAt] IS NOT NULL` so terminal rows are physically
        // excluded — keeps the index small even under high failed-message volume.
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

        filter.Should().NotBeNull().And.Contain("NextRetryAt").And.Contain("IS NOT NULL");

        // cleanup
        await connection.ExecuteAsync(
            new CommandDefinition(
                $"DROP TABLE IF EXISTS [{schema}].Published; DROP TABLE IF EXISTS [{schema}].Received; DROP TYPE IF EXISTS [{schema}].[HeadlessMessagingIdList]; DROP TYPE IF EXISTS [{schema}].[HeadlessMessagingOwnerList]; DROP SCHEMA IF EXISTS [{schema}]",
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
            WHERE TABLE_SCHEMA = @Schema AND TABLE_NAME IN ('Published', 'Received')
            """,
            new { Schema = schema }
        );

        tableCount.Should().Be(2);

        // cleanup
        await connection.ExecuteAsync(
            new CommandDefinition(
                $"DROP TABLE IF EXISTS [{schema}].Published; DROP TABLE IF EXISTS [{schema}].Received; DROP TYPE IF EXISTS [{schema}].[HeadlessMessagingIdList]; DROP TYPE IF EXISTS [{schema}].[HeadlessMessagingOwnerList]; DROP SCHEMA IF EXISTS [{schema}]",
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
                DROP INDEX IF EXISTS [IX_{schema}_Received_Version_MessageId_GroupCoalesced_IntentType] ON [{schema}].[Received];
                DROP INDEX IF EXISTS [IX_{schema}_Received_Version_ExpiresAt_StatusName] ON [{schema}].[Received];
                DROP INDEX IF EXISTS [IX_{schema}_Received_ExpiresAt_StatusName] ON [{schema}].[Received];
                DROP INDEX IF EXISTS [IX_{schema}_Received_Version_NextRetryAt] ON [{schema}].[Received];
                DROP INDEX IF EXISTS [IX_{schema}_Published_Version_ExpiresAt_StatusName] ON [{schema}].[Published];
                DROP INDEX IF EXISTS [IX_{schema}_Published_ExpiresAt_StatusName] ON [{schema}].[Published];
                DROP INDEX IF EXISTS [IX_{schema}_Published_Version_NextRetryAt] ON [{schema}].[Published];
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
                    ReceivedUnique = $"IX_{schema}_Received_Version_MessageId_GroupCoalesced_IntentType",
                    ReceivedVersionExpires = $"IX_{schema}_Received_Version_ExpiresAt_StatusName",
                    ReceivedExpires = $"IX_{schema}_Received_ExpiresAt_StatusName",
                    ReceivedRetry = $"IX_{schema}_Received_Version_NextRetryAt",
                    PublishedVersionExpires = $"IX_{schema}_Published_Version_ExpiresAt_StatusName",
                    PublishedExpires = $"IX_{schema}_Published_ExpiresAt_StatusName",
                    PublishedRetry = $"IX_{schema}_Published_Version_NextRetryAt",
                },
                cancellationToken: AbortToken
            )
        );

        indexCount.Should().Be(7);

        // cleanup
        await connection.ExecuteAsync(
            new CommandDefinition(
                $"DROP TABLE IF EXISTS [{schema}].Published; DROP TABLE IF EXISTS [{schema}].Received; DROP TYPE IF EXISTS [{schema}].[HeadlessMessagingIdList]; DROP TYPE IF EXISTS [{schema}].[HeadlessMessagingOwnerList]; DROP SCHEMA IF EXISTS [{schema}]",
                cancellationToken: AbortToken
            )
        );
    }

    [Fact]
    public void should_return_correct_table_names()
    {
        // given
        const string schema = "table_names";
        var initializer = _CreateInitializer(schema, useStorageLock: true);

        // when & then
        initializer.GetPublishedTableName().Should().Be($"{schema}.Published");
        initializer.GetReceivedTableName().Should().Be($"{schema}.Received");
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
        services.Configure<SqlServerOptions>(x =>
        {
            x.ConnectionString = fixture.ConnectionString;
            x.Schema = schema;
        });
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
