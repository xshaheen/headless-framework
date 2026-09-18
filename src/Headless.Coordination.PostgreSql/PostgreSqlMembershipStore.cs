// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.Serializer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Headless.Coordination.PostgreSql;

#pragma warning disable CA2100 // SQL text is built from a validated schema plus internal table constants.
internal sealed class PostgreSqlMembershipStore(
    IOptions<PostgreSqlCoordinationOptions> providerOptions,
    IOptions<CoordinationStorageOptions> storageOptions,
    IOptions<CoordinationOptions> coordinationOptions,
    [FromKeyedServices(CoordinationOptions.JsonSerializerServiceKey)] IJsonSerializer serializer
) : DatabaseMembershipStoreBase(coordinationOptions.Value, serializer)
{
    // Membership SQL is invariant after construction (the schema is fixed in IOptions and the store is a DI
    // singleton), so every command text is built once here. Before the schema became configurable these were
    // compile-time constants; building them per call would put a ~40-line interpolation on every heartbeat.
    private readonly string _allocateIncarnationSql = _BuildAllocateIncarnationSql(storageOptions.Value.Schema);
    private readonly string _upsertDescriptorSql = _BuildUpsertDescriptorSql(storageOptions.Value.Schema);
    private readonly string _heartbeatSql = _BuildHeartbeatSql(storageOptions.Value.Schema);
    private readonly string _leaveSql = _BuildLeaveSql(storageOptions.Value.Schema);
    private readonly string _readLivenessSql = _BuildReadLivenessSql(storageOptions.Value.Schema);
    private readonly string _readNodeLivenessSql = _BuildReadNodeLivenessSql(storageOptions.Value.Schema);
    private readonly string _readLiveNodesSql = _BuildReadLiveNodesSql(storageOptions.Value.Schema);
    private readonly string _pruneExpiredRowsSql = _BuildPruneExpiredRowsSql(storageOptions.Value.Schema);

    protected override async ValueTask<NodeIncarnation> AllocateIncarnationCoreAsync(
        string clusterName,
        NodeId nodeId,
        CancellationToken cancellationToken
    )
    {
        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = _CreateCommand(connection, _allocateIncarnationSql);
        command.Parameters.AddWithValue("ClusterName", clusterName);
        command.Parameters.AddWithValue("NodeId", nodeId.Value);

        var value = (long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;

        return new NodeIncarnation(value);
    }

    protected override async ValueTask UpsertDescriptorCoreAsync(
        string clusterName,
        NodeDescriptor descriptor,
        CancellationToken cancellationToken
    )
    {
        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = _CreateCommand(connection, _upsertDescriptorSql, transaction);
        command.Parameters.AddWithValue("ClusterName", clusterName);
        command.Parameters.AddWithValue("NodeId", descriptor.Identity.NodeId.Value);
        command.Parameters.AddWithValue("Incarnation", descriptor.Identity.Incarnation.Value);
        command.Parameters.AddWithValue("HostName", (object?)descriptor.HostName ?? DBNull.Value);
        command.Parameters.Add(
            new NpgsqlParameter("Endpoints", NpgsqlDbType.Jsonb) { Value = SerializeDictionary(descriptor.Endpoints) }
        );
        command.Parameters.AddWithValue("Role", (object?)descriptor.Role ?? DBNull.Value);
        command.Parameters.Add(
            new NpgsqlParameter("Metadata", NpgsqlDbType.Jsonb) { Value = SerializeDictionary(descriptor.Metadata) }
        );

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async ValueTask<bool> HeartbeatCoreAsync(
        string clusterName,
        NodeIdentity identity,
        CancellationToken cancellationToken
    )
    {
        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = _CreateCommand(connection, _heartbeatSql);
        command.Parameters.AddWithValue("ClusterName", clusterName);
        command.Parameters.AddWithValue("NodeId", identity.NodeId.Value);
        command.Parameters.AddWithValue("Incarnation", identity.Incarnation.Value);
        command.Parameters.AddWithValue("DeadThresholdMs", _ToMilliseconds(DeadThreshold));

        var accepted = (bool)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;

        // Retention pruning runs once per tick on the read path; the heartbeat path no longer prunes.
        return accepted;
    }

    protected override async ValueTask LeaveCoreAsync(
        string clusterName,
        NodeIdentity identity,
        CancellationToken cancellationToken
    )
    {
        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = _CreateCommand(connection, _leaveSql);
        command.Parameters.AddWithValue("ClusterName", clusterName);
        command.Parameters.AddWithValue("NodeId", identity.NodeId.Value);
        command.Parameters.AddWithValue("Incarnation", identity.Incarnation.Value);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async ValueTask<IReadOnlyList<NodeLivenessSnapshot>> ReadCurrentLivenessCoreAsync(
        string clusterName,
        CancellationToken cancellationToken
    )
    {
        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        // Pruning is best-effort cleanup; do not abort it on the caller's read cancellation.
        await _PruneExpiredRowsAsync(connection, clusterName, CancellationToken.None).ConfigureAwait(false);
        await using var command = _CreateCommand(connection, _readLivenessSql);
        command.Parameters.AddWithValue("ClusterName", clusterName);
        command.Parameters.AddWithValue("DeadThreshold", DeadThreshold);
        command.Parameters.AddWithValue("SuspicionThreshold", SuspicionThreshold);
        command.Parameters.AddWithValue("AliveState", nameof(NodeLivenessState.Alive));
        command.Parameters.AddWithValue("SuspectedState", nameof(NodeLivenessState.Suspected));
        command.Parameters.AddWithValue("DeadState", nameof(NodeLivenessState.Dead));

        var snapshots = new List<NodeLivenessSnapshot>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            snapshots.Add(await ReadSnapshotAsync(reader, cancellationToken).ConfigureAwait(false));
        }

        return snapshots;
    }

    protected override async ValueTask<NodeLivenessState?> ReadCurrentNodeLivenessCoreAsync(
        string clusterName,
        NodeIdentity identity,
        CancellationToken cancellationToken
    )
    {
        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = _CreateCommand(connection, _readNodeLivenessSql);
        command.Parameters.AddWithValue("ClusterName", clusterName);
        command.Parameters.AddWithValue("NodeId", identity.NodeId.Value);
        command.Parameters.AddWithValue("Incarnation", identity.Incarnation.Value);
        command.Parameters.AddWithValue("DeadThreshold", DeadThreshold);
        command.Parameters.AddWithValue("SuspicionThreshold", SuspicionThreshold);
        command.Parameters.AddWithValue("RetentionThreshold", DeadThreshold + DeadRetentionWindow);
        command.Parameters.AddWithValue("AliveState", nameof(NodeLivenessState.Alive));
        command.Parameters.AddWithValue("SuspectedState", nameof(NodeLivenessState.Suspected));
        command.Parameters.AddWithValue("DeadState", nameof(NodeLivenessState.Dead));

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        return result is string stateText ? Enum.Parse<NodeLivenessState>(stateText) : null;
    }

    protected override async ValueTask<IReadOnlyList<NodeIdentity>> ReadLiveNodesCoreAsync(
        string clusterName,
        CancellationToken cancellationToken
    )
    {
        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = _CreateCommand(connection, _readLiveNodesSql);
        command.Parameters.AddWithValue("ClusterName", clusterName);
        command.Parameters.AddWithValue("SuspicionThreshold", SuspicionThreshold);

        var identities = new List<NodeIdentity>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var nodeId = await reader.GetFieldValueAsync<string>(0, cancellationToken).ConfigureAwait(false);
            var incarnation = await reader.GetFieldValueAsync<long>(1, cancellationToken).ConfigureAwait(false);
            identities.Add(new NodeIdentity(new NodeId(nodeId), new NodeIncarnation(incarnation)));
        }

        return identities;
    }

    private async ValueTask _PruneExpiredRowsAsync(
        NpgsqlConnection connection,
        string clusterName,
        CancellationToken cancellationToken
    )
    {
        await using var command = _CreateCommand(connection, _pruneExpiredRowsSql);
        command.Parameters.AddWithValue("ClusterName", clusterName);
        command.Parameters.AddWithValue("RetentionThreshold", DeadThreshold + DeadRetentionWindow);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string _BuildAllocateIncarnationSql(string schema)
    {
        var generationTable = PostgreSqlMembershipSchema.Qualified(schema, PostgreSqlMembershipSchema.Generation.Table);

        // The target is aliased so the DO UPDATE self-reference stays a plain two-part correlation; a
        // schema-qualified table name would otherwise have to be repeated inside the SET expression.
        return $"""
            INSERT INTO {generationTable} AS generation (
                {PostgreSqlMembershipSchema.ClusterName},
                {PostgreSqlMembershipSchema.NodeId},
                {PostgreSqlMembershipSchema.Generation.CurrentIncarnation},
                {PostgreSqlMembershipSchema.UpdatedAt}
            )
            VALUES (@ClusterName, @NodeId, 1, clock_timestamp())
            ON CONFLICT ({PostgreSqlMembershipSchema.ClusterName}, {PostgreSqlMembershipSchema.NodeId})
            DO UPDATE SET
                {PostgreSqlMembershipSchema.Generation.CurrentIncarnation} =
                    generation.{PostgreSqlMembershipSchema.Generation.CurrentIncarnation} + 1,
                {PostgreSqlMembershipSchema.UpdatedAt} = clock_timestamp()
            RETURNING {PostgreSqlMembershipSchema.Generation.CurrentIncarnation};
            """;
    }

    private static string _BuildUpsertDescriptorSql(string schema)
    {
        var generationTable = PostgreSqlMembershipSchema.Qualified(schema, PostgreSqlMembershipSchema.Generation.Table);
        var descriptorTable = PostgreSqlMembershipSchema.Qualified(schema, PostgreSqlMembershipSchema.Descriptor.Table);
        var livenessTable = PostgreSqlMembershipSchema.Qualified(schema, PostgreSqlMembershipSchema.Liveness.Table);

        // Register writes both rows under one generation lock: the write-once cold descriptor and an
        // initial Alive liveness row stamped with the store clock. Both are gated on the freshly-allocated
        // incarnation still being current, so a stale/impossible incarnation establishes neither.
        return $"""
            WITH generation AS (
                SELECT {PostgreSqlMembershipSchema.Generation.CurrentIncarnation}
                FROM {generationTable}
                WHERE {PostgreSqlMembershipSchema.ClusterName} = @ClusterName
                  AND {PostgreSqlMembershipSchema.NodeId} = @NodeId
                FOR UPDATE
            ),
            descriptor AS (
                INSERT INTO {descriptorTable} (
                    {PostgreSqlMembershipSchema.ClusterName},
                    {PostgreSqlMembershipSchema.NodeId},
                    {PostgreSqlMembershipSchema.Incarnation},
                    {PostgreSqlMembershipSchema.Descriptor.HostName},
                    {PostgreSqlMembershipSchema.Descriptor.Endpoints},
                    {PostgreSqlMembershipSchema.Descriptor.Role},
                    {PostgreSqlMembershipSchema.Descriptor.Metadata},
                    {PostgreSqlMembershipSchema.CreatedAt}
                )
                SELECT
                    @ClusterName,
                    @NodeId,
                    @Incarnation,
                    @HostName,
                    @Endpoints,
                    @Role,
                    @Metadata,
                    clock_timestamp()
                FROM generation
                WHERE {PostgreSqlMembershipSchema.Generation.CurrentIncarnation} = @Incarnation
                ON CONFLICT ({PostgreSqlMembershipSchema.ClusterName}, {PostgreSqlMembershipSchema.NodeId}, {PostgreSqlMembershipSchema.Incarnation})
                DO NOTHING
            )
            INSERT INTO {livenessTable} (
                {PostgreSqlMembershipSchema.ClusterName},
                {PostgreSqlMembershipSchema.NodeId},
                {PostgreSqlMembershipSchema.Incarnation},
                {PostgreSqlMembershipSchema.Liveness.LastBeat},
                {PostgreSqlMembershipSchema.Liveness.LeftAt}
            )
            SELECT @ClusterName, @NodeId, @Incarnation, clock_timestamp(), NULL
            FROM generation
            WHERE {PostgreSqlMembershipSchema.Generation.CurrentIncarnation} = @Incarnation
            ON CONFLICT ({PostgreSqlMembershipSchema.ClusterName}, {PostgreSqlMembershipSchema.NodeId}, {PostgreSqlMembershipSchema.Incarnation})
            DO UPDATE SET
                {PostgreSqlMembershipSchema.Liveness.LastBeat} = clock_timestamp(),
                {PostgreSqlMembershipSchema.Liveness.LeftAt} = NULL
            WHERE @Incarnation = (SELECT {PostgreSqlMembershipSchema.Generation.CurrentIncarnation} FROM generation);
            """;
    }

    private static string _BuildHeartbeatSql(string schema)
    {
        var generationTable = PostgreSqlMembershipSchema.Qualified(schema, PostgreSqlMembershipSchema.Generation.Table);
        var livenessTable = PostgreSqlMembershipSchema.Qualified(schema, PostgreSqlMembershipSchema.Liveness.Table);

        return $"""
            WITH generation AS (
                SELECT {PostgreSqlMembershipSchema.Generation.CurrentIncarnation}
                FROM {generationTable}
                WHERE {PostgreSqlMembershipSchema.ClusterName} = @ClusterName
                  AND {PostgreSqlMembershipSchema.NodeId} = @NodeId
                FOR UPDATE
            ),
            heartbeat AS (
                UPDATE {livenessTable} AS liveness
                SET {PostgreSqlMembershipSchema.Liveness.LastBeat} = clock_timestamp()
                FROM generation
                WHERE liveness.{PostgreSqlMembershipSchema.ClusterName} = @ClusterName
                  AND liveness.{PostgreSqlMembershipSchema.NodeId} = @NodeId
                  AND liveness.{PostgreSqlMembershipSchema.Incarnation} = @Incarnation
                  AND generation.{PostgreSqlMembershipSchema.Generation.CurrentIncarnation} = @Incarnation
                  AND liveness.{PostgreSqlMembershipSchema.Liveness.LeftAt} IS NULL
                  AND liveness.{PostgreSqlMembershipSchema.Liveness.LastBeat}
                      > clock_timestamp() - (@DeadThresholdMs * INTERVAL '1 millisecond')
                RETURNING 1
            )
            SELECT EXISTS(SELECT 1 FROM heartbeat);
            """;
    }

    private static string _BuildLeaveSql(string schema)
    {
        var livenessTable = PostgreSqlMembershipSchema.Qualified(schema, PostgreSqlMembershipSchema.Liveness.Table);

        return $"""
            UPDATE {livenessTable}
            SET {PostgreSqlMembershipSchema.Liveness.LeftAt} = clock_timestamp()
            WHERE {PostgreSqlMembershipSchema.ClusterName} = @ClusterName
              AND {PostgreSqlMembershipSchema.NodeId} = @NodeId
              AND {PostgreSqlMembershipSchema.Incarnation} = @Incarnation;
            """;
    }

    private static string _BuildReadLivenessSql(string schema)
    {
        var generationTable = PostgreSqlMembershipSchema.Qualified(schema, PostgreSqlMembershipSchema.Generation.Table);
        var descriptorTable = PostgreSqlMembershipSchema.Qualified(schema, PostgreSqlMembershipSchema.Descriptor.Table);
        var livenessTable = PostgreSqlMembershipSchema.Qualified(schema, PostgreSqlMembershipSchema.Liveness.Table);

        return $"""
            SELECT
                l.{PostgreSqlMembershipSchema.NodeId},
                l.{PostgreSqlMembershipSchema.Incarnation},
                d.{PostgreSqlMembershipSchema.Descriptor.Role},
                d.{PostgreSqlMembershipSchema.Descriptor.Metadata}::text,
                CASE
                    WHEN l.{PostgreSqlMembershipSchema.Liveness.LeftAt} IS NOT NULL THEN @DeadState
                    WHEN l.{PostgreSqlMembershipSchema.Liveness.LastBeat} <= clock_timestamp() - @DeadThreshold THEN @DeadState
                    WHEN l.{PostgreSqlMembershipSchema.Liveness.LastBeat} <= clock_timestamp() - @SuspicionThreshold THEN @SuspectedState
                    ELSE @AliveState
                END AS state
            FROM {livenessTable} l
            JOIN {generationTable} g
              ON g.{PostgreSqlMembershipSchema.ClusterName} = l.{PostgreSqlMembershipSchema.ClusterName}
             AND g.{PostgreSqlMembershipSchema.NodeId} = l.{PostgreSqlMembershipSchema.NodeId}
             AND g.{PostgreSqlMembershipSchema.Generation.CurrentIncarnation} = l.{PostgreSqlMembershipSchema.Incarnation}
            LEFT JOIN {descriptorTable} d
              ON d.{PostgreSqlMembershipSchema.ClusterName} = l.{PostgreSqlMembershipSchema.ClusterName}
             AND d.{PostgreSqlMembershipSchema.NodeId} = l.{PostgreSqlMembershipSchema.NodeId}
             AND d.{PostgreSqlMembershipSchema.Incarnation} = l.{PostgreSqlMembershipSchema.Incarnation}
            WHERE l.{PostgreSqlMembershipSchema.ClusterName} = @ClusterName
            ORDER BY l.{PostgreSqlMembershipSchema.NodeId}, l.{PostgreSqlMembershipSchema.Incarnation};
            """;
    }

    private static string _BuildReadNodeLivenessSql(string schema)
    {
        var generationTable = PostgreSqlMembershipSchema.Qualified(schema, PostgreSqlMembershipSchema.Generation.Table);
        var livenessTable = PostgreSqlMembershipSchema.Qualified(schema, PostgreSqlMembershipSchema.Liveness.Table);

        // Targeted single-row read: join to the generation authority so a non-current incarnation yields no
        // row (absent -> null), classify with the store clock identically to ReadCurrentLivenessCoreAsync, and
        // exclude retention-expired rows in the WHERE so they read as absent (null) exactly as the snapshot's
        // prune would remove them. Read-only: no prune, no descriptor join (state only).
        return $"""
            SELECT
                CASE
                    WHEN l.{PostgreSqlMembershipSchema.Liveness.LeftAt} IS NOT NULL THEN @DeadState
                    WHEN l.{PostgreSqlMembershipSchema.Liveness.LastBeat} <= clock_timestamp() - @DeadThreshold THEN @DeadState
                    WHEN l.{PostgreSqlMembershipSchema.Liveness.LastBeat} <= clock_timestamp() - @SuspicionThreshold THEN @SuspectedState
                    ELSE @AliveState
                END AS state
            FROM {livenessTable} l
            JOIN {generationTable} g
              ON g.{PostgreSqlMembershipSchema.ClusterName} = l.{PostgreSqlMembershipSchema.ClusterName}
             AND g.{PostgreSqlMembershipSchema.NodeId} = l.{PostgreSqlMembershipSchema.NodeId}
             AND g.{PostgreSqlMembershipSchema.Generation.CurrentIncarnation} = l.{PostgreSqlMembershipSchema.Incarnation}
            WHERE l.{PostgreSqlMembershipSchema.ClusterName} = @ClusterName
              AND l.{PostgreSqlMembershipSchema.NodeId} = @NodeId
              AND l.{PostgreSqlMembershipSchema.Incarnation} = @Incarnation
              AND l.{PostgreSqlMembershipSchema.Liveness.LastBeat} > clock_timestamp() - @RetentionThreshold;
            """;
    }

    private static string _BuildReadLiveNodesSql(string schema)
    {
        var generationTable = PostgreSqlMembershipSchema.Qualified(schema, PostgreSqlMembershipSchema.Generation.Table);
        var livenessTable = PostgreSqlMembershipSchema.Qualified(schema, PostgreSqlMembershipSchema.Liveness.Table);

        // Alive-only, current-generation, identities only: join the generation authority so superseded
        // incarnations are excluded, keep rows that have not left and whose store-clock beat age is below the
        // suspicion threshold. No descriptor join, no metadata, no prune — the base orders the result.
        return $"""
            SELECT l.{PostgreSqlMembershipSchema.NodeId}, l.{PostgreSqlMembershipSchema.Incarnation}
            FROM {livenessTable} l
            JOIN {generationTable} g
              ON g.{PostgreSqlMembershipSchema.ClusterName} = l.{PostgreSqlMembershipSchema.ClusterName}
             AND g.{PostgreSqlMembershipSchema.NodeId} = l.{PostgreSqlMembershipSchema.NodeId}
             AND g.{PostgreSqlMembershipSchema.Generation.CurrentIncarnation} = l.{PostgreSqlMembershipSchema.Incarnation}
            WHERE l.{PostgreSqlMembershipSchema.ClusterName} = @ClusterName
              AND l.{PostgreSqlMembershipSchema.Liveness.LeftAt} IS NULL
              AND l.{PostgreSqlMembershipSchema.Liveness.LastBeat} > clock_timestamp() - @SuspicionThreshold;
            """;
    }

    private static string _BuildPruneExpiredRowsSql(string schema)
    {
        var descriptorTable = PostgreSqlMembershipSchema.Qualified(schema, PostgreSqlMembershipSchema.Descriptor.Table);
        var livenessTable = PostgreSqlMembershipSchema.Qualified(schema, PostgreSqlMembershipSchema.Liveness.Table);

        // Two separate statements, NOT a single data-modifying CTE: PostgreSQL runs all sub-statements of a WITH
        // under one snapshot, so a descriptor DELETE inside the same statement cannot see rows the liveness DELETE
        // just removed and would leave orphaned descriptors for one extra prune cycle. Sequential statements in one
        // command run as an implicit transaction where the second sees the first's deletes, matching the SqlServer
        // provider's single-pass cleanup.
        return $"""
            DELETE FROM {livenessTable}
            WHERE {PostgreSqlMembershipSchema.ClusterName} = @ClusterName
              AND {PostgreSqlMembershipSchema.Liveness.LastBeat} <= clock_timestamp() - @RetentionThreshold;

            DELETE FROM {descriptorTable} d
            WHERE d.{PostgreSqlMembershipSchema.ClusterName} = @ClusterName
              AND d.{PostgreSqlMembershipSchema.CreatedAt} <= clock_timestamp() - @RetentionThreshold
              AND NOT EXISTS (
                  SELECT 1
                  FROM {livenessTable} l
                  WHERE l.{PostgreSqlMembershipSchema.ClusterName} = d.{PostgreSqlMembershipSchema.ClusterName}
                    AND l.{PostgreSqlMembershipSchema.NodeId} = d.{PostgreSqlMembershipSchema.NodeId}
                    AND l.{PostgreSqlMembershipSchema.Incarnation} = d.{PostgreSqlMembershipSchema.Incarnation}
              );
            """;
    }

    private NpgsqlCommand _CreateCommand(
        NpgsqlConnection connection,
        string commandText,
        NpgsqlTransaction? transaction = null
    )
    {
        return new NpgsqlCommand(commandText, connection, transaction)
        {
            CommandType = CommandType.Text,
            CommandTimeout = DatabaseAdoHelpers.GetCommandTimeoutSeconds(providerOptions.Value.CommandTimeout),
        };
    }

    private static long _ToMilliseconds(TimeSpan value)
    {
        return (long)Math.Ceiling(value.TotalMilliseconds);
    }
}
#pragma warning restore CA2100
