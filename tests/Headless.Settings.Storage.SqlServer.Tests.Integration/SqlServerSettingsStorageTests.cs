// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.Abstractions;
using Headless.Caching;
using Headless.Hosting.Initialization;
using Headless.Security;
using Headless.Settings;
using Headless.Settings.Definitions;
using Headless.Settings.Entities;
using Headless.Settings.Models;
using Headless.Settings.Repositories;
using Headless.Settings.Values;
using Headless.Testing.Tests;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

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
        stored.CreatedAt.Should().NotBe(default);
        stored.UpdatedAt.Should().BeNull();
        updated.Should().NotBeNull();
        updated!.Value.Should().Be("Light");
        updated.CreatedAt.Should().NotBe(default);
        updated.UpdatedAt.Should().NotBeNull();
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
    public async Task should_rename_legacy_timestamp_columns_without_losing_setting_value()
    {
        // given
        await _DropSchemaAsync();
        var id = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2026, 7, 25, 10, 0, 0, TimeSpan.Zero);
        var updatedAt = createdAt.AddMinutes(5);
        await _CreateLegacyValueTableAsync(id, createdAt, updatedAt);
        using var host = _CreateHost();

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

    [Fact]
    public async Task should_save_a_value_batch_of_inserts_updates_and_deletes()
    {
        // given
        await _DropSchemaAsync();
        using var host = _CreateHost();
        await host.StartAsync(AbortToken);
        var repository = host.Services.GetRequiredService<ISettingValueRecordRepository>();
        var kept = new SettingValueRecord(Guid.NewGuid(), "Theme", "old", "Tenant", "t1");
        var removed = new SettingValueRecord(Guid.NewGuid(), "Font", "old", "Tenant", "t1");
        await repository.InsertAsync(kept, AbortToken);
        await repository.InsertAsync(removed, AbortToken);
        var added = new SettingValueRecord(Guid.NewGuid(), "Locale", "new", "Tenant", "t1");
        var changed = new SettingValueRecord(kept.Id, "Theme", "new", "Tenant", "t1");

        // when
        await repository.SaveAsync([added], [changed], [removed], AbortToken);

        // then
        var stored = await repository.GetListAsync("Tenant", "t1", AbortToken);
        stored.Select(x => (x.Name, x.Value)).Should().BeEquivalentTo([("Theme", "new"), ("Locale", "new")]);
    }

    [Fact]
    public async Task should_leave_every_value_unchanged_when_a_write_in_the_batch_fails()
    {
        // given
        await _DropSchemaAsync();
        using var host = _CreateHost();
        await host.StartAsync(AbortToken);
        var repository = host.Services.GetRequiredService<ISettingValueRecordRepository>();
        var first = new SettingValueRecord(Guid.NewGuid(), "Theme", "old", "Tenant", "t1");
        var second = new SettingValueRecord(Guid.NewGuid(), "Font", "old", "Tenant", "t1");
        await repository.InsertAsync(first, AbortToken);
        await repository.InsertAsync(second, AbortToken);
        var added = new SettingValueRecord(Guid.NewGuid(), "Locale", "new", "Tenant", "t1");
        var validUpdate = new SettingValueRecord(first.Id, "Theme", "new", "Tenant", "t1");

        // the column rejects this value, so the batch fails after the insert and the first update already ran
        var failingUpdate = new SettingValueRecord(
            second.Id,
            "Font",
            new string('x', SettingValueRecordConstants.ValueMaxLength + 1),
            "Tenant",
            "t1"
        );

        // when
        var act = async () => await repository.SaveAsync([added], [validUpdate, failingUpdate], [], AbortToken);

        // then
        await act.Should().ThrowAsync<SqlException>();
        var stored = await repository.GetListAsync("Tenant", "t1", AbortToken);
        stored.Select(x => (x.Name, x.Value)).Should().BeEquivalentTo([("Theme", "old"), ("Font", "old")]);
    }

    [Fact]
    public async Task should_roll_back_the_batch_when_an_updated_row_was_deleted_by_another_writer()
    {
        // given
        await _DropSchemaAsync();
        using var host = _CreateHost();
        await host.StartAsync(AbortToken);
        var repository = host.Services.GetRequiredService<ISettingValueRecordRepository>();
        var added = new SettingValueRecord(Guid.NewGuid(), "Theme", "new", "Tenant", "t1");
        var vanished = new SettingValueRecord(Guid.NewGuid(), "Font", "new", "Tenant", "t1");

        // when the batch updates a row nobody stored (another writer deleted it after it was read)
        var act = async () => await repository.SaveAsync([added], [vanished], [], AbortToken);

        // then the whole batch fails and the insert that ran before it is rolled back
        await act.Should().ThrowAsync<DBConcurrencyException>();
        (await repository.GetListAsync("Tenant", "t1", AbortToken)).Should().BeEmpty();
    }

    [Fact]
    public async Task should_let_every_concurrent_writer_of_a_new_name_succeed()
    {
        // given
        await _DropSchemaAsync();
        using var host = _CreateHost();
        await host.StartAsync(AbortToken);
        // The store reads definitions only to warm its cache; the storage-only host does not register the
        // definition manager's dependencies, so the store is built over the host's real repository and cache.
        var store = new SettingValueStore(
            host.Services.GetRequiredService<ISettingValueRecordRepository>(),
            Substitute.For<ISettingDefinitionManager>(),
            new SequentialGuidGenerator(SequentialGuidType.Version7),
            host.Services.GetRequiredService<ICache<SettingValueCacheItem>>(),
            Options.Create(new SettingManagementOptions())
        );
        var repository = host.Services.GetRequiredService<ISettingValueRecordRepository>();

        // when several writers set the same, not yet stored, name at once
        var writes = Enumerable
            .Range(0, 8)
            .Select(i =>
                store.SetAllAsync(
                    new Dictionary<string, string?>(StringComparer.Ordinal) { ["Theme"] = $"v{i}" },
                    "Tenant",
                    "t1",
                    AbortToken
                )
            );
        await Task.WhenAll(writes);

        // then every write succeeds and exactly one row holds one of their values
        var stored = await repository.GetListAsync("Tenant", "t1", AbortToken);
        stored.Should().ContainSingle().Which.Value.Should().MatchRegex("^v[0-7]$");
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
        // The value store caches every read, and the host refuses to start without a registered cache.
        builder.Services.AddHeadlessCaching(setup => setup.UseInMemory());
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
