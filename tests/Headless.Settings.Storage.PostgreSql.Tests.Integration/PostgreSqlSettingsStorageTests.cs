// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;
using Headless.Security;
using Headless.Settings;
using Headless.Settings.Entities;
using Headless.Settings.Repositories;
using Headless.Testing.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Tests;

[Collection<PostgreSqlSettingsFixture>]
public sealed class PostgreSqlSettingsStorageTests(PostgreSqlSettingsFixture fixture) : TestBase
{
    private const string _Schema = "settings_pg_raw";

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
    public async Task should_reject_duplicate_setting_values_when_provider_key_is_null()
    {
        // given
        await _DropSchemaAsync();
        using var host = _CreateHost();
        await host.StartAsync(AbortToken);
        var repository = host.Services.GetRequiredService<ISettingValueRecordRepository>();
        var first = new SettingValueRecord(Guid.NewGuid(), "Theme", "Dark", "Global", null);
        var duplicate = new SettingValueRecord(Guid.NewGuid(), "Theme", "Light", "Global", null);
        await repository.InsertAsync(first, AbortToken);

        // when
        var action = async () => await repository.InsertAsync(duplicate, AbortToken);

        // then
        await action
            .Should()
            .ThrowAsync<PostgresException>()
            .Where(exception => exception.SqlState == PostgresErrorCodes.UniqueViolation);
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
        (await _IndexExistsAsync("IX_SettingDefinitions_Name"))
            .Should()
            .BeTrue();
        (await _IndexExistsAsync("IX_SettingValues_Name_ProviderName_ProviderKey")).Should().BeTrue();
        (await _IndexExistsAsync("IX_SettingValues_Name_ProviderName_NullProviderKey")).Should().BeTrue();
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
            setup.UsePostgreSql(fixture.ConnectionString);
        });

        return builder.Build();
    }

    private async Task _DropSchemaAsync()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new NpgsqlCommand($"""DROP SCHEMA IF EXISTS "{_Schema}" CASCADE;""", connection);
        await command.ExecuteNonQueryAsync(AbortToken);
    }

    private async Task<bool> _TableExistsAsync(string tableName)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.tables
                WHERE table_schema = @schema AND table_name = @table
            )
            """,
            connection
        );
        command.Parameters.AddWithValue("schema", _Schema);
        command.Parameters.AddWithValue("table", tableName);

        return (bool)(await command.ExecuteScalarAsync(AbortToken))!;
    }

    private async Task<bool> _IndexExistsAsync(string indexName)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM pg_indexes
                WHERE schemaname = @schema AND indexname = @index
            )
            """,
            connection
        );
        command.Parameters.AddWithValue("schema", _Schema);
        command.Parameters.AddWithValue("index", indexName);

        return (bool)(await command.ExecuteScalarAsync(AbortToken))!;
    }

    private async Task<bool> _ColumnExistsAsync(string tableName, string columnName)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = @schema AND table_name = @table AND column_name = @column
            )
            """,
            connection
        );
        command.Parameters.AddWithValue("schema", _Schema);
        command.Parameters.AddWithValue("table", tableName);
        command.Parameters.AddWithValue("column", columnName);

        return (bool)(await command.ExecuteScalarAsync(AbortToken))!;
    }

    private async Task _CreateLegacyValueTableAsync(Guid id, DateTimeOffset createdAt, DateTimeOffset updatedAt)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new NpgsqlCommand(
            $"""
            CREATE SCHEMA "{_Schema}";
            CREATE TABLE "{_Schema}"."SettingValues" (
                "Id" uuid NOT NULL PRIMARY KEY,
                "Name" character varying(128) NOT NULL,
                "Value" character varying(2000) NOT NULL,
                "ProviderName" character varying(64) NOT NULL,
                "ProviderKey" character varying(64),
                "DateCreated" timestamp with time zone NOT NULL,
                "DateUpdated" timestamp with time zone
            );
            INSERT INTO "{_Schema}"."SettingValues"
                ("Id", "Name", "Value", "ProviderName", "ProviderKey", "DateCreated", "DateUpdated")
            VALUES (@id, 'Legacy.Theme', 'Dark', 'Global', NULL, @createdAt, @updatedAt);
            """,
            connection
        );
        command.Parameters.AddWithValue(nameof(id), id);
        command.Parameters.AddWithValue(nameof(createdAt), createdAt);
        command.Parameters.AddWithValue(nameof(updatedAt), updatedAt);
        await command.ExecuteNonQueryAsync(AbortToken);
    }

    private async Task _CreateTablesWithoutIndexesAsync()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new NpgsqlCommand(
            $"""
            CREATE SCHEMA IF NOT EXISTS "{_Schema}";

            CREATE TABLE IF NOT EXISTS "{_Schema}"."SettingDefinitions" (
                "Id" uuid NOT NULL,
                "Name" character varying(128) NOT NULL,
                "DisplayName" character varying(256) NOT NULL,
                "Description" character varying(512),
                "DefaultValue" character varying(2000),
                "IsVisibleToClients" boolean NOT NULL,
                "IsInherited" boolean NOT NULL,
                "IsEncrypted" boolean NOT NULL,
                "Providers" character varying(1024),
                "ExtraProperties" text NOT NULL,
                CONSTRAINT "PK_SettingDefinitions" PRIMARY KEY ("Id")
            );

            CREATE TABLE IF NOT EXISTS "{_Schema}"."SettingValues" (
                "Id" uuid NOT NULL,
                "Name" character varying(128) NOT NULL,
                "Value" character varying(2000) NOT NULL,
                "ProviderName" character varying(64) NOT NULL,
                "ProviderKey" character varying(64),
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone,
                CONSTRAINT "PK_SettingValues" PRIMARY KEY ("Id")
            );
            """,
            connection
        );
        await command.ExecuteNonQueryAsync(AbortToken);
    }
}
