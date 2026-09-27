// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings.Entities;
using Headless.Settings.Repositories;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Tests;

[Collection<PostgreSqlSettingsFixture>]
public sealed class PostgreSqlSettingsStorageTests(PostgreSqlSettingsFixture fixture) : TestBase
{
    private const string _Schema = "settings_pg_raw";

    [Fact]
    public async Task should_reject_duplicate_setting_values_when_provider_key_is_null()
    {
        // given
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        using var host = fixture.CreateHost(_Schema);
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
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        await _CreateTablesWithoutIndexesAsync();
        using var host = fixture.CreateHost(_Schema);

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
