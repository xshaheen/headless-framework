// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Caching;
using Headless.Hosting;
using Headless.MultiTenancy;
using Headless.Permissions;
using Headless.Testing.Tests;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests;

[Collection<SqlServerPermissionsFixture>]
public sealed class SqlServerPermissionsStorageTests(SqlServerPermissionsFixture fixture) : TestBase
{
    private const string _Schema = "permissions_sql_raw";

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
        (await _IndexExistsAsync("PermissionGroupDefinitions", "IX_PermissionGroupDefinitions_Name"))
            .Should()
            .BeTrue();
        (await _IndexExistsAsync("PermissionDefinitions", "IX_PermissionDefinitions_GroupName")).Should().BeTrue();
        (await _IndexExistsAsync("PermissionDefinitions", "IX_PermissionDefinitions_Name")).Should().BeTrue();
        (await _IndexExistsAsync("PermissionGrants", "IX_PermissionGrants_TenantId_Name_ProviderName_ProviderKey"))
            .Should()
            .BeTrue();
        (await _IndexExistsAsync("PermissionGrants", "IX_PermissionGrants_Name_ProviderName_ProviderKey_NoTenant"))
            .Should()
            .BeTrue();
    }

    [Fact]
    public async Task should_chunk_sql_server_permission_grant_queries_and_deletes()
    {
        // given
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        using var host = fixture.CreateHost(_Schema);
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
}
