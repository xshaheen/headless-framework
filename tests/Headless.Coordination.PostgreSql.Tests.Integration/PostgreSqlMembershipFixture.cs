// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Hosting.Initialization;
using Headless.Testing.Testcontainers;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;
using Testcontainers.PostgreSql;

namespace Tests;

[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class PostgreSqlMembershipFixture
    : HeadlessPostgreSqlFixture,
        ICollectionFixture<PostgreSqlMembershipFixture>,
        ICoordinationOracleFixture
{
    private const string _Schema = HeadlessStorageDefaults.Schema;

    public string ConnectionString => Container.GetConnectionString();

    protected override PostgreSqlBuilder Configure()
    {
        return base.Configure().WithDatabase("coordination_test").WithUsername("postgres").WithPassword("postgres");
    }

    protected override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(CancellationToken.None);
        // The membership tables now live in the feature-owned schema, so drop the schema itself rather than
        // three names that would resolve through search_path to whatever the old default was.
        await using var command = new NpgsqlCommand($"""DROP SCHEMA IF EXISTS "{_Schema}" CASCADE;""", connection);

        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    public void ConfigureProvider(IServiceCollection services, HeadlessCoordinationSetupBuilder setup)
    {
        setup.UsePostgreSql(options => options.ConnectionString = ConnectionString);
    }

    public async Task ShiftAsync(string clusterName, string nodeId, TimeSpan delta, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            $"""
            UPDATE "{_Schema}".coordination_node_generation SET updated_at = updated_at + @delta
            WHERE cluster_name = @cluster AND node_id = @node;
            UPDATE "{_Schema}".coordination_descriptor SET created_at = created_at + @delta
            WHERE cluster_name = @cluster AND node_id = @node;
            UPDATE "{_Schema}".coordination_liveness SET last_beat = last_beat + @delta, left_at = left_at + @delta
            WHERE cluster_name = @cluster AND node_id = @node;
            """,
            connection
        );
        command.Parameters.AddWithValue("cluster", clusterName);
        command.Parameters.AddWithValue("node", nodeId);
        command.Parameters.Add(new NpgsqlParameter<TimeSpan>("delta", NpgsqlDbType.Interval) { TypedValue = delta });

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<StoredMembership> ReadRowsAsync(string clusterName, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            $"""
            SELECT node_id, current_incarnation FROM "{_Schema}".coordination_node_generation WHERE cluster_name = @cluster;
            SELECT node_id, incarnation, left_at IS NOT NULL FROM "{_Schema}".coordination_liveness WHERE cluster_name = @cluster;
            SELECT node_id, incarnation FROM "{_Schema}".coordination_descriptor WHERE cluster_name = @cluster;
            """,
            connection
        );
        command.Parameters.AddWithValue("cluster", clusterName);
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
}
