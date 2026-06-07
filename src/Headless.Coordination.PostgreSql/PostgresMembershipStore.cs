// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Text.Json;
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
    private static readonly JsonSerializerOptions _JsonOptions = new(JsonSerializerDefaults.Web);

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
                {PostgresMembershipSchema.UpdatedAt}
            )
            VALUES (@ClusterName, @NodeId, 1, clock_timestamp())
            ON CONFLICT ({PostgresMembershipSchema.ClusterName}, {PostgresMembershipSchema.NodeId})
            DO UPDATE SET
                {PostgresMembershipSchema.Generation.CurrentIncarnation} =
                    {PostgresMembershipSchema.Generation.Table}.{PostgresMembershipSchema.Generation.CurrentIncarnation} + 1,
                {PostgresMembershipSchema.UpdatedAt} = clock_timestamp()
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
        const string sql = $"""
            INSERT INTO {PostgresMembershipSchema.Descriptor.Table} (
                {PostgresMembershipSchema.ClusterName},
                {PostgresMembershipSchema.NodeId},
                {PostgresMembershipSchema.Incarnation},
                {PostgresMembershipSchema.Descriptor.HostName},
                {PostgresMembershipSchema.Descriptor.Endpoints},
                {PostgresMembershipSchema.Descriptor.Role},
                {PostgresMembershipSchema.Descriptor.Metadata},
                {PostgresMembershipSchema.CreatedAt}
            )
            VALUES (
                @ClusterName,
                @NodeId,
                @Incarnation,
                @HostName,
                @Endpoints,
                @Role,
                @Metadata,
                clock_timestamp()
            )
            ON CONFLICT ({PostgresMembershipSchema.ClusterName}, {PostgresMembershipSchema.NodeId}, {PostgresMembershipSchema.Incarnation})
            DO NOTHING;
            """;

        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = _CreateCommand(connection, sql);
        command.Parameters.AddWithValue("ClusterName", clusterName);
        command.Parameters.AddWithValue("NodeId", descriptor.Identity.NodeId.Value);
        command.Parameters.AddWithValue("Incarnation", descriptor.Identity.Incarnation.Value);
        command.Parameters.AddWithValue("HostName", (object?)descriptor.HostName ?? DBNull.Value);
        command.Parameters.Add(new NpgsqlParameter("Endpoints", NpgsqlDbType.Jsonb)
        {
            Value = _SerializeDictionary(descriptor.Endpoints),
        });
        command.Parameters.AddWithValue("Role", (object?)descriptor.Role ?? DBNull.Value);
        command.Parameters.Add(new NpgsqlParameter("Metadata", NpgsqlDbType.Jsonb)
        {
            Value = _SerializeDictionary(descriptor.Metadata),
        });

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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
        await _PruneExpiredRowsAsync(connection, clusterName, cancellationToken).ConfigureAwait(false);

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
        await _PruneExpiredRowsAsync(connection, clusterName, cancellationToken).ConfigureAwait(false);
        await using var command = _CreateCommand(connection, sql);
        command.Parameters.AddWithValue("ClusterName", clusterName);
        command.Parameters.AddWithValue("DeadThreshold", DeadThreshold);
        command.Parameters.AddWithValue("SuspicionThreshold", SuspicionThreshold);
        command.Parameters.AddWithValue("AliveState", NodeLivenessState.Alive.ToString());
        command.Parameters.AddWithValue("SuspectedState", NodeLivenessState.Suspected.ToString());
        command.Parameters.AddWithValue("DeadState", NodeLivenessState.Dead.ToString());

        var snapshots = new List<NodeLivenessSnapshot>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var nodeId = new NodeId(reader.GetString(0));
            var incarnation = new NodeIncarnation(reader.GetInt64(1));
            var identity = new NodeIdentity(nodeId, incarnation);
            var role = await reader.IsDBNullAsync(2, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(2);
            var metadataJson = await reader.IsDBNullAsync(3, cancellationToken).ConfigureAwait(false)
                ? "{}"
                : reader.GetString(3);
            var state = Enum.Parse<NodeLivenessState>(reader.GetString(4));

            snapshots.Add(new NodeLivenessSnapshot(identity, state, role, _DeserializeDictionary(metadataJson)));
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
              AND d.{PostgresMembershipSchema.CreatedAt} <= clock_timestamp() - @RetentionThreshold
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

    private NpgsqlCommand _CreateCommand(NpgsqlConnection connection, string commandText)
    {
        return new NpgsqlCommand(commandText, connection)
        {
            CommandType = CommandType.Text,
            CommandTimeout = _GetCommandTimeoutSeconds(providerOptions.Value.CommandTimeout),
        };
    }

    private static string _SerializeDictionary(IReadOnlyDictionary<string, string> value)
    {
        return JsonSerializer.Serialize(value, _JsonOptions);
    }

    private static IReadOnlyDictionary<string, string> _DeserializeDictionary(string value)
    {
        return JsonSerializer.Deserialize<Dictionary<string, string>>(value, _JsonOptions)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private static int _GetCommandTimeoutSeconds(TimeSpan timeout)
    {
        return timeout.TotalSeconds >= int.MaxValue ? int.MaxValue : Math.Max(1, (int)Math.Ceiling(timeout.TotalSeconds));
    }
}
#pragma warning restore CA2100
