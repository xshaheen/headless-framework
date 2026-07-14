// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Hosting.Initialization;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Tests;

[Collection<PostgreSqlMembershipFixture>]
public sealed class PostgreSqlMembershipNativeTests(PostgreSqlMembershipFixture fixture) : TestBase
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
            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            await connection.OpenAsync(AbortToken);

            var generationCount = await _CountTablesAsync(connection, "coordination_node_generation");
            var descriptorCount = await _CountTablesAsync(connection, "coordination_descriptor");
            var livenessCount = await _CountTablesAsync(connection, "coordination_liveness");
            var livenessIndexCount = await _CountIndexesAsync(connection, "ix_coordination_liveness_cluster_lastbeat");

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
        var currentIncarnation = await _ReadCurrentIncarnationAsync(
            connectionString: fixture.ConnectionString,
            cluster
        );

        staleAccepted.Should().BeFalse();
        impossibleAccepted.Should().BeFalse();
        currentAccepted.Should().BeTrue();
        currentIncarnation.Should().Be(secondIdentity.Incarnation.Value);
    }

    [Fact]
    public async Task should_prune_both_descriptor_and_liveness_rows_after_retention_window()
    {
        var cluster = _Cluster();
        await using var node = await fixture.CreateNodeAsync(cluster, "node-a", AbortToken);
        await node.Membership.RegisterAsync(AbortToken);

        // Age past the prune window, then trigger exactly one liveness read so the read-path prune runs a single pass.
        await TimeProvider.System.Delay(CoordinationFixtureExtensions.AfterPruneWait, AbortToken);
        await node.Membership.GetLivenessSnapshotAsync(AbortToken);

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        var livenessRows = await _CountClusterRowsAsync(
            connection,
            "SELECT count(*) FROM coordination_liveness WHERE cluster_name = @ClusterName;",
            cluster
        );
        var descriptorRows = await _CountClusterRowsAsync(
            connection,
            "SELECT count(*) FROM coordination_descriptor WHERE cluster_name = @ClusterName;",
            cluster
        );

        // A single prune pass must physically remove BOTH the expired liveness row and its now-orphaned descriptor.
        // The descriptor leak is invisible to snapshot/live-set assertions (reads ignore descriptor-only rows), so a
        // direct row count is the only check that catches it — and it guards the two-statement prune from regressing
        // back into a single data-modifying CTE, which would leave the descriptor for one extra cycle.
        livenessRows.Should().Be(0);
        descriptorRows.Should().Be(0);
    }

    [Fact]
    public async Task should_wrap_initialization_failure_in_InvalidOperationException()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessCoordination(setup =>
        {
            setup.UsePostgreSql(options =>
            {
                // Unreachable endpoint with a short timeout: the connection open fails fast inside the initializer's
                // try, exercising the non-race failure branch that wraps the raw error for operator diagnosis.
                options.ConnectionString =
                    "Host=127.0.0.1;Port=1;Username=postgres;Password=postgres;Database=postgres;Timeout=2;";
            });
            setup.Configure(options =>
            {
                options.ClusterName = _Cluster();
                options.ConfiguredNodeId = "node-a";
            });
        });

        await using var provider = services.BuildServiceProvider();
        var initializer = provider.GetServices<IInitializer>().OfType<HostedInitializer>().Single();

        var act = async () => await initializer.InitializeAsync(AbortToken);

        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.WithMessage("*failed to initialize the membership schema*");
        // The original transport error must be preserved as the inner exception so the cause is not lost.
        thrown.Which.InnerException.Should().NotBeNull();
    }

    [Fact]
    public async Task should_classify_targeted_node_liveness_across_states()
    {
        var cluster = _Cluster();
        await using var node = await fixture.CreateNodeAsync(cluster, "node-a", AbortToken);
        var identity = await node.Membership.RegisterAsync(AbortToken);
        var store = node.Services.GetRequiredService<IMembershipStore>();

        // Alive immediately after register (durable liveness established without a loop tick).
        (await store.ReadNodeLivenessAsync(identity, AbortToken))
            .Should()
            .Be(NodeLivenessState.Alive);

        // Aged into the suspicion band -> Suspected.
        await TimeProvider.System.Delay(CoordinationFixtureExtensions.SuspectedWait, AbortToken);
        (await store.ReadNodeLivenessAsync(identity, AbortToken)).Should().Be(NodeLivenessState.Suspected);

        // Aged past the dead threshold but still inside the retention window -> Dead.
        await TimeProvider.System.Delay(
            CoordinationFixtureExtensions.DeadButRetainedWait - CoordinationFixtureExtensions.SuspectedWait,
            AbortToken
        );
        (await store.ReadNodeLivenessAsync(identity, AbortToken)).Should().Be(NodeLivenessState.Dead);
    }

    [Fact]
    public async Task should_return_dead_for_targeted_read_after_graceful_leave()
    {
        var cluster = _Cluster();
        await using var node = await fixture.CreateNodeAsync(cluster, "node-a", AbortToken);
        var identity = await node.Membership.RegisterAsync(AbortToken);
        var store = node.Services.GetRequiredService<IMembershipStore>();

        await store.LeaveAsync(identity, AbortToken);

        (await store.ReadNodeLivenessAsync(identity, AbortToken)).Should().Be(NodeLivenessState.Dead);
    }

    [Fact]
    public async Task should_return_null_for_targeted_read_of_stale_and_unregistered_identities()
    {
        var cluster = _Cluster();
        await using var first = await fixture.CreateNodeAsync(cluster, "node-a", AbortToken);
        var firstIdentity = await first.Membership.RegisterAsync(AbortToken);

        await using var second = await fixture.CreateNodeAsync(cluster, "node-a", AbortToken);
        var secondIdentity = await second.Membership.RegisterAsync(AbortToken);
        var store = second.Services.GetRequiredService<IMembershipStore>();
        var unregistered = new NodeIdentity(new NodeId("node-z"), new NodeIncarnation(1));

        // Superseded prior incarnation is not current-generation -> absent (null).
        (await store.ReadNodeLivenessAsync(firstIdentity, AbortToken))
            .Should()
            .BeNull();
        // Never-registered node -> absent (null).
        (await store.ReadNodeLivenessAsync(unregistered, AbortToken))
            .Should()
            .BeNull();
        // The current incarnation is still alive.
        (await store.ReadNodeLivenessAsync(secondIdentity, AbortToken))
            .Should()
            .Be(NodeLivenessState.Alive);
    }

    [Fact]
    public async Task should_return_null_without_pruning_for_retention_expired_targeted_read()
    {
        var cluster = _Cluster();
        await using var node = await fixture.CreateNodeAsync(cluster, "node-a", AbortToken);
        var identity = await node.Membership.RegisterAsync(AbortToken);
        var store = node.Services.GetRequiredService<IMembershipStore>();

        // Age past the retention window, then read the targeted path FIRST — before any snapshot read could
        // prune the row. The snapshot path produces absence by deleting; the targeted path must produce the same
        // absence by classification (returning null) without writing.
        await TimeProvider.System.Delay(CoordinationFixtureExtensions.AfterPruneWait, AbortToken);

        var state = await store.ReadNodeLivenessAsync(identity, AbortToken);

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        var livenessRows = await _CountClusterRowsAsync(
            connection,
            "SELECT count(*) FROM coordination_liveness WHERE cluster_name = @ClusterName;",
            cluster
        );

        state.Should().BeNull();
        // The retention-expired row must still be physically present: the targeted read classifies, it never prunes.
        livenessRows.Should().Be(1);
    }

    private static string _Cluster()
    {
        return "native-" + Guid.NewGuid().ToString("N");
    }

    private async Task<long> _CountClusterRowsAsync(NpgsqlConnection connection, string sql, string cluster)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("ClusterName", cluster);

        return (long)(await command.ExecuteScalarAsync(AbortToken))!;
    }

    private async Task _DropSchemaAsync()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new NpgsqlCommand(
            "DROP TABLE IF EXISTS coordination_liveness, coordination_descriptor, coordination_node_generation CASCADE;",
            connection
        );

        await command.ExecuteNonQueryAsync(AbortToken);
    }

    private async Task<long> _CountTablesAsync(NpgsqlConnection connection, string tableName)
    {
        const string sql = """
            SELECT count(*)
            FROM information_schema.tables
            WHERE table_schema = 'public'
              AND table_name = @TableName;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("TableName", tableName);

        return (long)(await command.ExecuteScalarAsync(AbortToken))!;
    }

    private async Task<long> _CountIndexesAsync(NpgsqlConnection connection, string indexName)
    {
        const string sql = """
            SELECT count(*)
            FROM pg_indexes
            WHERE schemaname = 'public'
              AND indexname = @IndexName;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("IndexName", indexName);

        return (long)(await command.ExecuteScalarAsync(AbortToken))!;
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
