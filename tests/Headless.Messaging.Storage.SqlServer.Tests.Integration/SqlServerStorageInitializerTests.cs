// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Dapper;
using Headless.Hosting.Initialization.Schema;
using Headless.Testing.Tests;
using Microsoft.Data.SqlClient;

namespace Tests;

[Collection<SqlServerTestFixture>]
public sealed class SqlServerStorageInitializerTests(SqlServerTestFixture fixture) : TestBase
{
    [Fact]
    public async Task should_create_schema_if_not_exists()
    {
        // given
        const string customSchema = "custom_test_schema";
        // when
        await TestMessagingSchema.ApplyAsync(fixture.ConnectionString, customSchema, AbortToken);

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
            new CommandDefinition(TestMessagingSchema.DropSql(customSchema), cancellationToken: AbortToken)
        );
    }

    [Fact]
    public async Task should_create_published_table_with_correct_structure()
    {
        // given
        const string schema = "structure_test";
        // when
        await TestMessagingSchema.ApplyAsync(fixture.ConnectionString, schema, AbortToken);

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
            new CommandDefinition(TestMessagingSchema.DropSql(schema), cancellationToken: AbortToken)
        );
    }

    [Fact]
    public async Task should_create_received_table_with_correct_structure()
    {
        // given
        const string schema = "received_test";
        // when
        await TestMessagingSchema.ApplyAsync(fixture.ConnectionString, schema, AbortToken);

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
            new CommandDefinition(TestMessagingSchema.DropSql(schema), cancellationToken: AbortToken)
        );
    }

    [Fact]
    public async Task should_create_indexes_on_published_table()
    {
        // given
        const string schema = "index_test";
        // when
        await TestMessagingSchema.ApplyAsync(fixture.ConnectionString, schema, AbortToken);

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
            new CommandDefinition(TestMessagingSchema.DropSql(schema), cancellationToken: AbortToken)
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
        await TestMessagingSchema.ApplyAsync(fixture.ConnectionString, schema, AbortToken);

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);

        var indexName = indexNamePattern.Replace("{schema}", schema, StringComparison.Ordinal);

        await _AssertRetryIndexShapeAsync(connection, schema, table, indexName);

        // cleanup
        await connection.ExecuteAsync(
            new CommandDefinition(TestMessagingSchema.DropSql(schema), cancellationToken: AbortToken)
        );
    }

    [Fact]
    public async Task should_serialize_concurrent_fresh_initialization()
    {
        var schema = $"concurrent_init_test_{Guid.NewGuid():N}";

        var initializationTasks = Enumerable
            .Range(0, 8)
            .Select(_ => TestMessagingSchema.ApplyAsync(fixture.ConnectionString, schema, AbortToken))
            .ToArray();

        await Task.WhenAll(initializationTasks);

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);

        await _AssertRetryIndexShapeAsync(
            connection,
            schema,
            "MessagingPublished",
            "IX_MessagingPublished_Version_NextRetryAt"
        );
        await _AssertRetryIndexShapeAsync(
            connection,
            schema,
            "MessagingReceived",
            "IX_MessagingReceived_Version_NextRetryAt"
        );

        // cleanup
        await connection.ExecuteAsync(
            new CommandDefinition(TestMessagingSchema.DropSql(schema), cancellationToken: AbortToken)
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
        // #508 — the tables step creates the final ([StatusName],[Added]) dashboard index directly.
        const string schema = "status_added_index_test";
        await TestMessagingSchema.ApplyAsync(fixture.ConnectionString, schema, AbortToken);

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
            new CommandDefinition(TestMessagingSchema.DropSql(schema), cancellationToken: AbortToken)
        );
    }

    [Fact]
    public async Task should_be_idempotent()
    {
        // given
        const string schema = "idempotent_test";
        // when - run twice
        await TestMessagingSchema.ApplyAsync(fixture.ConnectionString, schema, AbortToken);
        await TestMessagingSchema.ApplyAsync(fixture.ConnectionString, schema, AbortToken); // Should not throw

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
            new CommandDefinition(TestMessagingSchema.DropSql(schema), cancellationToken: AbortToken)
        );
    }

    [Fact]
    public async Task should_name_constraints_after_the_table_in_every_schema()
    {
        // given - the shared default schema plus a second schema in the same database, so the
        // schema-scoped constraint names must not collide across them
        const string otherSchema = "constraint_names";
        await TestMessagingSchema.ApplyAsync(fixture.ConnectionString, "headless", AbortToken);
        await TestMessagingSchema.ApplyAsync(fixture.ConnectionString, otherSchema, AbortToken);

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
                )
                    // The schema runner's history table and its key share the schema but are not Messaging objects.
                    .Where(name => !name.Contains(SchemaRunner.HistoryTableName, StringComparison.Ordinal))
                    .ToList();

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
                new CommandDefinition(TestMessagingSchema.DropSql(otherSchema), cancellationToken: AbortToken)
            );
        }
    }

    [Fact]
    public void should_return_correct_table_names()
    {
        // given
        const string schema = "table_names";
        var tableNames = TestStorageOptions.TableNames(schema);

        // when & then
        tableNames.GetPublishedTableName().Should().Be($"{schema}.MessagingPublished");
        tableNames.GetReceivedTableName().Should().Be($"{schema}.MessagingReceived");
    }

    [Fact]
    public async Task should_create_nothing_when_cancelled_before_it_starts()
    {
        // given
        const string schema = "cancel_test";
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // when
        var act = () => TestMessagingSchema.ApplyAsync(fixture.ConnectionString, schema, cts.Token);

        // then - the runner honors the token before it opens a connection, so the schema never appears
        await act.Should().ThrowAsync<OperationCanceledException>();
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
}
