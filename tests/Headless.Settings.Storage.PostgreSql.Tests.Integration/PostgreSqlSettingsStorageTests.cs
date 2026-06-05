// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Hosting.Initialization;
using Headless.Settings;
using Headless.Settings.Entities;
using Headless.Settings.Repositories;
using Headless.Settings.Seeders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Tests;

[Collection<PostgreSqlSettingsFixture>]
public sealed class PostgreSqlSettingsStorageTests(PostgreSqlSettingsFixture fixture)
{
    private const string _Schema = "settings_pg_raw";

    [Fact]
    public async Task should_initialize_tables_and_round_trip_setting_value()
    {
        // given
        await _DropSchemaAsync();
        using var host = _CreateHost();

        // when
        await host.StartAsync(TestContext.Current.CancellationToken);
        var initializer = host
            .Services.GetRequiredService<IEnumerable<IInitializer>>()
            .Single(x => x is not SettingsInitializationBackgroundService);
        var repository = host.Services.GetRequiredService<ISettingValueRecordRepository>();
        var record = new SettingValueRecord(Guid.NewGuid(), "Theme", "Dark", "Global");
        await repository.InsertAsync(record, TestContext.Current.CancellationToken);
        var stored = await repository.FindAsync("Theme", "Global", null, TestContext.Current.CancellationToken);
        var changed = new SettingValueRecord(record.Id, "Theme", "Light", "Global");
        await repository.UpdateAsync(changed, TestContext.Current.CancellationToken);
        var updated = await repository.FindAsync("Theme", "Global", null, TestContext.Current.CancellationToken);

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
    public async Task should_reject_duplicate_setting_values_when_provider_key_is_null()
    {
        // given
        await _DropSchemaAsync();
        using var host = _CreateHost();
        await host.StartAsync(TestContext.Current.CancellationToken);
        var repository = host.Services.GetRequiredService<ISettingValueRecordRepository>();
        var first = new SettingValueRecord(Guid.NewGuid(), "Theme", "Dark", "Global", null);
        var duplicate = new SettingValueRecord(Guid.NewGuid(), "Theme", "Light", "Global", null);
        await repository.InsertAsync(first, TestContext.Current.CancellationToken);

        // when
        var action = async () => await repository.InsertAsync(duplicate, TestContext.Current.CancellationToken);

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
        await host.StartAsync(TestContext.Current.CancellationToken);

        // then
        (await _IndexExistsAsync("IX_SettingDefinitions_Name"))
            .Should()
            .BeTrue();
        (await _IndexExistsAsync("IX_SettingValues_Name_ProviderName_ProviderKey")).Should().BeTrue();
        (await _IndexExistsAsync("IX_SettingValues_Name_ProviderName_NullProviderKey")).Should().BeTrue();
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
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand($"""DROP SCHEMA IF EXISTS "{_Schema}" CASCADE;""", connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task<bool> _TableExistsAsync(string tableName)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
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

        return (bool)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private async Task<bool> _IndexExistsAsync(string indexName)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
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

        return (bool)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private async Task _CreateTablesWithoutIndexesAsync()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
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
                "DateCreated" timestamp with time zone NOT NULL,
                "DateUpdated" timestamp with time zone,
                CONSTRAINT "PK_SettingValues" PRIMARY KEY ("Id")
            );
            """,
            connection
        );
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
