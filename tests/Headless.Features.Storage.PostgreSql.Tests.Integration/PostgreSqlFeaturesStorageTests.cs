// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.Abstractions;
using Headless.Caching;
using Headless.Features;
using Headless.Features.Definitions;
using Headless.Features.Entities;
using Headless.Features.Repositories;
using Headless.Features.Values;
using Headless.Hosting.Initialization;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Tests;

[Collection<PostgreSqlFeaturesFixture>]
public sealed class PostgreSqlFeaturesStorageTests(PostgreSqlFeaturesFixture fixture) : TestBase
{
    private const string _Schema = "features_pg_raw";

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
            .Single(x => x is IHostedLifecycleService);
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
        // given — chunk size is 500 rows; 550 forces two chunks
        await _DropSchemaAsync();
        using var host = _CreateHost();
        await host.StartAsync(AbortToken);
        var definitionRepository = host.Services.GetRequiredService<IFeatureDefinitionRecordRepository>();

        const int totalGroups = 550;
        const int totalFeatures = 550;
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
    public async Task should_reject_duplicate_feature_values_when_provider_key_is_null()
    {
        // given
        await _DropSchemaAsync();
        using var host = _CreateHost();
        await host.StartAsync(AbortToken);
        var valueRepository = host.Services.GetRequiredService<IFeatureValueRecordRepository>();
        var first = new FeatureValueRecord(Guid.NewGuid(), "Checkout.Enabled", "true", "DefaultValue", null);
        var duplicate = new FeatureValueRecord(Guid.NewGuid(), "Checkout.Enabled", "false", "DefaultValue", null);
        await valueRepository.InsertAsync(first, AbortToken);

        // when
        var action = async () => await valueRepository.InsertAsync(duplicate, AbortToken);

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
        (await _IndexExistsAsync("IX_FeatureGroupDefinitions_Name"))
            .Should()
            .BeTrue();
        (await _IndexExistsAsync("IX_FeatureDefinitions_GroupName")).Should().BeTrue();
        (await _IndexExistsAsync("IX_FeatureDefinitions_Name")).Should().BeTrue();
        (await _IndexExistsAsync("IX_FeatureValues_ProviderName_ProviderKey")).Should().BeTrue();
        (await _IndexExistsAsync("IX_FeatureValues_Name_ProviderName_ProviderKey")).Should().BeTrue();
        (await _IndexExistsAsync("IX_FeatureValues_Name_ProviderName_NullProviderKey")).Should().BeTrue();
    }

    [Fact]
    public async Task should_rename_legacy_timestamp_columns_without_losing_feature_value()
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
    public async Task should_save_a_value_batch_of_inserts_updates_and_deletes()
    {
        // given
        await _DropSchemaAsync();
        using var host = _CreateHost();
        await host.StartAsync(AbortToken);
        var repository = host.Services.GetRequiredService<IFeatureValueRecordRepository>();
        var kept = new FeatureValueRecord(Guid.NewGuid(), "Checkout.Enabled", "old", "Tenant", "t1");
        var removed = new FeatureValueRecord(Guid.NewGuid(), "Reports.Enabled", "old", "Tenant", "t1");
        await repository.InsertAsync(kept, AbortToken);
        await repository.InsertAsync(removed, AbortToken);
        var added = new FeatureValueRecord(Guid.NewGuid(), "Export.Enabled", "new", "Tenant", "t1");
        var changed = new FeatureValueRecord(kept.Id, "Checkout.Enabled", "new", "Tenant", "t1");

        // when
        await repository.SaveAsync([added], [changed], [removed], AbortToken);

        // then
        var stored = await repository.GetListAsync("Tenant", "t1", AbortToken);
        stored
            .Select(x => (x.Name, x.Value))
            .Should()
            .BeEquivalentTo([("Checkout.Enabled", "new"), ("Export.Enabled", "new")]);
    }

    [Fact]
    public async Task should_read_only_the_requested_names_from_a_scope()
    {
        // given three values in one scope and a same-named value in another scope
        await _DropSchemaAsync();
        using var host = _CreateHost();
        await host.StartAsync(AbortToken);
        var repository = host.Services.GetRequiredService<IFeatureValueRecordRepository>();
        await repository.InsertAsync(
            new FeatureValueRecord(Guid.NewGuid(), "Checkout.Enabled", "a", "Tenant", "t1"),
            AbortToken
        );
        await repository.InsertAsync(
            new FeatureValueRecord(Guid.NewGuid(), "Reports.Enabled", "b", "Tenant", "t1"),
            AbortToken
        );
        await repository.InsertAsync(
            new FeatureValueRecord(Guid.NewGuid(), "Export.Enabled", "c", "Tenant", "t1"),
            AbortToken
        );
        await repository.InsertAsync(
            new FeatureValueRecord(Guid.NewGuid(), "Checkout.Enabled", "other", "Tenant", "t2"),
            AbortToken
        );

        // when
        var stored = await repository.GetListAsync(
            new HashSet<string>(StringComparer.Ordinal) { "Checkout.Enabled", "Export.Enabled" },
            "Tenant",
            "t1",
            AbortToken
        );

        // then
        stored
            .Select(x => (x.Name, x.Value))
            .Should()
            .BeEquivalentTo([("Checkout.Enabled", "a"), ("Export.Enabled", "c")]);
    }

