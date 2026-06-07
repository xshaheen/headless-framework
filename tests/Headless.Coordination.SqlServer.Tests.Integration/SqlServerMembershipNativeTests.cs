// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Testing.Tests;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

[Collection<SqlServerMembershipFixture>]
public sealed class SqlServerMembershipNativeTests(SqlServerMembershipFixture fixture) : TestBase
{
    [Fact]
    public async Task should_create_pascal_case_membership_schema_identifiers()
    {
        await using var node = await fixture.CreateNodeAsync(_Cluster(), "node-a", AbortToken);
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);

        var tables = await _ReadStringsAsync(
            connection,
            """
            SELECT name
            FROM sys.tables
            WHERE name LIKE 'Coordination%'
            ORDER BY name;
            """
        );
        var columns = await _ReadStringsAsync(
            connection,
            """
            SELECT c.name
            FROM sys.columns c
            JOIN sys.tables t ON t.object_id = c.object_id
            WHERE t.name IN ('CoordinationDescriptor', 'CoordinationLiveness', 'CoordinationNodeGeneration')
            ORDER BY c.name;
            """
        );

        tables.Should().Contain("CoordinationDescriptor");
        tables.Should().Contain("CoordinationLiveness");
        tables.Should().Contain("CoordinationNodeGeneration");
        tables.Should().NotContain("coordination_descriptor");
        columns.Should().Contain("ClusterName");
        columns.Should().Contain("NodeId");
        columns.Should().Contain("DateCreated");
        columns.Should().Contain("DateUpdated");
        columns.Should().Contain("CurrentIncarnation");
        columns.Should().Contain("LastBeat");
        columns.Should().Contain("LeftAt");
        columns.Should().NotContain("CreatedAt");
        columns.Should().NotContain("UpdatedAt");
        columns.Should().NotContain("cluster_name");
    }

    [Fact]
    public async Task should_succeed_when_multiple_hosts_initialize_concurrently_against_same_schema()
    {
        // Start from a freshly dropped schema so all five initializers race the same first-time DDL (KTD-5).
        await _DropSchemaAsync();

        var clusters = Enumerable.Range(0, 5).Select(_ => _Cluster()).ToArray();

        // All initializers must complete without surfacing a duplicate-creation error from the concurrent DDL.
        var nodes = await Task.WhenAll(
            clusters.Select(cluster => fixture.CreateNodeAsync(cluster, "node-a", AbortToken).AsTask())
        );

        try
        {
            await using var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync(AbortToken);

            var generationCount = await _CountTablesAsync(connection, "CoordinationNodeGeneration");
            var descriptorCount = await _CountTablesAsync(connection, "CoordinationDescriptor");
            var livenessCount = await _CountTablesAsync(connection, "CoordinationLiveness");
            var livenessIndexCount = await _CountIndexesAsync(
                connection,
                "CoordinationLiveness",
                "IX_CoordinationLiveness_ClusterName_LastBeat"
            );

            // Each table and the liveness index must exist exactly once — a swallowed CREATE that actually failed
            // would leave a missing object, and a non-idempotent CREATE would have thrown above.
            generationCount.Should().Be(1);
            descriptorCount.Should().Be(1);
            livenessCount.Should().Be(1);
            livenessIndexCount.Should().Be(1);
        }
        finally
        {
            foreach (var node in nodes)
            {
                await node.DisposeAsync();
            }
        }
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
        var currentIncarnation = await _ReadCurrentIncarnationAsync(cluster);

        staleAccepted.Should().BeFalse();
        impossibleAccepted.Should().BeFalse();
        currentAccepted.Should().BeTrue();
        currentIncarnation.Should().Be(secondIdentity.Incarnation.Value);
    }

    private static string _Cluster()
    {
        return "native-" + Guid.NewGuid().ToString("N");
    }

    private async Task _DropSchemaAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new SqlCommand(
            """
            DROP TABLE IF EXISTS dbo.CoordinationLiveness;
            DROP TABLE IF EXISTS dbo.CoordinationDescriptor;
            DROP TABLE IF EXISTS dbo.CoordinationNodeGeneration;
            """,
            connection
        );

        await command.ExecuteNonQueryAsync(AbortToken);
    }

    private async Task<int> _CountTablesAsync(SqlConnection connection, string tableName)
    {
        const string sql = """
            SELECT count(*)
            FROM sys.tables t
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE s.name = N'dbo'
              AND t.name = @TableName;
            """;

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("TableName", tableName);

        return Convert.ToInt32(await command.ExecuteScalarAsync(AbortToken), CultureInfo.InvariantCulture);
    }

    private async Task<int> _CountIndexesAsync(SqlConnection connection, string tableName, string indexName)
    {
        const string sql = """
            SELECT count(*)
            FROM sys.indexes i
            JOIN sys.tables t ON t.object_id = i.object_id
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE s.name = N'dbo'
              AND t.name = @TableName
              AND i.name = @IndexName;
            """;

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("TableName", tableName);
        command.Parameters.AddWithValue("IndexName", indexName);

        return Convert.ToInt32(await command.ExecuteScalarAsync(AbortToken), CultureInfo.InvariantCulture);
    }

    private async Task<IReadOnlyList<string>> _ReadStringsAsync(SqlConnection connection, string sql)
    {
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(AbortToken);
        var values = new List<string>();

        while (await reader.ReadAsync(AbortToken))
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private async Task<long> _ReadCurrentIncarnationAsync(string cluster)
    {
        const string sql = """
            SELECT CurrentIncarnation
            FROM dbo.CoordinationNodeGeneration
            WHERE ClusterName = @ClusterName
              AND NodeId = 'node-a';
            """;

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("ClusterName", cluster);

        return Convert.ToInt64(await command.ExecuteScalarAsync(AbortToken), CultureInfo.InvariantCulture);
    }
}
