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
            INSERT INTO {MembershipSchema.Generation.Table} (
                {MembershipSchema.ClusterName},
                {MembershipSchema.NodeId},
                {MembershipSchema.Generation.CurrentIncarnation},
                {MembershipSchema.UpdatedAt}
            )
            VALUES (@ClusterName, @NodeId, 1, clock_timestamp())
            ON CONFLICT ({MembershipSchema.ClusterName}, {MembershipSchema.NodeId})
            DO UPDATE SET
                {MembershipSchema.Generation.CurrentIncarnation} =
                    {MembershipSchema.Generation.Table}.{MembershipSchema.Generation.CurrentIncarnation} + 1,
                {MembershipSchema.UpdatedAt} = clock_timestamp()
            RETURNING {MembershipSchema.Generation.CurrentIncarnation};
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
            INSERT INTO {MembershipSchema.Descriptor.Table} (
                {MembershipSchema.ClusterName},
                {MembershipSchema.NodeId},
                {MembershipSchema.Incarnation},
                {MembershipSchema.Descriptor.HostName},
                {MembershipSchema.Descriptor.Endpoints},
                {MembershipSchema.Descriptor.Role},
                {MembershipSchema.Descriptor.Metadata},
                {MembershipSchema.CreatedAt}
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
            ON CONFLICT ({MembershipSchema.ClusterName}, {MembershipSchema.NodeId}, {MembershipSchema.Incarnation})
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
                SELECT {MembershipSchema.Generation.CurrentIncarnation}
                FROM {MembershipSchema.Generation.Table}
                WHERE {MembershipSchema.ClusterName} = @ClusterName
                  AND {MembershipSchema.NodeId} = @NodeId
                FOR UPDATE
            ),
            heartbeat AS (
                INSERT INTO {MembershipSchema.Liveness.Table} (
                    {MembershipSchema.ClusterName},
                    {MembershipSchema.NodeId},
                    {MembershipSchema.Incarnation},
                    {MembershipSchema.Liveness.LastBeat},
                    {MembershipSchema.Liveness.LeftAt}
                )
                SELECT @ClusterName, @NodeId, @Incarnation, clock_timestamp(), NULL
                FROM generation
                WHERE {MembershipSchema.Generation.CurrentIncarnation} = @Incarnation
                ON CONFLICT ({MembershipSchema.ClusterName}, {MembershipSchema.NodeId}, {MembershipSchema.Incarnation})
                DO UPDATE SET
                    {MembershipSchema.Liveness.LastBeat} = clock_timestamp(),
                    {MembershipSchema.Liveness.LeftAt} = NULL
                WHERE @Incarnation = (
                    SELECT {MembershipSchema.Generation.CurrentIncarnation}
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
            UPDATE {MembershipSchema.Liveness.Table}
            SET {MembershipSchema.Liveness.LeftAt} = clock_timestamp()
            WHERE {MembershipSchema.ClusterName} = @ClusterName
              AND {MembershipSchema.NodeId} = @NodeId
              AND {MembershipSchema.Incarnation} = @Incarnation;
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
                l.{MembershipSchema.NodeId},
                l.{MembershipSchema.Incarnation},
                d.{MembershipSchema.Descriptor.Role},
                d.{MembershipSchema.Descriptor.Metadata}::text,
                CASE
                    WHEN l.{MembershipSchema.Liveness.LeftAt} IS NOT NULL THEN @DeadState
                    WHEN l.{MembershipSchema.Liveness.LastBeat} <= clock_timestamp() - @DeadThreshold THEN @DeadState
                    WHEN l.{MembershipSchema.Liveness.LastBeat} <= clock_timestamp() - @SuspicionThreshold THEN @SuspectedState
                    ELSE @AliveState
                END AS state
            FROM {MembershipSchema.Liveness.Table} l
            JOIN {MembershipSchema.Generation.Table} g
              ON g.{MembershipSchema.ClusterName} = l.{MembershipSchema.ClusterName}
             AND g.{MembershipSchema.NodeId} = l.{MembershipSchema.NodeId}
             AND g.{MembershipSchema.Generation.CurrentIncarnation} = l.{MembershipSchema.Incarnation}
            LEFT JOIN {MembershipSchema.Descriptor.Table} d
              ON d.{MembershipSchema.ClusterName} = l.{MembershipSchema.ClusterName}
             AND d.{MembershipSchema.NodeId} = l.{MembershipSchema.NodeId}
             AND d.{MembershipSchema.Incarnation} = l.{MembershipSchema.Incarnation}
            WHERE l.{MembershipSchema.ClusterName} = @ClusterName
            ORDER BY l.{MembershipSchema.NodeId}, l.{MembershipSchema.Incarnation};
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
                DELETE FROM {MembershipSchema.Liveness.Table}
                WHERE {MembershipSchema.ClusterName} = @ClusterName
                  AND {MembershipSchema.Liveness.LastBeat} <= clock_timestamp() - @RetentionThreshold
                RETURNING {MembershipSchema.ClusterName}, {MembershipSchema.NodeId}, {MembershipSchema.Incarnation}
            )
            DELETE FROM {MembershipSchema.Descriptor.Table} d
            WHERE d.{MembershipSchema.ClusterName} = @ClusterName
              AND d.{MembershipSchema.CreatedAt} <= clock_timestamp() - @RetentionThreshold
              AND NOT EXISTS (
                  SELECT 1
                  FROM {MembershipSchema.Liveness.Table} l
                  WHERE l.{MembershipSchema.ClusterName} = d.{MembershipSchema.ClusterName}
                    AND l.{MembershipSchema.NodeId} = d.{MembershipSchema.NodeId}
                    AND l.{MembershipSchema.Incarnation} = d.{MembershipSchema.Incarnation}
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
