// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.Features;
using Headless.Features.Entities;
using Headless.Features.Repositories;
using Headless.Features.Seeders;
using Headless.Hosting.Initialization;
using Headless.Testing.Tests;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

[Collection<SqlServerFeaturesFixture>]
public sealed class SqlServerFeaturesStorageTests(SqlServerFeaturesFixture fixture) : TestBase
{
    private const string _Schema = "features_sql_raw";

    [Fact]
    public async Task should_initialize_tables_and_round_trip_feature_value_and_definition()
    {
        // given
        await _DropSchemaAsync();
        using var host = _CreateHost();

        // when
        await host.StartAsync(AbortToken);
        var initializer = host
            .Services.GetRequiredService<IEnumerable<IInitializer>>()
            .Single(x => x is not FeaturesInitializationBackgroundService);
        var valueRepository = host.Services.GetRequiredService<IFeatureValueRecordRepository>();
        var definitionRepository = host.Services.GetRequiredService<IFeatureDefinitionRecordRepository>();
        var record = new FeatureValueRecord(Guid.NewGuid(), "Checkout.Enabled", "true", "Edition", "pro");
        var group = new FeatureGroupDefinitionRecord(Guid.NewGuid(), "Checkout", "Checkout");
        var feature = new FeatureDefinitionRecord(
            Guid.NewGuid(),
            "Checkout",
            "Checkout.Enabled",
            null,
            "Checkout enabled"
        );

        await valueRepository.InsertAsync(record, AbortToken);
        await definitionRepository.SaveAsync([group], [], [], [feature], [], [], AbortToken);
        var stored = await valueRepository.FindAsync("Checkout.Enabled", "Edition", "pro", AbortToken);
        var storedGroups = await definitionRepository.GetGroupsListAsync(AbortToken);
        var storedFeatures = await definitionRepository.GetFeaturesListAsync(AbortToken);

        // then
        initializer.IsInitialized.Should().BeTrue();
        (await _TableExistsAsync("FeatureValues")).Should().BeTrue();
        (await _TableExistsAsync("FeatureDefinitions")).Should().BeTrue();
        (await _TableExistsAsync("FeatureGroupDefinitions")).Should().BeTrue();
        stored.Should().NotBeNull();
        stored!.Value.Should().Be("true");
        storedGroups.Should().ContainSingle(x => x.Name == "Checkout");
        storedFeatures.Should().ContainSingle(x => x.Name == "Checkout.Enabled");
    }

    [Fact]
    public async Task should_persist_all_definitions_across_multiple_chunks_when_batch_exceeds_chunk_size()
    {
        // given — chunk size is 100 rows; 150 forces two chunks
        await _DropSchemaAsync();
        using var host = _CreateHost();
        await host.StartAsync(AbortToken);
        var definitionRepository = host.Services.GetRequiredService<IFeatureDefinitionRecordRepository>();

        const int totalGroups = 150;
        const int totalFeatures = 150;
        var groups = Enumerable
            .Range(0, totalGroups)
            .Select(i => new FeatureGroupDefinitionRecord(Guid.NewGuid(), $"Group_{i:D4}", $"Group {i}"))
            .ToList();
        var features = Enumerable
            .Range(0, totalFeatures)
            .Select(i => new FeatureDefinitionRecord(
                Guid.NewGuid(),
                groups[i % totalGroups].Name,
                $"Feature_{i:D4}",
                null,
                $"Feature {i}"
            ))
            .ToList();

        // when
        await definitionRepository.SaveAsync(groups, [], [], features, [], [], AbortToken);
        var storedGroups = await definitionRepository.GetGroupsListAsync(AbortToken);
        var storedFeatures = await definitionRepository.GetFeaturesListAsync(AbortToken);

        // then
        storedGroups.Should().HaveCount(totalGroups);
        storedFeatures.Should().HaveCount(totalFeatures);
        storedGroups.Select(g => g.Name).Should().BeEquivalentTo(groups.Select(g => g.Name));
        storedFeatures.Select(f => f.Name).Should().BeEquivalentTo(features.Select(f => f.Name));
    }

    [Fact]
    public async Task should_create_missing_indexes_when_tables_already_exist()
    {
        // given
        await _DropSchemaAsync();
        await _CreateTablesWithoutIndexesAsync();
        using var host = _CreateHost();

        // when
        await host.StartAsync(AbortToken);

        // then
        (await _IndexExistsAsync("FeatureGroupDefinitions", "IX_FeatureGroupDefinitions_Name"))
            .Should()
            .BeTrue();
        (await _IndexExistsAsync("FeatureDefinitions", "IX_FeatureDefinitions_GroupName")).Should().BeTrue();
        (await _IndexExistsAsync("FeatureDefinitions", "IX_FeatureDefinitions_Name")).Should().BeTrue();
        (await _IndexExistsAsync("FeatureValues", "IX_FeatureValues_ProviderName_ProviderKey")).Should().BeTrue();
        (await _IndexExistsAsync("FeatureValues", "IX_FeatureValues_Name_ProviderName_ProviderKey")).Should().BeTrue();
    }

    [Fact]
    public async Task should_delete_feature_values_in_chunks_when_count_exceeds_sql_server_parameter_limit()
    {
        // given
        await _DropSchemaAsync();
        using var host = _CreateHost();
        await host.StartAsync(AbortToken);
        await _BulkInsertFeatureValuesAsync(totalRows: 2101);
        var valueRepository = host.Services.GetRequiredService<IFeatureValueRecordRepository>();
        var stored = await valueRepository.GetListAsync("Edition", "bulk", AbortToken);

        // when
        await valueRepository.DeleteAsync(stored, AbortToken);
        var remaining = await valueRepository.GetListAsync("Edition", "bulk", AbortToken);

        // then
        stored.Should().HaveCount(2101);
        remaining.Should().BeEmpty();
    }

