// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Hosting.Initialization;
using Headless.Permissions;
using Headless.Permissions.Entities;
using Headless.Permissions.Repositories;
using Headless.Testing.Tests;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

[Collection<SqlServerPermissionsFixture>]
public sealed class SqlServerPermissionsStorageTests(SqlServerPermissionsFixture fixture) : TestBase
{
    private const string _Schema = "permissions_sql_raw";

    [Fact]
    public async Task should_initialize_tables_and_round_trip_permission_grant_and_definition()
    {
        // given
        await _DropSchemaAsync();
        using var host = _CreateHost();

        // when
        await host.StartAsync(AbortToken);
        var initializer = host
            .Services.GetRequiredService<IEnumerable<IInitializer>>()
            .Single(x => x is IHostedLifecycleService);
        var grantRepository = host.Services.GetRequiredService<IPermissionGrantRepository>();
        var definitionRepository = host.Services.GetRequiredService<IPermissionDefinitionRecordRepository>();
        var record = new PermissionGrantRecord(Guid.NewGuid(), "Users.Create", "Role", "admin", isGranted: true);
        var group = new PermissionGroupDefinitionRecord(Guid.NewGuid(), "Users", "Users");
        var permission = new PermissionDefinitionRecord(Guid.NewGuid(), "Users", "Users.Create", null, "Create users");

        await grantRepository.InsertAsync(record, AbortToken);
        await definitionRepository.SaveAsync([group], [], [], [permission], [], [], AbortToken);
        var stored = await grantRepository.FindAsync("Users.Create", "Role", "admin", AbortToken);
        var storedGroups = await definitionRepository.GetGroupsListAsync(AbortToken);
        var storedPermissions = await definitionRepository.GetPermissionsListAsync(AbortToken);

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
    public async Task should_repair_missing_indexes_when_tables_already_exist()
    {
        // given
        await _DropSchemaAsync();
        await _CreateTablesWithoutIndexesAsync();
        using var host = _CreateHost();

        // when
        await host.StartAsync(AbortToken);

        // then
        (await _IndexExistsAsync("PermissionGroupDefinitions", "IX_PermissionGroupDefinitions_Name"))
            .Should()
            .BeTrue();
        (await _IndexExistsAsync("PermissionDefinitions", "IX_PermissionDefinitions_GroupName")).Should().BeTrue();
        (await _IndexExistsAsync("PermissionDefinitions", "IX_PermissionDefinitions_Name")).Should().BeTrue();
        (await _IndexExistsAsync("PermissionGrants", "IX_PermissionGrants_TenantId_Name_ProviderName_ProviderKey"))
            .Should()
            .BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(101)]
    public async Task should_batch_permission_grants_from_single_use_enumerable(int count)
    {
        // given
        await _DropSchemaAsync();
        using var host = _CreateHost();
        await host.StartAsync(AbortToken);
        var grantRepository = host.Services.GetRequiredService<IPermissionGrantRepository>();
        await _CreateInsertCommandCounterAsync();
        var records = Enumerable
            .Range(0, count)
            .Select(index => new PermissionGrantRecord(
                Guid.NewGuid(),
                $"Bulk.Permission.{index}",
                "Role",
                "bulk-admin",
                isGranted: true
            ))
            .ToArray();
        var enumerationCount = 0;

        IEnumerable<PermissionGrantRecord> enumerateOnce()
        {
            enumerationCount++;

            foreach (var record in records)
            {
                yield return record;
            }
        }

        // when
        await grantRepository.InsertManyAsync(enumerateOnce(), AbortToken);
        var stored = await grantRepository.GetListAsync("Role", "bulk-admin", AbortToken);

        // then
        enumerationCount.Should().Be(1);
        stored.Should().HaveCount(count);
        (await _InsertCommandCountAsync()).Should().Be((count + 99) / 100);
    }

    [Fact]
    public async Task should_roll_back_all_permission_grants_when_later_batch_conflicts()
    {
        // given
        await _DropSchemaAsync();
        using var host = _CreateHost();
        await host.StartAsync(AbortToken);
        var grantRepository = host.Services.GetRequiredService<IPermissionGrantRepository>();
        var records = Enumerable
            .Range(0, 100)
            .Select(index => new PermissionGrantRecord(
                Guid.NewGuid(),
                $"Atomic.Permission.{index}",
                "Role",
                "atomic-admin",
                isGranted: true
            ))
            .Append(
                new PermissionGrantRecord(
                    Guid.NewGuid(),
                    "Atomic.Permission.0",
                    "Role",
                    "atomic-admin",
                    isGranted: false
                )
            )
            .ToArray();

        // when
        var act = () => grantRepository.InsertManyAsync(records, AbortToken);

        // then
        await act.Should().ThrowAsync<SqlException>().Where(x => x.Number == 2601 || x.Number == 2627);
        (await grantRepository.GetListAsync("Role", "atomic-admin", AbortToken)).Should().BeEmpty();
    }

    [Fact]
    public async Task should_not_enumerate_permission_grants_when_insertion_is_already_cancelled()
    {
        // given
        using var host = _CreateHost();
        var grantRepository = host.Services.GetRequiredService<IPermissionGrantRepository>();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var enumerated = false;

        IEnumerable<PermissionGrantRecord> records()
        {
            enumerated = true;
            yield return new PermissionGrantRecord(Guid.NewGuid(), "Cancelled", "Role", "admin", isGranted: true);
        }

        // when
        var act = () => grantRepository.InsertManyAsync(records(), cancellation.Token);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
        enumerated.Should().BeFalse();
    }

    [Fact]
    public async Task should_scope_permission_grant_reads_to_current_tenant()
    {
        // given
        await _DropSchemaAsync();
        var currentTenant = new TestCurrentTenant("tenant-a");
        using var host = _CreateHost(currentTenant);
        await host.StartAsync(AbortToken);
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
        await grantRepository.InsertManyAsync([tenantA, tenantB], AbortToken);
        var found = await grantRepository.FindAsync("Users.Approve", "Role", "admin", AbortToken);
        var list = await grantRepository.GetListAsync("Role", "admin", AbortToken);

        // then
        found.Should().NotBeNull();
        found!.TenantId.Should().Be("tenant-a");
        found.IsGranted.Should().BeTrue();
        list.Should().ContainSingle(x => x.TenantId == "tenant-a");
    }

    [Fact]
    public async Task should_chunk_sql_server_permission_grant_queries_and_deletes()
    {
        // given
        await _DropSchemaAsync();
        using var host = _CreateHost();
        await host.StartAsync(AbortToken);
        var grantRepository = host.Services.GetRequiredService<IPermissionGrantRepository>();
        var records = Enumerable
            .Range(0, 2101)
            .Select(index => new PermissionGrantRecord(
                Guid.NewGuid(),
                $"Bulk.Permission.{index}",
                "Role",
                "bulk-admin",
                isGranted: true
            ))
            .ToArray();

        // when
        await grantRepository.InsertManyAsync(records, AbortToken);
        var found = await grantRepository.GetListAsync(
            records.Select(x => x.Name).ToArray(),
            "Role",
            "bulk-admin",
            AbortToken
        );
        await grantRepository.DeleteManyAsync(records, AbortToken);
        var remaining = await grantRepository.GetListAsync("Role", "bulk-admin", AbortToken);

        // then
        found.Should().HaveCount(records.Length);
        remaining.Should().BeEmpty();
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
            IF OBJECT_ID(N'{_Schema}.PermissionGrants', N'U') IS NOT NULL DROP TABLE [{_Schema}].[PermissionGrants];
            IF OBJECT_ID(N'{_Schema}.PermissionDefinitions', N'U') IS NOT NULL DROP TABLE [{_Schema}].[PermissionDefinitions];
            IF OBJECT_ID(N'{_Schema}.PermissionGroupDefinitions', N'U') IS NOT NULL DROP TABLE [{_Schema}].[PermissionGroupDefinitions];
            IF OBJECT_ID(N'{_Schema}.PermissionGrantInsertCommandCounter', N'U') IS NOT NULL DROP TABLE [{_Schema}].[PermissionGrantInsertCommandCounter];
            IF TYPE_ID(N'{_Schema}.HeadlessPermissionsIdList') IS NOT NULL DROP TYPE [{_Schema}].[HeadlessPermissionsIdList];
            IF TYPE_ID(N'{_Schema}.HeadlessPermissionsNameList') IS NOT NULL DROP TYPE [{_Schema}].[HeadlessPermissionsNameList];
            IF EXISTS (SELECT * FROM sys.schemas WHERE name = N'{_Schema}') EXEC(N'DROP SCHEMA [{_Schema}]');
            """,
            connection
        );
        await command.ExecuteNonQueryAsync(AbortToken);
    }

    private async Task _CreateTablesWithoutIndexesAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new SqlCommand(
            $"""
            IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = N'{_Schema}') EXEC(N'CREATE SCHEMA [{_Schema}]');

            CREATE TABLE [{_Schema}].[PermissionGroupDefinitions] (
                [Id] uniqueidentifier NOT NULL,
                [Name] nvarchar(128) NOT NULL,
                [DisplayName] nvarchar(256) NOT NULL,
                [ExtraProperties] nvarchar(max) NOT NULL,
                CONSTRAINT [PK_PermissionGroupDefinitions] PRIMARY KEY CLUSTERED ([Id] ASC)
            );

            CREATE TABLE [{_Schema}].[PermissionDefinitions] (
                [Id] uniqueidentifier NOT NULL,
                [GroupName] nvarchar(128) NOT NULL,
                [Name] nvarchar(128) NOT NULL,
                [DisplayName] nvarchar(256) NOT NULL,
                [IsEnabled] bit NOT NULL,
                [ParentName] nvarchar(128) NULL,
                [Providers] nvarchar(128) NULL,
                [ExtraProperties] nvarchar(max) NOT NULL,
                CONSTRAINT [PK_PermissionDefinitions] PRIMARY KEY CLUSTERED ([Id] ASC)
            );

            CREATE TABLE [{_Schema}].[PermissionGrants] (
                [Id] uniqueidentifier NOT NULL,
                [Name] nvarchar(128) NOT NULL,
                [ProviderName] nvarchar(64) NOT NULL,
                [ProviderKey] nvarchar(64) NOT NULL,
                [TenantId] nvarchar(41) NULL,
                [IsGranted] bit NOT NULL DEFAULT CAST(1 AS bit),
                CONSTRAINT [PK_PermissionGrants] PRIMARY KEY CLUSTERED ([Id] ASC)
            );
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

    private async Task _CreateInsertCommandCounterAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new SqlCommand(
            $"""
            CREATE TABLE [{_Schema}].[PermissionGrantInsertCommandCounter] ([Count] int NOT NULL);
            INSERT INTO [{_Schema}].[PermissionGrantInsertCommandCounter] ([Count]) VALUES (0);
            EXEC(N'
                CREATE TRIGGER [{_Schema}].[TR_PermissionGrant_InsertCommandCounter]
                ON [{_Schema}].[PermissionGrants]
                AFTER INSERT
                AS
                BEGIN
                    SET NOCOUNT ON;
                    UPDATE [{_Schema}].[PermissionGrantInsertCommandCounter] SET [Count] = [Count] + 1;
                END
            ');
            """,
            connection
        );
        await command.ExecuteNonQueryAsync(AbortToken);
    }

    private async Task<int> _InsertCommandCountAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new SqlCommand(
            $"SELECT [Count] FROM [{_Schema}].[PermissionGrantInsertCommandCounter];",
            connection
        );

        return (int)(await command.ExecuteScalarAsync(AbortToken))!;
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
                WHERE name = @index AND object_id = OBJECT_ID(@object)
            ) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END
            """,
            connection
        );
        command.Parameters.AddWithValue("@index", indexName);
        command.Parameters.AddWithValue("@object", $"{_Schema}.{tableName}");

        return (bool)await command.ExecuteScalarAsync(AbortToken);
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
        public void Dispose()
        {
            action();
        }
    }
}
