// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;
using Headless.Permissions;
using Headless.Permissions.Entities;
using Headless.Permissions.Repositories;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

[Collection<SqlServerPermissionsFixture>]
public sealed class SqlServerPermissionsStorageTests(SqlServerPermissionsFixture fixture)
{
    private const string _Schema = "permissions_sql_raw";

    [Fact]
    public async Task should_initialize_tables_and_round_trip_permission_grant_and_definition()
    {
        // given
        await _DropSchemaAsync();
        using var host = _CreateHost();

        // when
        await host.StartAsync(TestContext.Current.CancellationToken);
        var initializer = host.Services.GetRequiredService<IEnumerable<IInitializer>>().Single();
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
        var stored = await grantRepository.FindAsync("Users.Create", "Role", "admin", TestContext.Current.CancellationToken);
        var storedGroups = await definitionRepository.GetGroupsListAsync(TestContext.Current.CancellationToken);
        var storedPermissions = await definitionRepository.GetPermissionsListAsync(TestContext.Current.CancellationToken);

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

    private IHost _CreateHost()
    {
        var builder = Host.CreateApplicationBuilder();
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
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new SqlCommand(
            $"""
            IF OBJECT_ID(N'{_Schema}.PermissionGrants', N'U') IS NOT NULL DROP TABLE [{_Schema}].[PermissionGrants];
            IF OBJECT_ID(N'{_Schema}.PermissionDefinitions', N'U') IS NOT NULL DROP TABLE [{_Schema}].[PermissionDefinitions];
            IF OBJECT_ID(N'{_Schema}.PermissionGroupDefinitions', N'U') IS NOT NULL DROP TABLE [{_Schema}].[PermissionGroupDefinitions];
            IF EXISTS (SELECT * FROM sys.schemas WHERE name = N'{_Schema}') EXEC(N'DROP SCHEMA [{_Schema}]');
            """,
            connection
        );
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task<bool> _TableExistsAsync(string tableName)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
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

        return (bool)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }
}
