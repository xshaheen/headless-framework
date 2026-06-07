// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Headless.Coordination.PostgreSql;

#pragma warning disable CA2100 // SQL text is built from internal schema constants only.
internal sealed class PostgresMembershipStore(
    IOptions<PostgreSqlCoordinationOptions> providerOptions,
    IOptions<CoordinationOptions> coordinationOptions
) : DatabaseMembershipStoreBase(coordinationOptions.Value)
{
    protected override async ValueTask<NodeIncarnation> AllocateIncarnationCoreAsync(
        string clusterName,
        NodeId nodeId,
        CancellationToken cancellationToken
    )
    {
        const string sql = $"""
            INSERT INTO {PostgresMembershipSchema.Generation.Table} (
                {PostgresMembershipSchema.ClusterName},
                {PostgresMembershipSchema.NodeId},
                {PostgresMembershipSchema.Generation.CurrentIncarnation},
                {PostgresMembershipSchema.DateUpdated}
            )
            VALUES (@ClusterName, @NodeId, 1, clock_timestamp())
            ON CONFLICT ({PostgresMembershipSchema.ClusterName}, {PostgresMembershipSchema.NodeId})
            DO UPDATE SET
                {PostgresMembershipSchema.Generation.CurrentIncarnation} =
                    {PostgresMembershipSchema.Generation.Table}.{PostgresMembershipSchema.Generation.CurrentIncarnation} + 1,
                {PostgresMembershipSchema.DateUpdated} = clock_timestamp()
            RETURNING {PostgresMembershipSchema.Generation.CurrentIncarnation};
            """;

        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = _CreateCommand(connection, sql);
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
        // Register writes both rows under one generation lock: the write-once cold descriptor and an
        // initial Alive liveness row stamped with the store clock. Both are gated on the freshly-allocated
        // incarnation still being current, so a stale/impossible incarnation establishes neither.
        const string sql = $"""
            WITH generation AS (
                SELECT {PostgresMembershipSchema.Generation.CurrentIncarnation}
                FROM {PostgresMembershipSchema.Generation.Table}
                WHERE {PostgresMembershipSchema.ClusterName} = @ClusterName
                  AND {PostgresMembershipSchema.NodeId} = @NodeId
                FOR UPDATE
            ),
            descriptor AS (
                INSERT INTO {PostgresMembershipSchema.Descriptor.Table} (
                    {PostgresMembershipSchema.ClusterName},
                    {PostgresMembershipSchema.NodeId},
                    {PostgresMembershipSchema.Incarnation},
                    {PostgresMembershipSchema.Descriptor.HostName},
                    {PostgresMembershipSchema.Descriptor.Endpoints},
                    {PostgresMembershipSchema.Descriptor.Role},
                    {PostgresMembershipSchema.Descriptor.Metadata},
                    {PostgresMembershipSchema.DateCreated}
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
                WHERE {PostgresMembershipSchema.Generation.CurrentIncarnation} = @Incarnation
                ON CONFLICT ({PostgresMembershipSchema.ClusterName}, {PostgresMembershipSchema.NodeId}, {PostgresMembershipSchema.Incarnation})
                DO NOTHING
            )
            INSERT INTO {PostgresMembershipSchema.Liveness.Table} (
                {PostgresMembershipSchema.ClusterName},
                {PostgresMembershipSchema.NodeId},
                {PostgresMembershipSchema.Incarnation},
                {PostgresMembershipSchema.Liveness.LastBeat},
                {PostgresMembershipSchema.Liveness.LeftAt}
            )
            SELECT @ClusterName, @NodeId, @Incarnation, clock_timestamp(), NULL
            FROM generation
            WHERE {PostgresMembershipSchema.Generation.CurrentIncarnation} = @Incarnation
            ON CONFLICT ({PostgresMembershipSchema.ClusterName}, {PostgresMembershipSchema.NodeId}, {PostgresMembershipSchema.Incarnation})
            DO UPDATE SET
                {PostgresMembershipSchema.Liveness.LastBeat} = clock_timestamp(),
                {PostgresMembershipSchema.Liveness.LeftAt} = NULL
            WHERE @Incarnation = (SELECT {PostgresMembershipSchema.Generation.CurrentIncarnation} FROM generation);
            """;

        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var command = _CreateCommand(connection, sql, (NpgsqlTransaction)transaction);
        command.Parameters.AddWithValue("ClusterName", clusterName);
        command.Parameters.AddWithValue("NodeId", descriptor.Identity.NodeId.Value);
        command.Parameters.AddWithValue("Incarnation", descriptor.Identity.Incarnation.Value);
        command.Parameters.AddWithValue("HostName", (object?)descriptor.HostName ?? DBNull.Value);
        command.Parameters.Add(new NpgsqlParameter("Endpoints", NpgsqlDbType.Jsonb)
        {
            Value = SerializeDictionary(descriptor.Endpoints),
        });
        command.Parameters.AddWithValue("Role", (object?)descriptor.Role ?? DBNull.Value);
        command.Parameters.Add(new NpgsqlParameter("Metadata", NpgsqlDbType.Jsonb)
        {
            Value = SerializeDictionary(descriptor.Metadata),
        });

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async ValueTask<bool> HeartbeatCoreAsync(
        string clusterName,
        NodeIdentity identity,
        CancellationToken cancellationToken
    )
    {
        const string sql = $"""
            WITH generation AS (
                SELECT {PostgresMembershipSchema.Generation.CurrentIncarnation}
                FROM {PostgresMembershipSchema.Generation.Table}
                WHERE {PostgresMembershipSchema.ClusterName} = @ClusterName
                  AND {PostgresMembershipSchema.NodeId} = @NodeId
                FOR UPDATE
            ),
            heartbeat AS (
                INSERT INTO {PostgresMembershipSchema.Liveness.Table} (
                    {PostgresMembershipSchema.ClusterName},
                    {PostgresMembershipSchema.NodeId},
                    {PostgresMembershipSchema.Incarnation},
                    {PostgresMembershipSchema.Liveness.LastBeat},
                    {PostgresMembershipSchema.Liveness.LeftAt}
                )
                SELECT @ClusterName, @NodeId, @Incarnation, clock_timestamp(), NULL
                FROM generation
                WHERE {PostgresMembershipSchema.Generation.CurrentIncarnation} = @Incarnation
                ON CONFLICT ({PostgresMembershipSchema.ClusterName}, {PostgresMembershipSchema.NodeId}, {PostgresMembershipSchema.Incarnation})
                DO UPDATE SET
                    {PostgresMembershipSchema.Liveness.LastBeat} = clock_timestamp(),
                    {PostgresMembershipSchema.Liveness.LeftAt} = NULL
                WHERE @Incarnation = (
                    SELECT {PostgresMembershipSchema.Generation.CurrentIncarnation}
                    FROM generation
                )
                RETURNING 1
            )
            SELECT EXISTS(SELECT 1 FROM heartbeat);
            """;

        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = _CreateCommand(connection, sql);
        command.Parameters.AddWithValue("ClusterName", clusterName);
        command.Parameters.AddWithValue("NodeId", identity.NodeId.Value);
        command.Parameters.AddWithValue("Incarnation", identity.Incarnation.Value);

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
        const string sql = $"""
            UPDATE {PostgresMembershipSchema.Liveness.Table}
            SET {PostgresMembershipSchema.Liveness.LeftAt} = clock_timestamp()
            WHERE {PostgresMembershipSchema.ClusterName} = @ClusterName
              AND {PostgresMembershipSchema.NodeId} = @NodeId
              AND {PostgresMembershipSchema.Incarnation} = @Incarnation;
            """;

        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = _CreateCommand(connection, sql);
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
        const string sql = $"""
            SELECT
                l.{PostgresMembershipSchema.NodeId},
                l.{PostgresMembershipSchema.Incarnation},
                d.{PostgresMembershipSchema.Descriptor.Role},
                d.{PostgresMembershipSchema.Descriptor.Metadata}::text,
                CASE
                    WHEN l.{PostgresMembershipSchema.Liveness.LeftAt} IS NOT NULL THEN @DeadState
                    WHEN l.{PostgresMembershipSchema.Liveness.LastBeat} <= clock_timestamp() - @DeadThreshold THEN @DeadState
                    WHEN l.{PostgresMembershipSchema.Liveness.LastBeat} <= clock_timestamp() - @SuspicionThreshold THEN @SuspectedState
                    ELSE @AliveState
                END AS state
            FROM {PostgresMembershipSchema.Liveness.Table} l
            JOIN {PostgresMembershipSchema.Generation.Table} g
              ON g.{PostgresMembershipSchema.ClusterName} = l.{PostgresMembershipSchema.ClusterName}
             AND g.{PostgresMembershipSchema.NodeId} = l.{PostgresMembershipSchema.NodeId}
             AND g.{PostgresMembershipSchema.Generation.CurrentIncarnation} = l.{PostgresMembershipSchema.Incarnation}
            LEFT JOIN {PostgresMembershipSchema.Descriptor.Table} d
              ON d.{PostgresMembershipSchema.ClusterName} = l.{PostgresMembershipSchema.ClusterName}
             AND d.{PostgresMembershipSchema.NodeId} = l.{PostgresMembershipSchema.NodeId}
             AND d.{PostgresMembershipSchema.Incarnation} = l.{PostgresMembershipSchema.Incarnation}
            WHERE l.{PostgresMembershipSchema.ClusterName} = @ClusterName
            ORDER BY l.{PostgresMembershipSchema.NodeId}, l.{PostgresMembershipSchema.Incarnation};
            """;

        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        // Pruning is best-effort cleanup; do not abort it on the caller's read cancellation.
        await _PruneExpiredRowsAsync(connection, clusterName, CancellationToken.None).ConfigureAwait(false);
        await using var command = _CreateCommand(connection, sql);
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
            snapshots.Add(ReadSnapshot(reader));
        }

        return snapshots;
    }

    private async ValueTask _PruneExpiredRowsAsync(
        NpgsqlConnection connection,
        string clusterName,
        CancellationToken cancellationToken
    )
    {
        const string sql = $"""
            WITH deleted_liveness AS (
                DELETE FROM {PostgresMembershipSchema.Liveness.Table}
                WHERE {PostgresMembershipSchema.ClusterName} = @ClusterName
                  AND {PostgresMembershipSchema.Liveness.LastBeat} <= clock_timestamp() - @RetentionThreshold
                RETURNING {PostgresMembershipSchema.ClusterName}, {PostgresMembershipSchema.NodeId}, {PostgresMembershipSchema.Incarnation}
            )
            DELETE FROM {PostgresMembershipSchema.Descriptor.Table} d
            WHERE d.{PostgresMembershipSchema.ClusterName} = @ClusterName
              AND d.{PostgresMembershipSchema.DateCreated} <= clock_timestamp() - @RetentionThreshold
              AND NOT EXISTS (
                  SELECT 1
                  FROM {PostgresMembershipSchema.Liveness.Table} l
                  WHERE l.{PostgresMembershipSchema.ClusterName} = d.{PostgresMembershipSchema.ClusterName}
                    AND l.{PostgresMembershipSchema.NodeId} = d.{PostgresMembershipSchema.NodeId}
                    AND l.{PostgresMembershipSchema.Incarnation} = d.{PostgresMembershipSchema.Incarnation}
              );
            """;

        await using var command = _CreateCommand(connection, sql);
        command.Parameters.AddWithValue("ClusterName", clusterName);
        command.Parameters.AddWithValue("RetentionThreshold", DeadThreshold + DeadRetentionWindow);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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
}
#pragma warning restore CA2100
