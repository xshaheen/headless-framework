// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.Settings.Entities;
using Headless.Settings.Repositories;
using Headless.Testing.Tests;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

[Collection<SqlServerSettingsFixture>]
public sealed class SqlServerSettingsStorageTests(SqlServerSettingsFixture fixture) : TestBase
{
    private const string _Schema = "settings_sql_raw";

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
        (await _IndexExistsAsync("SettingDefinitions", "IX_SettingDefinitions_Name"))
            .Should()
            .BeTrue();
        (await _IndexExistsAsync("SettingValues", "IX_SettingValues_Name_ProviderName_ProviderKey")).Should().BeTrue();
    }

    [Fact]
    public async Task should_rename_legacy_timestamp_columns_without_losing_setting_value()
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
        var repository = host.Services.GetRequiredService<ISettingValueRecordRepository>();
        var stored = await repository.FindAsync("Legacy.Theme", "Global", null, AbortToken);

        // then
        stored.Should().NotBeNull();
        stored!.Id.Should().Be(id);
        stored.CreatedAt.Should().Be(createdAt);
        stored.UpdatedAt.Should().Be(updatedAt);
        (await _ColumnExistsAsync("SettingValues", "DateCreated")).Should().BeFalse();
        (await _ColumnExistsAsync("SettingValues", "DateUpdated")).Should().BeFalse();
    }

    [Fact]
    public async Task should_return_empty_list_when_name_filter_is_empty()
    {
        // given
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        using var host = fixture.CreateHost(_Schema);
        await host.StartAsync(AbortToken);
        var repository = host.Services.GetRequiredService<ISettingValueRecordRepository>();

        // when
        var values = await repository.GetListAsync([], "Global", null, AbortToken);

        // then
        values.Should().BeEmpty();
    }

    [Fact]
    public async Task should_delete_setting_values_in_chunks_when_count_exceeds_sql_server_parameter_limit()
    {
        // given
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        using var host = fixture.CreateHost(_Schema);
        await host.StartAsync(AbortToken);
        await _BulkInsertSettingValuesAsync(totalRows: 2101);
        var repository = host.Services.GetRequiredService<ISettingValueRecordRepository>();
        var stored = await repository.GetListAsync("Global", "bulk", AbortToken);

        // when
        await repository.DeleteAsync(stored, AbortToken);
        var remaining = await repository.GetListAsync("Global", "bulk", AbortToken);

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
            CREATE TABLE [{_Schema}].[SettingValues] (
                [Id] uniqueidentifier NOT NULL PRIMARY KEY,
                [Name] nvarchar(128) NOT NULL,
                [Value] nvarchar(2000) NOT NULL,
                [ProviderName] nvarchar(64) NOT NULL,
                [ProviderKey] nvarchar(64) NULL,
                [DateCreated] datetimeoffset NOT NULL,
                [DateUpdated] datetimeoffset NULL
            );
            INSERT INTO [{_Schema}].[SettingValues]
                ([Id], [Name], [Value], [ProviderName], [ProviderKey], [DateCreated], [DateUpdated])
            VALUES (@id, N'Legacy.Theme', N'Dark', N'Global', NULL, @createdAt, @updatedAt);
            """,
            connection
        );
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@createdAt", createdAt);
        command.Parameters.AddWithValue("@updatedAt", updatedAt);
        await command.ExecuteNonQueryAsync(AbortToken);
    }

    private async Task _CreateTablesWithoutIndexesAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new SqlCommand(
            $"""
            IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = N'{_Schema}') EXEC(N'CREATE SCHEMA [{_Schema}]');

            CREATE TABLE [{_Schema}].[SettingDefinitions] (
                [Id] uniqueidentifier NOT NULL,
                [Name] nvarchar(128) NOT NULL,
                [DisplayName] nvarchar(256) NOT NULL,
                [Description] nvarchar(512) NULL,
                [DefaultValue] nvarchar(2000) NULL,
                [IsVisibleToClients] bit NOT NULL,
                [IsInherited] bit NOT NULL,
                [IsEncrypted] bit NOT NULL,
                [Providers] nvarchar(1024) NULL,
                [ExtraProperties] nvarchar(max) NOT NULL,
                CONSTRAINT [PK_SettingDefinitions] PRIMARY KEY CLUSTERED ([Id] ASC)
            );

            CREATE TABLE [{_Schema}].[SettingValues] (
                [Id] uniqueidentifier NOT NULL,
                [Name] nvarchar(128) NOT NULL,
                [Value] nvarchar(2000) NOT NULL,
                [ProviderName] nvarchar(64) NOT NULL,
                [ProviderKey] nvarchar(64) NULL,
                [CreatedAt] datetimeoffset NOT NULL,
                [UpdatedAt] datetimeoffset NULL,
                CONSTRAINT [PK_SettingValues] PRIMARY KEY CLUSTERED ([Id] ASC)
            );
            """,
            connection
        );
        await command.ExecuteNonQueryAsync(AbortToken);
    }

    private async Task _BulkInsertSettingValuesAsync(int totalRows)
    {
        using var table = new DataTable();
        table.Columns.Add("Id", typeof(Guid));
        table.Columns.Add("Name", typeof(string));
        table.Columns.Add("Value", typeof(string));
        table.Columns.Add("ProviderName", typeof(string));
        table.Columns.Add("ProviderKey", typeof(string));
        table.Columns.Add("CreatedAt", typeof(DateTimeOffset));

        for (var i = 0; i < totalRows; i++)
        {
            table.Rows.Add(Guid.NewGuid(), $"Setting_{i:D4}", "true", "Global", "bulk", DateTimeOffset.UtcNow);
        }

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        using var bulkCopy = new SqlBulkCopy(connection);

        bulkCopy.DestinationTableName = $"[{_Schema}].[SettingValues]";
        bulkCopy.ColumnMappings.Add("Id", "Id");
        bulkCopy.ColumnMappings.Add("Name", "Name");
        bulkCopy.ColumnMappings.Add("Value", "Value");
        bulkCopy.ColumnMappings.Add("ProviderName", "ProviderName");
        bulkCopy.ColumnMappings.Add("ProviderKey", "ProviderKey");
        bulkCopy.ColumnMappings.Add("CreatedAt", "CreatedAt");

        await bulkCopy.WriteToServerAsync(table, AbortToken);
    }
}
