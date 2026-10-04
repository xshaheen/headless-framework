// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Tests;

[Collection<PostgreSqlFeaturesFixture>]
public sealed class PostgreSqlFeaturesStorageTests(PostgreSqlFeaturesFixture fixture) : TestBase
{
    private const string _Schema = "features_pg_raw";

    [Fact]
    public async Task should_persist_all_definitions_across_multiple_chunks_when_batch_exceeds_chunk_size()
    {
        // given — chunk size is 500 rows; 550 forces two chunks
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        using var host = fixture.CreateHost(_Schema);
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
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        using var host = fixture.CreateHost(_Schema);
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
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        await _CreateTablesWithoutIndexesAsync();
        using var host = fixture.CreateHost(_Schema);

        // when
        await host.StartAsync(AbortToken);

        // then
        (await _IndexExistsAsync("ix_feature_group_definitions_name"))
            .Should()
            .BeTrue();
        (await _IndexExistsAsync("ix_feature_definitions_group_name")).Should().BeTrue();
        (await _IndexExistsAsync("ix_feature_definitions_name")).Should().BeTrue();
        (await _IndexExistsAsync("ix_feature_values_provider_name_provider_key")).Should().BeTrue();
        (await _IndexExistsAsync("ix_feature_values_name_provider_name_provider_key")).Should().BeTrue();
        (await _IndexExistsAsync("ix_feature_values_name_provider_name_null_provider_key")).Should().BeTrue();
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

            CREATE TABLE IF NOT EXISTS "{_Schema}".feature_group_definitions (
                id uuid NOT NULL,
                name character varying(128) NOT NULL,
                display_name character varying(256) NOT NULL,
                extra_properties text NOT NULL,
                CONSTRAINT pk_feature_group_definitions PRIMARY KEY (id)
            );

            CREATE TABLE IF NOT EXISTS "{_Schema}".feature_definitions (
                id uuid NOT NULL,
                group_name character varying(128) NOT NULL,
                name character varying(128) NOT NULL,
                display_name character varying(256) NOT NULL,
                parent_name character varying(128),
                description character varying(256),
                default_value character varying(256),
                is_visible_to_clients boolean NOT NULL,
                is_available_to_host boolean NOT NULL,
                providers character varying(256),
                extra_properties text NOT NULL,
                CONSTRAINT pk_feature_definitions PRIMARY KEY (id)
            );

            CREATE TABLE IF NOT EXISTS "{_Schema}".feature_values (
                id uuid NOT NULL,
                name character varying(128) NOT NULL,
                value character varying(128) NOT NULL,
                provider_name character varying(64) NOT NULL,
                provider_key character varying(64),
                created_at timestamp with time zone NOT NULL,
                updated_at timestamp with time zone,
                CONSTRAINT pk_feature_values PRIMARY KEY (id)
            );
            """,
            connection
        );
        await command.ExecuteNonQueryAsync(AbortToken);
    }
}