    [Fact]
    public async Task should_leave_every_value_unchanged_when_a_write_in_the_batch_fails()
    {
        // given
        await _DropSchemaAsync();
        using var host = _CreateHost();
        await host.StartAsync(AbortToken);
        var repository = host.Services.GetRequiredService<IFeatureValueRecordRepository>();
        var first = new FeatureValueRecord(Guid.NewGuid(), "Checkout.Enabled", "old", "Tenant", "t1");
        var second = new FeatureValueRecord(Guid.NewGuid(), "Reports.Enabled", "old", "Tenant", "t1");
        await repository.InsertAsync(first, AbortToken);
        await repository.InsertAsync(second, AbortToken);
        var added = new FeatureValueRecord(Guid.NewGuid(), "Export.Enabled", "new", "Tenant", "t1");
        var validUpdate = new FeatureValueRecord(first.Id, "Checkout.Enabled", "new", "Tenant", "t1");

        // the column rejects this value, so the batch fails after the insert and the first update already ran
        var failingUpdate = new FeatureValueRecord(
            second.Id,
            "Reports.Enabled",
            new string('x', FeatureValueRecordConstants.ValueMaxLength + 1),
            "Tenant",
            "t1"
        );

        // when
        var act = async () => await repository.SaveAsync([added], [validUpdate, failingUpdate], [], AbortToken);

        // then
        await act.Should().ThrowAsync<PostgresException>();
        var stored = await repository.GetListAsync("Tenant", "t1", AbortToken);
        stored
            .Select(x => (x.Name, x.Value))
            .Should()
            .BeEquivalentTo([("Checkout.Enabled", "old"), ("Reports.Enabled", "old")]);
    }

    [Fact]
    public async Task should_roll_back_the_batch_when_an_updated_row_was_deleted_by_another_writer()
    {
        // given
        await _DropSchemaAsync();
        using var host = _CreateHost();
        await host.StartAsync(AbortToken);
        var repository = host.Services.GetRequiredService<IFeatureValueRecordRepository>();
        var added = new FeatureValueRecord(Guid.NewGuid(), "Checkout.Enabled", "new", "Tenant", "t1");
        var vanished = new FeatureValueRecord(Guid.NewGuid(), "Reports.Enabled", "new", "Tenant", "t1");

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
        var store = new FeatureValueStore(
            Substitute.For<IFeatureDefinitionManager>(),
            host.Services.GetRequiredService<IFeatureValueRecordRepository>(),
            new SequentialGuidGenerator(SequentialGuidType.Version7),
            host.Services.GetRequiredService<ICache>()
        );
        var repository = host.Services.GetRequiredService<IFeatureValueRecordRepository>();

        // when several writers set the same, not yet stored, name at once
        var writes = Enumerable
            .Range(0, 8)
            .Select(i =>
                store.SetAllAsync(
                    new Dictionary<string, string?>(StringComparer.Ordinal) { ["Checkout.Enabled"] = $"v{i}" },
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
        // The value store caches every read, and the host refuses to start without a registered cache.
        builder.Services.AddHeadlessCaching(setup => setup.UseInMemory());
        builder.Services.AddHeadlessFeatures(setup =>
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
            CREATE TABLE "{_Schema}"."FeatureValues" (
                "Id" uuid NOT NULL PRIMARY KEY,
                "Name" character varying(128) NOT NULL,
                "Value" character varying(128) NOT NULL,
                "ProviderName" character varying(64) NOT NULL,
                "ProviderKey" character varying(64),
                "DateCreated" timestamp with time zone NOT NULL,
                "DateUpdated" timestamp with time zone
            );
            INSERT INTO "{_Schema}"."FeatureValues"
                ("Id", "Name", "Value", "ProviderName", "ProviderKey", "DateCreated", "DateUpdated")
            VALUES (@id, 'Legacy.Feature', 'true', 'Edition', 'pro', @createdAt, @updatedAt);
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

            CREATE TABLE IF NOT EXISTS "{_Schema}"."FeatureGroupDefinitions" (
                "Id" uuid NOT NULL,
                "Name" character varying(128) NOT NULL,
                "DisplayName" character varying(256) NOT NULL,
                "ExtraProperties" text NOT NULL,
                CONSTRAINT "PK_FeatureGroupDefinitions" PRIMARY KEY ("Id")
            );

            CREATE TABLE IF NOT EXISTS "{_Schema}"."FeatureDefinitions" (
                "Id" uuid NOT NULL,
                "GroupName" character varying(128) NOT NULL,
                "Name" character varying(128) NOT NULL,
                "DisplayName" character varying(256) NOT NULL,
                "ParentName" character varying(128),
                "Description" character varying(256),
                "DefaultValue" character varying(256),
                "IsVisibleToClients" boolean NOT NULL,
                "IsAvailableToHost" boolean NOT NULL,
                "Providers" character varying(256),
                "ExtraProperties" text NOT NULL,
                CONSTRAINT "PK_FeatureDefinitions" PRIMARY KEY ("Id")
            );

            CREATE TABLE IF NOT EXISTS "{_Schema}"."FeatureValues" (
                "Id" uuid NOT NULL,
                "Name" character varying(128) NOT NULL,
                "Value" character varying(128) NOT NULL,
                "ProviderName" character varying(64) NOT NULL,
                "ProviderKey" character varying(64),
                CONSTRAINT "PK_FeatureValues" PRIMARY KEY ("Id")
            );
            """,
            connection
        );
        await command.ExecuteNonQueryAsync(AbortToken);
    }
}
