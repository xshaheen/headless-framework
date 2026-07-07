// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Hosting.Initialization;
using Headless.Permissions;
using Headless.Permissions.Entities;
using Headless.Permissions.PostgreSql;
using Headless.Permissions.Repositories;
using Headless.Permissions.Seeders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Tests;

[Collection<PostgreSqlPermissionsFixture>]
public sealed class PostgreSqlPermissionsStorageTests(PostgreSqlPermissionsFixture fixture)
{
    private const string _Schema = "permissions_pg_raw";

    [Fact]
    public async Task should_initialize_tables_and_round_trip_permission_grant_and_definition()
    {
        // given
        await _DropSchemaAsync();
        using var host = _CreateHost();

        // when
        await host.StartAsync(TestContext.Current.CancellationToken);
        var initializer = host
            .Services.GetRequiredService<IEnumerable<IInitializer>>()
            .Single(x => x is not PermissionsInitializationBackgroundService);
        var grantRepository = host.Services.GetRequiredService<IPermissionGrantRepository>();
        var definitionRepository = host.Services.GetRequiredService<IPermissionDefinitionRecordRepository>();
        var record = new PermissionGrantRecord(Guid.NewGuid(), "Users.Create", "Role", "admin", isGranted: true);
        var group = new PermissionGroupDefinitionRecord(Guid.NewGuid(), "Users", "Users");
        var permission = new PermissionDefinitionRecord(Guid.NewGuid(), "Users", "Users.Create", null, "Create users");

        await grantRepository.InsertAsync(record, TestContext.Current.CancellationToken);
        await definitionRepository.SaveAsync(
            [group],
            [],
            [],
            [permission],
            [],
            [],
            TestContext.Current.CancellationToken
        );
        var stored = await grantRepository.FindAsync(
            "Users.Create",
            "Role",
            "admin",
            TestContext.Current.CancellationToken
        );
        var storedGroups = await definitionRepository.GetGroupsListAsync(TestContext.Current.CancellationToken);
        var storedPermissions = await definitionRepository.GetPermissionsListAsync(
            TestContext.Current.CancellationToken
        );

        // then
        initializer.IsInitialized.Should().BeTrue();
        (await _TableExistsAsync("PermissionGrants")).Should().BeTrue();
        (await _TableExistsAsync("PermissionDefinitions")).Should().BeTrue();
        (await _TableExistsAsync("PermissionGroupDefinitions")).Should().BeTrue();
        stored.Should().NotBeNull();
        stored!.IsGranted.Should().BeTrue();
        storedGroups.Should().ContainSingle(x => x.Name == "Users");
        storedPermissions.Should().ContainSingle(x => x.Name == "Users.Create");
    }

