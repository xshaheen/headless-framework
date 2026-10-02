// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Testing.Testcontainers;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class SqlServerMembershipFixture
    : HeadlessSqlServerFixture,
        IAsyncLifetime,
        ICollectionFixture<SqlServerMembershipFixture>,
        ICoordinationOracleFixture
{
    // Re-implemented rather than overridden: the base fixture's InitializeAsync is not virtual. The container is reused
    // across runs, and the schema runner trusts its history, so a run that dropped the tables but not the history would
    // leave the next run believing the tables exist; start every run from neither.
    public new async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(CancellationToken.None);
        await using var reset = new SqlCommand(
            """
            DROP TABLE IF EXISTS headless.CoordinationLiveness;
            DROP TABLE IF EXISTS headless.CoordinationDescriptor;
            DROP TABLE IF EXISTS headless.CoordinationNodeGeneration;
            DROP TABLE IF EXISTS headless.headless_schema_history;
            """,
            connection
        );
        await reset.ExecuteNonQueryAsync(CancellationToken.None);
    }

    public void ConfigureProvider(IServiceCollection services, HeadlessCoordinationSetupBuilder setup)
    {
        setup.UseSqlServer(options => options.ConnectionString = ConnectionString);
    }

    public async Task ShiftAsync(string clusterName, string nodeId, TimeSpan delta, CancellationToken cancellationToken)
    {
        // The oracle moves time in whole seconds, well inside DATEADD's int range.
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            """
            UPDATE headless.CoordinationNodeGeneration SET UpdatedAt = DATEADD(second, @seconds, UpdatedAt)
            WHERE ClusterName = @cluster AND NodeId = @node;
            UPDATE headless.CoordinationDescriptor SET CreatedAt = DATEADD(second, @seconds, CreatedAt)
            WHERE ClusterName = @cluster AND NodeId = @node;
            UPDATE headless.CoordinationLiveness
            SET LastBeat = DATEADD(second, @seconds, LastBeat), LeftAt = DATEADD(second, @seconds, LeftAt)
            WHERE ClusterName = @cluster AND NodeId = @node;
            """,
            connection
        );
        command.Parameters.AddWithValue("cluster", clusterName);
        command.Parameters.AddWithValue("node", nodeId);
        command.Parameters.AddWithValue("seconds", checked((int)delta.TotalSeconds));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<StoredMembership> ReadRowsAsync(string clusterName, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            """
            SELECT NodeId, CurrentIncarnation FROM headless.CoordinationNodeGeneration WHERE ClusterName = @cluster;
            SELECT NodeId, Incarnation, CAST(CASE WHEN LeftAt IS NULL THEN 0 ELSE 1 END AS bit)
            FROM headless.CoordinationLiveness WHERE ClusterName = @cluster;
            SELECT NodeId, Incarnation FROM headless.CoordinationDescriptor WHERE ClusterName = @cluster;
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
