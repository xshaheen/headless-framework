// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Tests;

[Collection<PostgresMembershipFixture>]
public sealed class PostgresMembershipNativeTests(PostgresMembershipFixture fixture) : TestBase
{
    [Fact]
    public async Task should_create_snake_case_membership_schema_identifiers()
    {
        await using var node = await fixture.CreateNodeAsync(_Cluster(), "node-a", AbortToken);
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);

        var tables = await _ReadStringsAsync(
            connection,
            """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'public'
              AND table_name LIKE 'coordination_%'
            ORDER BY table_name;
            """
        );
        var columns = await _ReadStringsAsync(
            connection,
            """
            SELECT column_name
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name IN ('coordination_descriptor', 'coordination_liveness', 'coordination_node_generation')
            ORDER BY column_name;
            """
        );

        tables.Should().Contain("coordination_descriptor");
        tables.Should().Contain("coordination_liveness");
        tables.Should().Contain("coordination_node_generation");
        tables.Should().NotContain("CoordinationDescriptor");
        columns.Should().Contain("cluster_name");
        columns.Should().Contain("node_id");
        columns.Should().Contain("date_created");
        columns.Should().Contain("date_updated");
        columns.Should().Contain("current_incarnation");
        columns.Should().Contain("last_beat");
        columns.Should().Contain("left_at");
        columns.Should().NotContain("created_at");
        columns.Should().NotContain("updated_at");
        columns.Should().NotContain("ClusterName");
    }

    [Fact]
    public async Task should_reject_stale_and_impossible_heartbeats_without_mutating_current_generation()
    {
        var cluster = _Cluster();
        await using var first = await fixture.CreateNodeAsync(cluster, "node-a", AbortToken);
        var firstIdentity = await first.Membership.RegisterAsync(AbortToken);

        await using var second = await fixture.CreateNodeAsync(cluster, "node-a", AbortToken);
        var secondIdentity = await second.Membership.RegisterAsync(AbortToken);
        var store = second.Services.GetRequiredService<IMembershipStore>();
        var impossibleIdentity = new NodeIdentity(
            secondIdentity.NodeId,
            new NodeIncarnation(secondIdentity.Incarnation.Value + 1)
        );

        var staleAccepted = await store.HeartbeatAsync(firstIdentity, AbortToken);
        var impossibleAccepted = await store.HeartbeatAsync(impossibleIdentity, AbortToken);
        var currentAccepted = await store.HeartbeatAsync(secondIdentity, AbortToken);
        var currentIncarnation = await _ReadCurrentIncarnationAsync(connectionString: fixture.ConnectionString, cluster);

        staleAccepted.Should().BeFalse();
        impossibleAccepted.Should().BeFalse();
        currentAccepted.Should().BeTrue();
        currentIncarnation.Should().Be(secondIdentity.Incarnation.Value);
    }

    private static string _Cluster()
    {
        return "native-" + Guid.NewGuid().ToString("N");
    }

    private async Task<IReadOnlyList<string>> _ReadStringsAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(AbortToken);
        var values = new List<string>();

        while (await reader.ReadAsync(AbortToken))
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private async Task<long> _ReadCurrentIncarnationAsync(string connectionString, string cluster)
    {
        const string sql = """
            SELECT current_incarnation
            FROM coordination_node_generation
            WHERE cluster_name = @ClusterName
              AND node_id = 'node-a';
            """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("ClusterName", cluster);

        return (long)(await command.ExecuteScalarAsync(AbortToken))!;
    }
}
