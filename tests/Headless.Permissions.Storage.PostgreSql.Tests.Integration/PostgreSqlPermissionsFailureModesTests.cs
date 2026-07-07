// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;
using Headless.Permissions;
using Headless.Permissions.PostgreSql;
using Headless.Permissions.Seeders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Tests;

[Collection<PostgreSqlPermissionsFixture>]
public sealed class PostgreSqlPermissionsFailureModesTests(PostgreSqlPermissionsFixture fixture)
{
    [Fact]
    public async Task should_throw_and_keep_initializer_unmarked_when_database_unreachable()
    {
        // given — port 1 is reserved and won't accept connections; short timeout to fail fast.
        // Credentials are placeholders; we never reach the auth handshake because the TCP connect fails first.
        const string unreachable =
            "Host=127.0.0.1;Port=1;Database=missing;Username=postgres;Password=placeholder-never-used;Timeout=2";
        using var host = _CreateHost(unreachable);

        // when / then — wrapped in HostFailedToStartException by the host pipeline; inner is NpgsqlException
        await FluentActions
            .Awaiting(() => host.StartAsync(TestContext.Current.CancellationToken))
            .Should()
            .ThrowAsync<Exception>()
            .Where(e => e is NpgsqlException || e.InnerException is NpgsqlException);

        var initializer = host
            .Services.GetRequiredService<IEnumerable<IInitializer>>()
            .Single(x => x is not PermissionsInitializationBackgroundService);
        initializer.IsInitialized.Should().BeFalse();

        await FluentActions
            .Awaiting(() => initializer.WaitForInitializationAsync(TestContext.Current.CancellationToken))
            .Should()
            .ThrowAsync<NpgsqlException>();
    }

    [Fact]
    public async Task should_succeed_when_multiple_hosts_initialize_concurrently_against_same_schema()
    {
        // given — 5 hosts racing to create the same schema/tables; the initializer is
        // designed to be idempotent via CREATE IF NOT EXISTS + duplicate-error suppression.
        await _DropSchemaAsync("permissions_pg_concurrent");
        const int hostCount = 5;
        var hosts = Enumerable
            .Range(0, hostCount)
            .Select(_ => _CreateHost(fixture.ConnectionString, "permissions_pg_concurrent"))
            .ToArray();

        try
        {
            // when — start all hosts in parallel
            var startTasks = hosts.Select(h => h.StartAsync(TestContext.Current.CancellationToken)).ToArray();
            await Task.WhenAll(startTasks);

            // then — all initializers report ready, exactly one of each table exists, and the
            // full 5-index complement is present (regression guard: a swallowed CREATE INDEX
            // failure would otherwise pass the table-count assertion silently).
            hosts
                .Select(h =>
                    h.Services.GetRequiredService<IEnumerable<IInitializer>>()
                        .Single(x => x is not PermissionsInitializationBackgroundService)
                        .IsInitialized
                )
                .Should()
                .AllSatisfy(initialized => initialized.Should().BeTrue());
            (await _CountTablesAsync("permissions_pg_concurrent", "PermissionGrants")).Should().Be(1);
            (await _CountTablesAsync("permissions_pg_concurrent", "PermissionDefinitions")).Should().Be(1);
            (await _CountTablesAsync("permissions_pg_concurrent", "PermissionGroupDefinitions")).Should().Be(1);
            (await _CountIndexesAsync("permissions_pg_concurrent")).Should().Be(5);
        }
        finally
        {
            foreach (var host in hosts)
            {
                host.Dispose();
            }
        }
    }

    private static IHost _CreateHost(string connectionString, string schema = "permissions_pg_failure")
    {
        var builder = Host.CreateApplicationBuilder();
        // unify: management-core deps
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddHeadlessPermissions(setup =>
        {
            setup.ConfigureStorage(options => options.Schema = schema);
            setup.UsePostgreSql(connectionString);
        });

        return builder.Build();
    }

    private async Task _DropSchemaAsync(string schema)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand($"""DROP SCHEMA IF EXISTS "{schema}" CASCADE;""", connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task<int> _CountTablesAsync(string schema, string table)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT COUNT(*)
            FROM information_schema.tables
            WHERE table_schema = @schema AND table_name = @table
            """,
            connection
        );
        command.Parameters.AddWithValue(nameof(schema), schema);
        command.Parameters.AddWithValue(nameof(table), table);

        return Convert.ToInt32(
            await command.ExecuteScalarAsync(TestContext.Current.CancellationToken),
            CultureInfo.InvariantCulture
        );
    }

    private async Task<int> _CountIndexesAsync(string schema)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        // Matches the 5 `CREATE [UNIQUE] INDEX IF NOT EXISTS IX_*` statements in the PG initializer;
        // the LIKE filter excludes the PK indexes (named `PK_<table>`).
        await using var command = new NpgsqlCommand(
            """
            SELECT COUNT(*)
            FROM pg_indexes
            WHERE schemaname = @schema AND indexname LIKE 'IX_%'
            """,
            connection
        );
        command.Parameters.AddWithValue(nameof(schema), schema);

        return Convert.ToInt32(
            await command.ExecuteScalarAsync(TestContext.Current.CancellationToken),
            CultureInfo.InvariantCulture
        );
    }
}
