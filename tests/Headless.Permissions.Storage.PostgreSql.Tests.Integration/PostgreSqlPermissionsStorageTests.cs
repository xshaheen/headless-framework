// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Caching;
using Headless.Hosting;
using Headless.MultiTenancy;
using Headless.Permissions;
using Headless.Primitives;
using Headless.Serializer;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Tests;

[Collection<PostgreSqlPermissionsFixture>]
public sealed class PostgreSqlPermissionsStorageTests(PostgreSqlPermissionsFixture fixture) : TestBase
{
    private const string _Schema = "permissions_pg_raw";

    [Fact]
    public async Task should_round_trip_definition_extra_properties_with_their_keys_and_values_as_written()
    {
        // given — keys that a camelCasing dictionary policy would rename, and values the store reads back as CLR
        // primitives
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        using var host = fixture.CreateHost(_Schema);
        await host.StartAsync(AbortToken);
        var repository = host.Services.GetRequiredService<IPermissionDefinitionRecordRepository>();
        var group = new PermissionGroupDefinitionRecord(Guid.NewGuid(), "Users", "Users");
        group.SetProperty("Order", 5L).SetProperty("USD", "Default");
        var permission = new PermissionDefinitionRecord(Guid.NewGuid(), "Users", "Users.Create", null, "Create users");
        permission.SetProperty("Group", "Users").SetProperty("Enabled", true);

        // when
        await repository.SaveAsync([group], [], [], [permission], [], [], AbortToken);
        var storedGroup = (await repository.GetGroupsListAsync(AbortToken)).Single();
        var storedPermission = (await repository.GetPermissionsListAsync(AbortToken)).Single();

        // then — the definitions read back equal, so a re-seed finds nothing to update
        storedGroup.ExtraProperties.Should().BeEquivalentTo(group.ExtraProperties);
        storedPermission.ExtraProperties.Should().BeEquivalentTo(permission.ExtraProperties);
        storedGroup.HasSameData(group).Should().BeTrue();
        storedPermission.HasSameData(permission).Should().BeTrue();
    }

    [Fact]
    public void should_leave_the_shared_json_serializer_to_the_host()
    {
        // given — the shared serializer carries the host's JSON options for caches and blob metadata; a storage that
        // registered its own first would decide those options by registration order
        using var host = fixture.CreateHost(_Schema);

        // when
        var serializer = host.Services.GetService<IJsonSerializer>();

        // then
        serializer.Should().BeNull();
    }

    [Fact]
    public async Task should_enforce_unique_host_permission_grants()
    {
        // given
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        using var host = fixture.CreateHost(_Schema);
        await host.StartAsync(AbortToken);
        var grantRepository = host.Services.GetRequiredService<IPermissionGrantRepository>();
        var first = new PermissionGrantRecord(Guid.NewGuid(), "Users.Delete", "Role", "admin", isGranted: true);
        var duplicate = new PermissionGrantRecord(Guid.NewGuid(), "Users.Delete", "Role", "admin", isGranted: false);

        // when
        await grantRepository.InsertAsync(first, AbortToken);
        var act = () => grantRepository.InsertAsync(duplicate, AbortToken);

        // then
        await act.Should().ThrowAsync<PostgresException>().Where(x => x.SqlState == PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task should_repair_missing_indexes_when_tables_already_exist()
    {
        // given
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        await _CreateTablesWithoutIndexesAsync();
        using var host = fixture.CreateHost(_Schema);

        // when
        await host.StartAsync(AbortToken);

        // then
        (await _IndexExistsAsync("ix_permission_group_definitions_name"))
            .Should()
            .BeTrue();
        (await _IndexExistsAsync("ix_permission_definitions_group_name")).Should().BeTrue();
        (await _IndexExistsAsync("ix_permission_definitions_name")).Should().BeTrue();
        (await _IndexExistsAsync("ix_permission_grants_tenant_id_name_provider_name_provider_key")).Should().BeTrue();
        (await _IndexExistsAsync("ix_permission_grants_name_provider_name_provider_key_no_tenant")).Should().BeTrue();
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

            CREATE TABLE IF NOT EXISTS "{_Schema}".permission_group_definitions (
                id uuid NOT NULL,
                name character varying(128) NOT NULL,
                display_name character varying(256) NOT NULL,
                extra_properties text NOT NULL,
                CONSTRAINT pk_permission_group_definitions PRIMARY KEY (id)
            );

            CREATE TABLE IF NOT EXISTS "{_Schema}".permission_definitions (
                id uuid NOT NULL,
                group_name character varying(128) NOT NULL,
                name character varying(128) NOT NULL,
                display_name character varying(256) NOT NULL,
                is_enabled boolean NOT NULL,
                parent_name character varying(128),
                providers character varying(128),
                extra_properties text NOT NULL,
                CONSTRAINT pk_permission_definitions PRIMARY KEY (id)
            );

            CREATE TABLE IF NOT EXISTS "{_Schema}".permission_grants (
                id uuid NOT NULL,
                name character varying(128) NOT NULL,
                provider_name character varying(64) NOT NULL,
                provider_key character varying(64) NOT NULL,
                tenant_id character varying(41),
                is_granted boolean NOT NULL DEFAULT TRUE,
                created_at timestamp with time zone NOT NULL,
                updated_at timestamp with time zone,
                CONSTRAINT pk_permission_grants PRIMARY KEY (id)
            );
            """,
            connection
        );
        await command.ExecuteNonQueryAsync(AbortToken);
    }
}