    private IHost _CreateHost()
    {
        var builder = Host.CreateApplicationBuilder();
        // unify: management-core deps
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddHeadlessFeatures(setup =>
        {
            setup.ConfigureStorage(options => options.Schema = _Schema);
            setup.UseSqlServer(fixture.ConnectionString);
        });

        return builder.Build();
    }

    private async Task _DropSchemaAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new SqlCommand(
            $"""
            IF OBJECT_ID(N'{_Schema}.FeatureValues', N'U') IS NOT NULL DROP TABLE [{_Schema}].[FeatureValues];
            IF OBJECT_ID(N'{_Schema}.FeatureDefinitions', N'U') IS NOT NULL DROP TABLE [{_Schema}].[FeatureDefinitions];
            IF OBJECT_ID(N'{_Schema}.FeatureGroupDefinitions', N'U') IS NOT NULL DROP TABLE [{_Schema}].[FeatureGroupDefinitions];
            IF TYPE_ID(N'{_Schema}.HeadlessFeaturesIdList') IS NOT NULL DROP TYPE [{_Schema}].[HeadlessFeaturesIdList];
            IF EXISTS (SELECT * FROM sys.schemas WHERE name = N'{_Schema}') EXEC(N'DROP SCHEMA [{_Schema}]');
            """,
            connection
        );
        await command.ExecuteNonQueryAsync(AbortToken);
    }

    private async Task<bool> _TableExistsAsync(string tableName)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new SqlCommand(
            """
            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM information_schema.tables
                WHERE table_schema = @schema AND table_name = @table
            ) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END
            """,
            connection
        );
        command.Parameters.AddWithValue("@schema", _Schema);
        command.Parameters.AddWithValue("@table", tableName);

        return (bool)await command.ExecuteScalarAsync(AbortToken);
    }

    private async Task<bool> _IndexExistsAsync(string tableName, string indexName)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new SqlCommand(
            """
            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE object_id = OBJECT_ID(@qualifiedTable) AND name = @index
            ) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END
            """,
            connection
        );
        command.Parameters.AddWithValue("@qualifiedTable", $"{_Schema}.{tableName}");
        command.Parameters.AddWithValue("@index", indexName);

        return (bool)await command.ExecuteScalarAsync(AbortToken);
    }

    private async Task _CreateTablesWithoutIndexesAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new SqlCommand(
            $"""
            IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = N'{_Schema}') EXEC(N'CREATE SCHEMA [{_Schema}]');

            CREATE TABLE [{_Schema}].[FeatureGroupDefinitions] (
                [Id] uniqueidentifier NOT NULL,
                [Name] nvarchar(128) NOT NULL,
                [DisplayName] nvarchar(256) NOT NULL,
                [ExtraProperties] nvarchar(max) NOT NULL,
                CONSTRAINT [PK_FeatureGroupDefinitions] PRIMARY KEY CLUSTERED ([Id] ASC)
            );

            CREATE TABLE [{_Schema}].[FeatureDefinitions] (
                [Id] uniqueidentifier NOT NULL,
                [GroupName] nvarchar(128) NOT NULL,
                [Name] nvarchar(128) NOT NULL,
                [DisplayName] nvarchar(256) NOT NULL,
                [ParentName] nvarchar(128) NULL,
                [Description] nvarchar(256) NULL,
                [DefaultValue] nvarchar(256) NULL,
                [IsVisibleToClients] bit NOT NULL,
                [IsAvailableToHost] bit NOT NULL,
                [Providers] nvarchar(256) NULL,
                [ExtraProperties] nvarchar(max) NOT NULL,
                CONSTRAINT [PK_FeatureDefinitions] PRIMARY KEY CLUSTERED ([Id] ASC)
            );

            CREATE TABLE [{_Schema}].[FeatureValues] (
                [Id] uniqueidentifier NOT NULL,
                [Name] nvarchar(128) NOT NULL,
                [Value] nvarchar(128) NOT NULL,
                [ProviderName] nvarchar(64) NOT NULL,
                [ProviderKey] nvarchar(64) NULL,
                CONSTRAINT [PK_FeatureValues] PRIMARY KEY CLUSTERED ([Id] ASC)
            );
            """,
            connection
        );
        await command.ExecuteNonQueryAsync(AbortToken);
    }

    private async Task _BulkInsertFeatureValuesAsync(int totalRows)
    {
        using var table = new DataTable();
        table.Columns.Add("Id", typeof(Guid));
        table.Columns.Add("Name", typeof(string));
        table.Columns.Add("Value", typeof(string));
        table.Columns.Add("ProviderName", typeof(string));
        table.Columns.Add("ProviderKey", typeof(string));
        table.Columns.Add("DateCreated", typeof(DateTimeOffset));

        var dateCreated = TimeProvider.System.GetUtcNow();

        for (var i = 0; i < totalRows; i++)
        {
            table.Rows.Add(Guid.NewGuid(), $"Feature_{i:D4}", "true", "Edition", "bulk", dateCreated);
        }

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        using var bulkCopy = new SqlBulkCopy(connection);
        bulkCopy.DestinationTableName = $"[{_Schema}].[FeatureValues]";
        bulkCopy.ColumnMappings.Add("Id", "Id");
        bulkCopy.ColumnMappings.Add("Name", "Name");
        bulkCopy.ColumnMappings.Add("Value", "Value");
        bulkCopy.ColumnMappings.Add("ProviderName", "ProviderName");
        bulkCopy.ColumnMappings.Add("ProviderKey", "ProviderKey");
        bulkCopy.ColumnMappings.Add("DateCreated", "DateCreated");

        await bulkCopy.WriteToServerAsync(table, AbortToken);
    }
}
