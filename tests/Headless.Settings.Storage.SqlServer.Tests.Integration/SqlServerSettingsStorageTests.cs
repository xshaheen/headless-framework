// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless;
using Headless.Hosting.Initialization;
using Headless.Security;
using Headless.Settings;
using Headless.Settings.Entities;
using Headless.Settings.Repositories;
using Headless.Testing.Tests;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

[Collection<SqlServerSettingsFixture>]
public sealed class SqlServerSettingsStorageTests(SqlServerSettingsFixture fixture) : TestBase
{
    private const string _Schema = "settings_sql_raw";

    [Fact]
    public async Task should_initialize_tables_and_round_trip_setting_value()
    {
        // given
        await _DropSchemaAsync();
        using var host = _CreateHost();

        // when
        await host.StartAsync(AbortToken);
        var initializer = host
            .Services.GetRequiredService<IEnumerable<IInitializer>>()
            .Single(x => x is IHostedLifecycleService);
        var repository = host.Services.GetRequiredService<ISettingValueRecordRepository>();
        var record = new SettingValueRecord(Guid.NewGuid(), "Theme", "Dark", "Global");
        await repository.InsertAsync(record, AbortToken);
        var stored = await repository.FindAsync("Theme", "Global", null, AbortToken);
        var changed = new SettingValueRecord(record.Id, "Theme", "Light", "Global");
        await repository.UpdateAsync(changed, AbortToken);
        var updated = await repository.FindAsync("Theme", "Global", null, AbortToken);

        // then
        initializer.IsInitialized.Should().BeTrue();
        (await _TableExistsAsync("SettingValues")).Should().BeTrue();
        (await _TableExistsAsync("SettingDefinitions")).Should().BeTrue();
        stored.Should().NotBeNull();
        stored!.Value.Should().Be("Dark");
        stored.DateCreated.Should().NotBe(default);
        stored.DateUpdated.Should().BeNull();
        updated.Should().NotBeNull();
        updated!.Value.Should().Be("Light");
        updated.DateCreated.Should().NotBe(default);
        updated.DateUpdated.Should().NotBeNull();
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
        (await _IndexExistsAsync("SettingDefinitions", "IX_SettingDefinitions_Name"))
            .Should()
            .BeTrue();
        (await _IndexExistsAsync("SettingValues", "IX_SettingValues_Name_ProviderName_ProviderKey")).Should().BeTrue();
    }

    [Fact]
    public async Task should_return_empty_list_when_name_filter_is_empty()
    {
        // given
        await _DropSchemaAsync();
        using var host = _CreateHost();
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
        await _DropSchemaAsync();
        using var host = _CreateHost();
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

    private IHost _CreateHost()
    {
        var builder = Host.CreateApplicationBuilder();
        // unify: management-core deps
        builder.Services.AddSingleton(TimeProvider.System);
        // AddHeadlessSettings now registers the management core, which requires IStringEncryptionService.
        builder.Configuration.AddInMemoryCollection([
            new KeyValuePair<string, string?>("Headless:StringEncryption:DefaultPassPhrase", "TestPassPhrase123456"),
            new KeyValuePair<string, string?>("Headless:StringEncryption:InitVectorBytes", "VGVzdElWMDEyMzQ1Njc4OQ=="),
            new KeyValuePair<string, string?>("Headless:StringEncryption:DefaultSalt", "VGVzdFNhbHQ="),
        ]);
        builder.Services.AddStringEncryptionService(
            builder.Configuration.GetRequiredSection("Headless:StringEncryption")
        );
        builder.Services.AddHeadlessSettings(setup =>
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
            IF OBJECT_ID(N'{_Schema}.SettingValues', N'U') IS NOT NULL DROP TABLE [{_Schema}].[SettingValues];
            IF OBJECT_ID(N'{_Schema}.SettingDefinitions', N'U') IS NOT NULL DROP TABLE [{_Schema}].[SettingDefinitions];
            IF TYPE_ID(N'{_Schema}.HeadlessSettingsIdList') IS NOT NULL DROP TYPE [{_Schema}].[HeadlessSettingsIdList];
            IF TYPE_ID(N'{_Schema}.HeadlessSettingsNameList') IS NOT NULL DROP TYPE [{_Schema}].[HeadlessSettingsNameList];
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
                [DateCreated] datetimeoffset NOT NULL,
                [DateUpdated] datetimeoffset NULL,
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
        table.Columns.Add("DateCreated", typeof(DateTimeOffset));

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
        bulkCopy.ColumnMappings.Add("DateCreated", "DateCreated");

        await bulkCopy.WriteToServerAsync(table, AbortToken);
    }
}
