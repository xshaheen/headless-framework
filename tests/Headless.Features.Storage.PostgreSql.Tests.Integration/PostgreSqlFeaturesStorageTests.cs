// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features;
using Headless.Features.Entities;
using Headless.Features.PostgreSql;
using Headless.Features.Repositories;
using Headless.Features.Seeders;
using Headless.Hosting.Initialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Tests;

[Collection<PostgreSqlFeaturesFixture>]
public sealed class PostgreSqlFeaturesStorageTests(PostgreSqlFeaturesFixture fixture)
{
    private const string _Schema = "features_pg_raw";

    [Fact]
    public async Task should_initialize_tables_and_round_trip_feature_value_and_definition()
    {
        // given
        await _DropSchemaAsync();
        using var host = _CreateHost();

        // when
        await host.StartAsync(TestContext.Current.CancellationToken);
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

        await valueRepository.InsertAsync(record, TestContext.Current.CancellationToken);
        await definitionRepository.SaveAsync([group], [], [], [feature], [], [], TestContext.Current.CancellationToken);
        var stored = await valueRepository.FindAsync(
            "Checkout.Enabled",
            "Edition",
            "pro",
            TestContext.Current.CancellationToken
        );
        var storedGroups = await definitionRepository.GetGroupsListAsync(TestContext.Current.CancellationToken);
        var storedFeatures = await definitionRepository.GetFeaturesListAsync(TestContext.Current.CancellationToken);

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
        await host.StartAsync(TestContext.Current.CancellationToken);
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
        await definitionRepository.SaveAsync(groups, [], [], features, [], [], TestContext.Current.CancellationToken);
        var storedGroups = await definitionRepository.GetGroupsListAsync(TestContext.Current.CancellationToken);
        var storedFeatures = await definitionRepository.GetFeaturesListAsync(TestContext.Current.CancellationToken);

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
        await host.StartAsync(TestContext.Current.CancellationToken);
        var valueRepository = host.Services.GetRequiredService<IFeatureValueRecordRepository>();
        var first = new FeatureValueRecord(Guid.NewGuid(), "Checkout.Enabled", "true", "DefaultValue", null);
        var duplicate = new FeatureValueRecord(Guid.NewGuid(), "Checkout.Enabled", "false", "DefaultValue", null);
        await valueRepository.InsertAsync(first, TestContext.Current.CancellationToken);

        // when
        var action = async () => await valueRepository.InsertAsync(duplicate, TestContext.Current.CancellationToken);

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
        (await _IndexExistsAsync("IX_FeatureGroupDefinitions_Name"))
            .Should()
            .BeTrue();
        (await _IndexExistsAsync("IX_FeatureDefinitions_GroupName")).Should().BeTrue();
        (await _IndexExistsAsync("IX_FeatureDefinitions_Name")).Should().BeTrue();
        (await _IndexExistsAsync("IX_FeatureValues_ProviderName_ProviderKey")).Should().BeTrue();
        (await _IndexExistsAsync("IX_FeatureValues_Name_ProviderName_ProviderKey")).Should().BeTrue();
        (await _IndexExistsAsync("IX_FeatureValues_Name_ProviderName_NullProviderKey")).Should().BeTrue();
    }

    private IHost _CreateHost()
    {
        var builder = Host.CreateApplicationBuilder();
        // unify: management-core deps
        builder.Services.AddSingleton(TimeProvider.System);
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
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
