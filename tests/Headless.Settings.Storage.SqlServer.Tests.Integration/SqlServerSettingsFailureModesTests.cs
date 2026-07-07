// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Hosting.Initialization;
using Headless.Settings;
using Headless.Settings.Seeders;
using Headless.Settings.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

[Collection<SqlServerSettingsFixture>]
public sealed class SqlServerSettingsFailureModesTests(SqlServerSettingsFixture fixture)
{
    [Fact]
    public async Task should_throw_and_keep_initializer_unmarked_when_database_unreachable()
    {
        // given — port 1 is reserved and won't accept connections; short timeout to fail fast.
        // Password is a placeholder; we never reach the auth handshake because the TCP connect fails first.
        const string unreachable =
            "Server=127.0.0.1,1;Database=missing;User Id=sa;Password=placeholder-never-used;Connect Timeout=2;TrustServerCertificate=true";
        using var host = _CreateHost(unreachable);

        // when & then — wrapped in HostFailedToStartException by the host pipeline; inner is SqlException
        await FluentActions
            .Awaiting(() => host.StartAsync(TestContext.Current.CancellationToken))
            .Should()
            .ThrowAsync<Exception>()
            .Where(e => e is SqlException || e.InnerException is SqlException);

        var initializer = host
            .Services.GetRequiredService<IEnumerable<IInitializer>>()
            .Single(x => x is not SettingsInitializationBackgroundService);
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
        await _DropSchemaAsync("settings_sql_concurrent");
        const int hostCount = 5;
        var hosts = Enumerable
            .Range(0, hostCount)
            .Select(_ => _CreateHost(fixture.ConnectionString, "settings_sql_concurrent"))
            .ToArray();

        try
        {
            // when — start all hosts in parallel
            var startTasks = hosts.Select(h => h.StartAsync(TestContext.Current.CancellationToken)).ToArray();
            await Task.WhenAll(startTasks);

            // then — all initializers report ready, exactly one of each table exists, and the
            // full 3-index complement is present (regression guard: a CATCH that swallows a real
            // CREATE INDEX failure would otherwise pass the table-count assertion silently).
            hosts
                .Select(h =>
                    h.Services.GetRequiredService<IEnumerable<IInitializer>>()
                        .Single(x => x is not SettingsInitializationBackgroundService)
                        .IsInitialized
                )
                .Should()
                .AllSatisfy(initialized => initialized.Should().BeTrue());
            (await _CountTablesAsync("settings_sql_concurrent", "SettingValues")).Should().Be(1);
            (await _CountTablesAsync("settings_sql_concurrent", "SettingDefinitions")).Should().Be(1);
            (await _CountIndexesAsync("settings_sql_concurrent")).Should().Be(3);
        }
        finally
        {
            foreach (var host in hosts)
            {
                host.Dispose();
            }
        }
    }

    private static IHost _CreateHost(string connectionString, string schema = "settings_sql_failure")
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
            IF OBJECT_ID(N'{schema}.SettingValues', N'U') IS NOT NULL DROP TABLE [{schema}].[SettingValues];
            IF OBJECT_ID(N'{schema}.SettingDefinitions', N'U') IS NOT NULL DROP TABLE [{schema}].[SettingDefinitions];
            IF TYPE_ID(N'{schema}.HeadlessSettingsIdList') IS NOT NULL DROP TYPE [{schema}].[HeadlessSettingsIdList];
            IF TYPE_ID(N'{schema}.HeadlessSettingsNameList') IS NOT NULL DROP TYPE [{schema}].[HeadlessSettingsNameList];
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
        // count matches the 3 CREATE UNIQUE INDEX statements in SqlServerSettingsStorageInitializer.
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
