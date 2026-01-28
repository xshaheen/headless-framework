// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Dapper;
using Headless.Messaging.Configuration;
using Headless.Messaging.Persistence;
using Headless.Messaging.SqlServer;
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
        await using var connection = new SqlConnection(fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        var schemaExists = await connection.QueryFirstOrDefaultAsync<int>(
            "SELECT 1 FROM sys.schemas WHERE name = @Schema",
            new { Schema = customSchema }
        );
        schemaExists.Should().Be(1);

        // cleanup
        await connection.ExecuteAsync(
            $"DROP TABLE IF EXISTS [{customSchema}].Published; DROP TABLE IF EXISTS [{customSchema}].Received; DROP SCHEMA IF EXISTS [{customSchema}]"
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
        await using var connection = new SqlConnection(fixture.Container.GetConnectionString());
        await connection.OpenAsync();

        var columns = await connection.QueryAsync<string>(
            """
            SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = @Schema AND TABLE_NAME = 'Published'
            """,
            new { Schema = schema }
        );

        columns.Should().Contain(["Id", "Version", "Name", "Content", "Retries", "Added", "ExpiresAt", "StatusName"]);

        // cleanup
        await connection.ExecuteAsync(
            $"DROP TABLE IF EXISTS [{schema}].Published; DROP TABLE IF EXISTS [{schema}].Received; DROP SCHEMA IF EXISTS [{schema}]"
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
        await using var connection = new SqlConnection(fixture.Container.GetConnectionString());
        await connection.OpenAsync();

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
            $"DROP TABLE IF EXISTS [{schema}].Published; DROP TABLE IF EXISTS [{schema}].Received; DROP SCHEMA IF EXISTS [{schema}]"
        );
    }

    [Fact]
    public async Task should_create_lock_table_when_storage_lock_enabled()
    {
        // given
        const string schema = "lock_test";
        var initializer = _CreateInitializer(schema, useStorageLock: true);

        // when
        await initializer.InitializeAsync(AbortToken);

        // then
        await using var connection = new SqlConnection(fixture.Container.GetConnectionString());
        await connection.OpenAsync();

        var tableExists = await connection.QueryFirstOrDefaultAsync<int>(
            """
            SELECT 1 FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_SCHEMA = @Schema AND TABLE_NAME = 'Lock'
            """,
            new { Schema = schema }
        );

        tableExists.Should().Be(1);

        // cleanup
        await connection.ExecuteAsync(
            $"DROP TABLE IF EXISTS [{schema}].Published; DROP TABLE IF EXISTS [{schema}].Received; DROP TABLE IF EXISTS [{schema}].Lock; DROP SCHEMA IF EXISTS [{schema}]"
        );
    }

    [Fact]
    public async Task should_not_create_lock_table_when_storage_lock_disabled()
    {
        // given
        const string schema = "no_lock_test";
        var initializer = _CreateInitializer(schema, useStorageLock: false);

        // when
        await initializer.InitializeAsync(AbortToken);

        // then
        await using var connection = new SqlConnection(fixture.Container.GetConnectionString());
        await connection.OpenAsync();

        var tableExists = await connection.QueryFirstOrDefaultAsync<int?>(
            """
            SELECT 1 FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_SCHEMA = @Schema AND TABLE_NAME = 'Lock'
            """,
            new { Schema = schema }
        );

        tableExists.Should().BeNull();

        // cleanup
        await connection.ExecuteAsync(
            $"DROP TABLE IF EXISTS [{schema}].Published; DROP TABLE IF EXISTS [{schema}].Received; DROP SCHEMA IF EXISTS [{schema}]"
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
        await using var connection = new SqlConnection(fixture.Container.GetConnectionString());
        await connection.OpenAsync();

        var indexes = await connection.QueryAsync<string>(
            """
            SELECT i.name
            FROM sys.indexes i
            INNER JOIN sys.tables t ON i.object_id = t.object_id
            INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE s.name = @Schema AND t.name = 'Published' AND i.name IS NOT NULL
            """,
            new { Schema = schema }
        );

        indexes.Should().HaveCountGreaterThanOrEqualTo(2); // At least PK + some indexes

        // cleanup
        await connection.ExecuteAsync(
            $"DROP TABLE IF EXISTS [{schema}].Published; DROP TABLE IF EXISTS [{schema}].Received; DROP SCHEMA IF EXISTS [{schema}]"
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
        await using var connection = new SqlConnection(fixture.Container.GetConnectionString());
        await connection.OpenAsync();

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
            $"DROP TABLE IF EXISTS [{schema}].Published; DROP TABLE IF EXISTS [{schema}].Received; DROP SCHEMA IF EXISTS [{schema}]"
        );
    }

    [Fact]
    public void should_return_correct_table_names()
    {
        // given
        const string schema = "table_names";
        var initializer = _CreateInitializer(schema, useStorageLock: true);

        // when / then
        initializer.GetPublishedTableName().Should().Be($"{schema}.Published");
        initializer.GetReceivedTableName().Should().Be($"{schema}.Received");
        initializer.GetLockTableName().Should().Be($"{schema}.Lock");
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
        await using var connection = new SqlConnection(fixture.Container.GetConnectionString());
        await connection.OpenAsync();

        var schemaExists = await connection.QueryFirstOrDefaultAsync<int?>(
            "SELECT 1 FROM sys.schemas WHERE name = @Schema",
            new { Schema = schema }
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
            x.ConnectionString = fixture.Container.GetConnectionString();
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
