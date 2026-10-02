// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Hosting.Initialization;
using Headless.Sql;
using Headless.Sql.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class SqliteMembershipFixture
    : ICollectionFixture<SqliteMembershipFixture>,
        ICoordinationOracleFixture,
        IAsyncLifetime
{
    private static readonly SqliteDialect _Dialect = SqliteDialect.Instance;
    private readonly SqliteTestDatabase _database = SqliteTestDatabase.Create();

    public string ConnectionString => _database.ConnectionString;

    public ValueTask InitializeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return _database.DisposeAsync();
    }

    public void ConfigureProvider(IServiceCollection services, HeadlessCoordinationSetupBuilder setup)
    {
        setup.UseSqlite(options => options.ConnectionString = ConnectionString);
    }

    public async Task ShiftAsync(string clusterName, string nodeId, TimeSpan delta, CancellationToken cancellationToken)
    {
        static string table(string name) => _Dialect.Qualify(HeadlessStorageDefaults.Schema, name);
        static string shifted(string column) => _Dialect.ShiftByDuration(column, "delta");

        await using var connection = await _OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {table("coordination_node_generation")} SET updated_at = {shifted("updated_at")}
            WHERE cluster_name = @cluster AND node_id = @node;
            UPDATE {table("coordination_descriptor")} SET created_at = {shifted("created_at")}
            WHERE cluster_name = @cluster AND node_id = @node;
            UPDATE {table("coordination_liveness")} SET last_beat = {shifted("last_beat")}, left_at = {shifted(
                "left_at"
            )}
            WHERE cluster_name = @cluster AND node_id = @node;
            """;
        _Dialect.AddParameter(command, "cluster", SqlColumnType.KeyText(0), clusterName);
        _Dialect.AddParameter(command, "node", SqlColumnType.KeyText(0), nodeId);
        _Dialect.AddDuration(command, "delta", delta);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<StoredMembership> ReadRowsAsync(string clusterName, CancellationToken cancellationToken)
    {
        static string table(string name) => _Dialect.Qualify(HeadlessStorageDefaults.Schema, name);

        await using var connection = await _OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT node_id, current_incarnation FROM {table(
                "coordination_node_generation"
            )} WHERE cluster_name = @cluster;
            SELECT node_id, incarnation, left_at IS NOT NULL FROM {table(
                "coordination_liveness"
            )} WHERE cluster_name = @cluster;
            SELECT node_id, incarnation FROM {table("coordination_descriptor")} WHERE cluster_name = @cluster;
            """;
        _Dialect.AddParameter(command, "cluster", SqlColumnType.KeyText(0), clusterName);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var generations = new List<(string, long)>();
        while (await reader.ReadAsync(cancellationToken))
        {
            generations.Add((reader.GetString(0), reader.GetInt64(1)));
        }

        await reader.NextResultAsync(cancellationToken);
        var liveness = new List<(string, long, bool)>();
        while (await reader.ReadAsync(cancellationToken))
        {
            liveness.Add((reader.GetString(0), reader.GetInt64(1), reader.GetBoolean(2)));
        }

        await reader.NextResultAsync(cancellationToken);
        var descriptors = new List<(string, long)>();
        while (await reader.ReadAsync(cancellationToken))
        {
            descriptors.Add((reader.GetString(0), reader.GetInt64(1)));
        }

        return new StoredMembership(generations, liveness, descriptors);
    }

    public async Task<IReadOnlyList<string>> ListTablesAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_schema WHERE type = 'table'";

        var tables = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    private async Task<SqliteConnection> _OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);

        return connection;
    }
}
