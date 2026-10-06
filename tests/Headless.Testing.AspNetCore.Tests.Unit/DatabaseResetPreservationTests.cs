// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Hosting.Initialization.Schema;
using Headless.Testing.AspNetCore;
using Headless.Testing.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Respawn;
using Respawn.Graph;

namespace Tests;

public sealed class DatabaseResetPreservationTests : TestBase
{
    private const string _EfHistory = "__EFMigrationsHistory";
    private const string _HostState = "feature_definitions";
    private const string _AppData = "orders";
    private const string _Custom = "reference_data";

    private HeadlessTestServer<Program>? _server;

    protected override async ValueTask DisposeAsyncCore()
    {
        if (_server is not null)
        {
            await _server.DisposeAsync();
        }

        await base.DisposeAsyncCore();
    }

    [Fact]
    public async Task should_preserve_both_history_tables_and_listed_tables_when_resetting()
    {
        // given
        await using var connection = await _CreateDatabaseAsync(_CreateSharedDatabaseConnectionString());
        var reset = await DatabaseReset.CreateAsync(
            connection,
            new DatabaseResetOptions { DbAdapter = DbAdapter.Sqlite, TablesToPreserve = [new Table(_Custom)] },
            AbortToken
        );

        // when
        await reset.ResetAsync(connection, AbortToken);

        // then
        (await _CountAsync(connection, SchemaRunner.HistoryTableName))
            .Should()
            .Be(1);
        (await _CountAsync(connection, _EfHistory)).Should().Be(1);
        (await _CountAsync(connection, _Custom)).Should().Be(1);
        (await _CountAsync(connection, _AppData)).Should().Be(0);
        (await _CountAsync(connection, _HostState)).Should().Be(0);
    }

    [Fact]
    public async Task should_preserve_host_state_tables_declared_by_schema_contributions()
    {
        // given
        var connectionString = _CreateSharedDatabaseConnectionString();
        await using var keeper = await _CreateDatabaseAsync(connectionString);
        _server = _CreateServer(connectionString, preserveHostStateTables: true);
        await _server.InitializeAsync();

        // when
        await _server.ResetDatabaseAsync(AbortToken);

        // then
        (await _CountAsync(keeper, _HostState))
            .Should()
            .Be(1);
        (await _CountAsync(keeper, SchemaRunner.HistoryTableName)).Should().Be(1);
        (await _CountAsync(keeper, _AppData)).Should().Be(0);
    }

    [Fact]
    public async Task should_reset_host_state_tables_when_preservation_is_disabled()
    {
        // given
        var connectionString = _CreateSharedDatabaseConnectionString();
        await using var keeper = await _CreateDatabaseAsync(connectionString);
        _server = _CreateServer(connectionString, preserveHostStateTables: false);
        await _server.InitializeAsync();

        // when
        await _server.ResetDatabaseAsync(AbortToken);

        // then
        (await _CountAsync(keeper, _HostState))
            .Should()
            .Be(0);
        (await _CountAsync(keeper, SchemaRunner.HistoryTableName)).Should().Be(1);
    }

    private static HeadlessTestServer<Program> _CreateServer(string connectionString, bool preserveHostStateTables)
    {
        var server = new HeadlessTestServer<Program>(configureTestServices: services =>
            services.AddSingleton(
                new SchemaContribution(
                    feature: "Features",
                    dialect: Substitute.For<ISchemaDialect>(),
                    createConnection: () => new SqliteConnection(connectionString),
                    schema: "main",
                    steps: [new SchemaStep("1", "Create the definition table.", "SELECT 1;")],
                    hostStateTables: [_HostState]
                )
            )
        );

        server.ConfigureDatabaseReset(options =>
        {
            options.ConnectionProvider = _ => new SqliteConnection(connectionString);
            options.DbAdapter = DbAdapter.Sqlite;
            options.PreserveHostStateTables = preserveHostStateTables;
        });

        return server;
    }

    private static string _CreateSharedDatabaseConnectionString()
    {
        return new SqliteConnectionStringBuilder
        {
            DataSource = $"headless-preserve-{Guid.NewGuid():N}",
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
            DefaultTimeout = 1,
        }.ToString();
    }

    private static async Task<SqliteConnection> _CreateDatabaseAsync(string connectionString)
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(AbortToken);

        foreach (var table in new[] { SchemaRunner.HistoryTableName, _EfHistory, _HostState, _AppData, _Custom })
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"CREATE TABLE \"{table}\" (Id INTEGER PRIMARY KEY); INSERT INTO \"{table}\" (Id) VALUES (1);";
            await command.ExecuteNonQueryAsync(AbortToken);
        }

        return connection;
    }

    private static async Task<long> _CountAsync(DbConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM \"{table}\";";
        return (long)(await command.ExecuteScalarAsync(AbortToken))!;
    }
}
