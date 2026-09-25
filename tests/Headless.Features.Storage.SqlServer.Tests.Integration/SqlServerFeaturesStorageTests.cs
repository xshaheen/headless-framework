// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.Features.Entities;
using Headless.Features.Repositories;
using Headless.Testing.Tests;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

[Collection<SqlServerFeaturesFixture>]
public sealed class SqlServerFeaturesStorageTests(SqlServerFeaturesFixture fixture) : TestBase
{
    private const string _Schema = "features_sql_raw";

    [Fact]
    public async Task should_persist_all_definitions_across_multiple_chunks_when_batch_exceeds_chunk_size()
    {
        // given — chunk size is 100 rows; 150 forces two chunks
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        using var host = fixture.CreateHost(_Schema);
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
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        await _CreateTablesWithoutIndexesAsync();
        using var host = fixture.CreateHost(_Schema);

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
    public async Task should_rename_legacy_timestamp_columns_without_losing_feature_value()
    {
        // given
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        var id = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2026, 7, 25, 10, 0, 0, TimeSpan.Zero);
        var updatedAt = createdAt.AddMinutes(5);
        await _CreateLegacyValueTableAsync(id, createdAt, updatedAt);
        using var host = fixture.CreateHost(_Schema);

        // when
        await host.StartAsync(AbortToken);
        var repository = host.Services.GetRequiredService<IFeatureValueRecordRepository>();
        var stored = await repository.FindAsync("Legacy.Feature", "Edition", "pro", AbortToken);

        // then
        stored.Should().NotBeNull();
        stored!.Id.Should().Be(id);
        stored.CreatedAt.Should().Be(createdAt);
        stored.UpdatedAt.Should().Be(updatedAt);
        (await _ColumnExistsAsync("FeatureValues", "DateCreated")).Should().BeFalse();
        (await _ColumnExistsAsync("FeatureValues", "DateUpdated")).Should().BeFalse();
    }

    [Fact]
    public async Task should_delete_feature_values_in_chunks_when_count_exceeds_sql_server_parameter_limit()
    {
        // given
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        using var host = fixture.CreateHost(_Schema);
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

    private async Task<bool> _ColumnExistsAsync(string tableName, string columnName)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new SqlCommand(
            """
            SELECT CASE WHEN COL_LENGTH(@qualifiedTable, @column) IS NOT NULL
                THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END
            """,
            connection
        );
        command.Parameters.AddWithValue("@qualifiedTable", $"{_Schema}.{tableName}");
        command.Parameters.AddWithValue("@column", columnName);

        return (bool)await command.ExecuteScalarAsync(AbortToken);
    }

    private async Task _CreateLegacyValueTableAsync(Guid id, DateTimeOffset createdAt, DateTimeOffset updatedAt)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new SqlCommand(
            $"""
            EXEC(N'CREATE SCHEMA [{_Schema}]');
            CREATE TABLE [{_Schema}].[FeatureValues] (
                [Id] uniqueidentifier NOT NULL PRIMARY KEY,
                [Name] nvarchar(128) NOT NULL,
                [Value] nvarchar(128) NOT NULL,
                [ProviderName] nvarchar(64) NOT NULL,
                [ProviderKey] nvarchar(64) NULL,
                [DateCreated] datetimeoffset NOT NULL,
                [DateUpdated] datetimeoffset NULL
            );
            INSERT INTO [{_Schema}].[FeatureValues]
                ([Id], [Name], [Value], [ProviderName], [ProviderKey], [DateCreated], [DateUpdated])
            VALUES (@id, N'Legacy.Feature', N'true', N'Edition', N'pro', @createdAt, @updatedAt);
            """,
            connection
        );
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@createdAt", createdAt);
        command.Parameters.AddWithValue("@updatedAt", updatedAt);
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
        table.Columns.Add("CreatedAt", typeof(DateTimeOffset));

        var createdAt = TimeProvider.System.GetUtcNow();

        for (var i = 0; i < totalRows; i++)
        {
            table.Rows.Add(Guid.NewGuid(), $"Feature_{i:D4}", "true", "Edition", "bulk", createdAt);
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
        bulkCopy.ColumnMappings.Add("CreatedAt", "CreatedAt");

        await bulkCopy.WriteToServerAsync(table, AbortToken);
    }
}
