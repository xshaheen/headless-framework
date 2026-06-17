// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;
using Headless.Permissions;
using Headless.Permissions.Seeders;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

[Collection<SqlServerPermissionsFixture>]
public sealed class SqlServerPermissionsFailureModesTests(SqlServerPermissionsFixture fixture)
{
    [Fact]
    public async Task should_throw_and_keep_initializer_unmarked_when_database_unreachable()
    {
        // given — port 1 is reserved and won't accept connections; short timeout to fail fast.
        // Password is a placeholder; we never reach the auth handshake because the TCP connect fails first.
        const string unreachable =
            "Server=127.0.0.1,1;Database=missing;User Id=sa;Password=placeholder-never-used;Connect Timeout=2;TrustServerCertificate=true";
        using var host = _CreateHost(unreachable);

        // when / then — wrapped in HostFailedToStartException by the host pipeline; inner is SqlException
        await FluentActions
            .Awaiting(() => host.StartAsync(TestContext.Current.CancellationToken))
            .Should()
            .ThrowAsync<Exception>()
            .Where(e => e is SqlException || e.InnerException is SqlException);

        var initializer = host
            .Services.GetRequiredService<IEnumerable<IInitializer>>()
            .Single(x => x is not PermissionsInitializationBackgroundService);
        initializer.IsInitialized.Should().BeFalse();

        await FluentActions
            .Awaiting(() => initializer.WaitForInitializationAsync(TestContext.Current.CancellationToken))
            .Should()
            .ThrowAsync<SqlException>();
    }

    [Fact]
    public async Task should_succeed_when_multiple_hosts_initialize_concurrently_against_same_schema()
    {
        // given — 5 hosts racing to create the same schema/tables; the initializer is
        // designed to be idempotent via OBJECT_ID checks + duplicate-error suppression.
        await _DropSchemaAsync("permissions_sql_concurrent");
        const int hostCount = 5;
        var hosts = Enumerable
            .Range(0, hostCount)
            .Select(_ => _CreateHost(fixture.ConnectionString, "permissions_sql_concurrent"))
            .ToArray();

        try
        {
            // when — start all hosts in parallel
            var startTasks = hosts.Select(h => h.StartAsync(TestContext.Current.CancellationToken)).ToArray();
            await Task.WhenAll(startTasks);

            // then — all initializers report ready, exactly one of each table exists, and the
            // full 5-index complement is present (regression guard: a CATCH that swallows a real
            // CREATE INDEX failure would otherwise pass the table-count assertion silently).
            hosts
                .Select(h =>
                    h.Services.GetRequiredService<IEnumerable<IInitializer>>()
                        .Single(x => x is not PermissionsInitializationBackgroundService)
                        .IsInitialized
                )
                .Should()
                .AllSatisfy(initialized => initialized.Should().BeTrue());
            (await _CountTablesAsync("permissions_sql_concurrent", "PermissionGrants")).Should().Be(1);
            (await _CountTablesAsync("permissions_sql_concurrent", "PermissionDefinitions")).Should().Be(1);
            (await _CountTablesAsync("permissions_sql_concurrent", "PermissionGroupDefinitions")).Should().Be(1);
            (await _CountIndexesAsync("permissions_sql_concurrent")).Should().Be(5);
        }
        finally
        {
            foreach (var host in hosts)
            {
                host.Dispose();
            }
        }
    }

    private static IHost _CreateHost(string connectionString, string schema = "permissions_sql_failure")
    {
        var builder = Host.CreateApplicationBuilder();
        // unify: management-core deps
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddHeadlessPermissions(setup =>
        {
            setup.ConfigureStorage(options => options.Schema = schema);
            setup.UseSqlServer(connectionString);
        });

        return builder.Build();
    }

    private async Task _DropSchemaAsync(string schema)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new SqlCommand(
            $"""
            IF OBJECT_ID(N'{schema}.PermissionGrants', N'U') IS NOT NULL DROP TABLE [{schema}].[PermissionGrants];
            IF OBJECT_ID(N'{schema}.PermissionDefinitions', N'U') IS NOT NULL DROP TABLE [{schema}].[PermissionDefinitions];
            IF OBJECT_ID(N'{schema}.PermissionGroupDefinitions', N'U') IS NOT NULL DROP TABLE [{schema}].[PermissionGroupDefinitions];
            IF TYPE_ID(N'{schema}.HeadlessPermissionsIdList') IS NOT NULL DROP TYPE [{schema}].[HeadlessPermissionsIdList];
            IF TYPE_ID(N'{schema}.HeadlessPermissionsNameList') IS NOT NULL DROP TYPE [{schema}].[HeadlessPermissionsNameList];
            IF EXISTS (SELECT * FROM sys.schemas WHERE name = N'{schema}') EXEC(N'DROP SCHEMA [{schema}]');
            """,
            connection
        );
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task<int> _CountTablesAsync(string schema, string table)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new SqlCommand(
            """
            SELECT COUNT(*)
            FROM information_schema.tables
            WHERE table_schema = @schema AND table_name = @table
            """,
            connection
        );
        command.Parameters.AddWithValue("@schema", schema);
        command.Parameters.AddWithValue("@table", table);

        return Convert.ToInt32(
            await command.ExecuteScalarAsync(TestContext.Current.CancellationToken),
            CultureInfo.InvariantCulture
        );
    }

    private async Task<int> _CountIndexesAsync(string schema)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        // Nonclustered indexes only (type = 2) across the schema — excludes the clustered PKs so the
        // count matches the 5 CREATE [UNIQUE] INDEX statements in SqlServerPermissionsStorageInitializer.
        await using var command = new SqlCommand(
            """
            SELECT COUNT(*)
            FROM sys.indexes i
            INNER JOIN sys.objects o ON i.object_id = o.object_id
            INNER JOIN sys.schemas s ON o.schema_id = s.schema_id
            WHERE s.name = @schema AND o.type = 'U' AND i.type = 2
            """,
            connection
        );
        command.Parameters.AddWithValue("@schema", schema);

        return Convert.ToInt32(
            await command.ExecuteScalarAsync(TestContext.Current.CancellationToken),
            CultureInfo.InvariantCulture
        );
    }
}