    [Fact]
    public async Task should_enforce_unique_host_permission_grants()
    {
        // given
        await _DropSchemaAsync();
        using var host = _CreateHost();
        await host.StartAsync(TestContext.Current.CancellationToken);
        var grantRepository = host.Services.GetRequiredService<IPermissionGrantRepository>();
        var first = new PermissionGrantRecord(Guid.NewGuid(), "Users.Delete", "Role", "admin", isGranted: true);
        var duplicate = new PermissionGrantRecord(Guid.NewGuid(), "Users.Delete", "Role", "admin", isGranted: false);

        // when
        await grantRepository.InsertAsync(first, TestContext.Current.CancellationToken);
        var act = () => grantRepository.InsertAsync(duplicate, TestContext.Current.CancellationToken);

        // then
        await act.Should().ThrowAsync<PostgresException>().Where(x => x.SqlState == PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task should_scope_permission_grant_reads_to_current_tenant()
    {
        // given
        await _DropSchemaAsync();
        var currentTenant = new TestCurrentTenant("tenant-a");
        using var host = _CreateHost(currentTenant);
        await host.StartAsync(TestContext.Current.CancellationToken);
        var grantRepository = host.Services.GetRequiredService<IPermissionGrantRepository>();
        var tenantA = new PermissionGrantRecord(
            Guid.NewGuid(),
            "Users.Approve",
            "Role",
            "admin",
            isGranted: true,
            tenantId: "tenant-a"
        );
        var tenantB = new PermissionGrantRecord(
            Guid.NewGuid(),
            "Users.Approve",
            "Role",
            "admin",
            isGranted: false,
            tenantId: "tenant-b"
        );

        // when
        await grantRepository.InsertManyAsync([tenantA, tenantB], TestContext.Current.CancellationToken);
        var found = await grantRepository.FindAsync(
            "Users.Approve",
            "Role",
            "admin",
            TestContext.Current.CancellationToken
        );
        var list = await grantRepository.GetListAsync("Role", "admin", TestContext.Current.CancellationToken);

        // then
        found.Should().NotBeNull();
        found!.TenantId.Should().Be("tenant-a");
        found.IsGranted.Should().BeTrue();
        list.Should().ContainSingle(x => x.TenantId == "tenant-a");
    }

    [Fact]
    public async Task should_repair_missing_indexes_when_tables_already_exist()
    {
        // given
        await _DropSchemaAsync();
        await _CreateTablesWithoutIndexesAsync();
        using var host = _CreateHost();

        // when
        await host.StartAsync(TestContext.Current.CancellationToken);

        // then
        (await _IndexExistsAsync("IX_PermissionGroupDefinitions_Name"))
            .Should()
            .BeTrue();
        (await _IndexExistsAsync("IX_PermissionDefinitions_GroupName")).Should().BeTrue();
        (await _IndexExistsAsync("IX_PermissionDefinitions_Name")).Should().BeTrue();
        (await _IndexExistsAsync("IX_PermissionGrants_TenantId_Name_ProviderName_ProviderKey")).Should().BeTrue();
        (await _IndexExistsAsync("IX_PermissionGrants_Name_ProviderName_ProviderKey_NullTenantId")).Should().BeTrue();
    }

    private IHost _CreateHost(ICurrentTenant? currentTenant = null)
    {
        var builder = Host.CreateApplicationBuilder();
        if (currentTenant is not null)
        {
            builder.Services.AddSingleton(currentTenant);
        }

        // unify: management-core deps
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddHeadlessPermissions(setup =>
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

            CREATE TABLE IF NOT EXISTS "{_Schema}"."PermissionGroupDefinitions" (
                "Id" uuid NOT NULL,
                "Name" character varying(128) NOT NULL,
                "DisplayName" character varying(256) NOT NULL,
                "ExtraProperties" text NOT NULL,
                CONSTRAINT "PK_PermissionGroupDefinitions" PRIMARY KEY ("Id")
            );

            CREATE TABLE IF NOT EXISTS "{_Schema}"."PermissionDefinitions" (
                "Id" uuid NOT NULL,
                "GroupName" character varying(128) NOT NULL,
                "Name" character varying(128) NOT NULL,
                "DisplayName" character varying(256) NOT NULL,
                "IsEnabled" boolean NOT NULL,
                "ParentName" character varying(128),
                "Providers" character varying(128),
                "ExtraProperties" text NOT NULL,
                CONSTRAINT "PK_PermissionDefinitions" PRIMARY KEY ("Id")
            );

            CREATE TABLE IF NOT EXISTS "{_Schema}"."PermissionGrants" (
                "Id" uuid NOT NULL,
                "Name" character varying(128) NOT NULL,
                "ProviderName" character varying(64) NOT NULL,
                "ProviderKey" character varying(64) NOT NULL,
                "TenantId" character varying(41),
                "IsGranted" boolean NOT NULL DEFAULT TRUE,
                CONSTRAINT "PK_PermissionGrants" PRIMARY KEY ("Id")
            );
            """,
            connection
        );
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private sealed class TestCurrentTenant(string? tenantId) : ICurrentTenant
    {
        public bool IsAvailable => Id is not null;

        public string? Id { get; private set; } = tenantId;

        public string? Name => null;

        public IDisposable Change(string? id, string? name = null)
        {
            var previousId = Id;
            Id = id;

            return new RestoreAction(() => Id = previousId);
        }
    }

    private sealed class RestoreAction(Action action) : IDisposable
    {
        public void Dispose() => action();
    }
}
