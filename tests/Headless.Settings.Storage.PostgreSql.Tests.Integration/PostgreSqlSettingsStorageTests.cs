// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings;
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
        (await _IndexExistsAsync("ix_setting_definitions_name"))
            .Should()
            .BeTrue();
        (await _IndexExistsAsync("ix_setting_values_name_provider_name_provider_key")).Should().BeTrue();
        (await _IndexExistsAsync("ix_setting_values_name_provider_name_null_provider_key")).Should().BeTrue();
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

    private async Task _CreateTablesWithoutIndexesAsync()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new NpgsqlCommand(
            $"""
            CREATE SCHEMA IF NOT EXISTS "{_Schema}";

            CREATE TABLE IF NOT EXISTS "{_Schema}".setting_definitions (
                id uuid NOT NULL,
                name character varying(128) NOT NULL,
                display_name character varying(256) NOT NULL,
                description character varying(512),
                default_value character varying(2000),
                is_visible_to_clients boolean NOT NULL,
                is_inherited boolean NOT NULL,
                is_encrypted boolean NOT NULL,
                providers character varying(1024),
                extra_properties text NOT NULL,
                CONSTRAINT pk_setting_definitions PRIMARY KEY (id)
            );

            CREATE TABLE IF NOT EXISTS "{_Schema}".setting_values (
                id uuid NOT NULL,
                name character varying(128) NOT NULL,
                value character varying(2000) NOT NULL,
                provider_name character varying(64) NOT NULL,
                provider_key character varying(64),
                created_at timestamp with time zone NOT NULL,
                updated_at timestamp with time zone,
                CONSTRAINT pk_setting_values PRIMARY KEY (id)
            );
            """,
            connection
        );
        await command.ExecuteNonQueryAsync(AbortToken);
    }
}
