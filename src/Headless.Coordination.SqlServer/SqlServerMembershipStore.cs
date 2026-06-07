// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Headless.Coordination.SqlServer;

#pragma warning disable CA2100 // SQL text is built from validated schema plus internal table constants.
internal sealed class SqlServerMembershipStore(
    IOptions<SqlServerCoordinationOptions> providerOptions,
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
        var options = providerOptions.Value;
        var generationTable = _Qualified(MembershipSchema.Generation.Table);
        var sql = $$"""
            DECLARE @allocated table ([value] bigint NOT NULL);

            UPDATE {{generationTable}} WITH (UPDLOCK, HOLDLOCK)
            SET [{{MembershipSchema.Generation.CurrentIncarnation}}] = [{{MembershipSchema.Generation.CurrentIncarnation}}] + 1,
                [{{MembershipSchema.UpdatedAt}}] = SYSUTCDATETIME()
            OUTPUT inserted.[{{MembershipSchema.Generation.CurrentIncarnation}}] INTO @allocated
            WHERE [{{MembershipSchema.ClusterName}}] = @ClusterName
              AND [{{MembershipSchema.NodeId}}] = @NodeId;

            IF NOT EXISTS (SELECT 1 FROM @allocated)
            BEGIN
                BEGIN TRY
                    INSERT INTO {{generationTable}} (
                        [{{MembershipSchema.ClusterName}}],
                        [{{MembershipSchema.NodeId}}],
                        [{{MembershipSchema.Generation.CurrentIncarnation}}],
                        [{{MembershipSchema.UpdatedAt}}]
                    )
                    VALUES (@ClusterName, @NodeId, 1, SYSUTCDATETIME());

                    INSERT INTO @allocated ([value]) VALUES (1);
                END TRY
                BEGIN CATCH
                    IF ERROR_NUMBER() NOT IN (2601, 2627) THROW;

                    UPDATE {{generationTable}} WITH (UPDLOCK, HOLDLOCK)
                    SET [{{MembershipSchema.Generation.CurrentIncarnation}}] = [{{MembershipSchema.Generation.CurrentIncarnation}}] + 1,
                        [{{MembershipSchema.UpdatedAt}}] = SYSUTCDATETIME()
                    OUTPUT inserted.[{{MembershipSchema.Generation.CurrentIncarnation}}] INTO @allocated
                    WHERE [{{MembershipSchema.ClusterName}}] = @ClusterName
                      AND [{{MembershipSchema.NodeId}}] = @NodeId;
                END CATCH;
            END;

            SELECT TOP (1) [value] FROM @allocated;
            """;

        await using var connection = options.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqlTransaction)await connection
            .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);
        await using var command = _CreateCommand(connection, sql, transaction);
        command.Parameters.AddWithValue("ClusterName", clusterName);
        command.Parameters.AddWithValue("NodeId", nodeId.Value);

        var value = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new NodeIncarnation(value);
    }

    protected override async ValueTask UpsertDescriptorCoreAsync(
        string clusterName,
        NodeDescriptor descriptor,
        CancellationToken cancellationToken
    )
    {
        var descriptorTable = _Qualified(MembershipSchema.Descriptor.Table);
        var sql = $$"""
            INSERT INTO {{descriptorTable}} (
                [{{MembershipSchema.ClusterName}}],
                [{{MembershipSchema.NodeId}}],
                [{{MembershipSchema.Incarnation}}],
                [{{MembershipSchema.Descriptor.HostName}}],
                [{{MembershipSchema.Descriptor.Endpoints}}],
                [{{MembershipSchema.Descriptor.Role}}],
                [{{MembershipSchema.Descriptor.Metadata}}],
                [{{MembershipSchema.CreatedAt}}]
            )
            SELECT
                @ClusterName,
                @NodeId,
                @Incarnation,
                @HostName,
                @Endpoints,
                @Role,
                @Metadata,
                SYSUTCDATETIME()
            WHERE NOT EXISTS (
                SELECT 1
                FROM {{descriptorTable}} WITH (UPDLOCK, HOLDLOCK)
                WHERE [{{MembershipSchema.ClusterName}}] = @ClusterName
                  AND [{{MembershipSchema.NodeId}}] = @NodeId
                  AND [{{MembershipSchema.Incarnation}}] = @Incarnation
            );
            """;

        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = _CreateCommand(connection, sql);
        command.Parameters.AddWithValue("ClusterName", clusterName);
        command.Parameters.AddWithValue("NodeId", descriptor.Identity.NodeId.Value);
        command.Parameters.AddWithValue("Incarnation", descriptor.Identity.Incarnation.Value);
        command.Parameters.AddWithValue("HostName", (object?)descriptor.HostName ?? DBNull.Value);
        command.Parameters.AddWithValue("Endpoints", _SerializeDictionary(descriptor.Endpoints));
        command.Parameters.AddWithValue("Role", (object?)descriptor.Role ?? DBNull.Value);
        command.Parameters.AddWithValue("Metadata", _SerializeDictionary(descriptor.Metadata));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async ValueTask<bool> HeartbeatCoreAsync(
        string clusterName,
        NodeIdentity identity,
        CancellationToken cancellationToken
    )
    {
        var generationTable = _Qualified(MembershipSchema.Generation.Table);
        var livenessTable = _Qualified(MembershipSchema.Liveness.Table);
        var sql = $$"""
            DECLARE @currentIncarnation bigint;

            SELECT @currentIncarnation = [{{MembershipSchema.Generation.CurrentIncarnation}}]
            FROM {{generationTable}} WITH (UPDLOCK, HOLDLOCK)
            WHERE [{{MembershipSchema.ClusterName}}] = @ClusterName
              AND [{{MembershipSchema.NodeId}}] = @NodeId;

            IF @currentIncarnation IS NULL OR @currentIncarnation <> @Incarnation
            BEGIN
                SELECT CAST(0 AS bit);
                RETURN;
            END;

            UPDATE {{livenessTable}} WITH (UPDLOCK, HOLDLOCK)
            SET [{{MembershipSchema.Liveness.LastBeat}}] = SYSUTCDATETIME(),
                [{{MembershipSchema.Liveness.LeftAt}}] = NULL
            WHERE [{{MembershipSchema.ClusterName}}] = @ClusterName
              AND [{{MembershipSchema.NodeId}}] = @NodeId
              AND [{{MembershipSchema.Incarnation}}] = @Incarnation;

            IF @@ROWCOUNT = 0
            BEGIN
                INSERT INTO {{livenessTable}} (
                    [{{MembershipSchema.ClusterName}}],
                    [{{MembershipSchema.NodeId}}],
                    [{{MembershipSchema.Incarnation}}],
                    [{{MembershipSchema.Liveness.LastBeat}}],
                    [{{MembershipSchema.Liveness.LeftAt}}]
                )
                SELECT @ClusterName, @NodeId, @Incarnation, SYSUTCDATETIME(), NULL
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM {{livenessTable}} WITH (UPDLOCK, HOLDLOCK)
                    WHERE [{{MembershipSchema.ClusterName}}] = @ClusterName
                      AND [{{MembershipSchema.NodeId}}] = @NodeId
                      AND [{{MembershipSchema.Incarnation}}] = @Incarnation
                );
            END;

            SELECT CAST(1 AS bit);
            """;

        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqlTransaction)await connection
            .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);
        await using var command = _CreateCommand(connection, sql, transaction);
        command.Parameters.AddWithValue("ClusterName", clusterName);
        command.Parameters.AddWithValue("NodeId", identity.NodeId.Value);
        command.Parameters.AddWithValue("Incarnation", identity.Incarnation.Value);

        var accepted = (bool)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        await _PruneExpiredRowsAsync(connection, clusterName, cancellationToken).ConfigureAwait(false);

        return accepted;
    }

    protected override async ValueTask LeaveCoreAsync(
        string clusterName,
        NodeIdentity identity,
        CancellationToken cancellationToken
    )
    {
        var livenessTable = _Qualified(MembershipSchema.Liveness.Table);
        var sql = $$"""
            UPDATE {{livenessTable}}
            SET [{{MembershipSchema.Liveness.LeftAt}}] = SYSUTCDATETIME()
            WHERE [{{MembershipSchema.ClusterName}}] = @ClusterName
              AND [{{MembershipSchema.NodeId}}] = @NodeId
              AND [{{MembershipSchema.Incarnation}}] = @Incarnation;
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
        var generationTable = _Qualified(MembershipSchema.Generation.Table);
        var descriptorTable = _Qualified(MembershipSchema.Descriptor.Table);
        var livenessTable = _Qualified(MembershipSchema.Liveness.Table);
        var sql = $$"""
            SELECT
                l.[{{MembershipSchema.NodeId}}],
                l.[{{MembershipSchema.Incarnation}}],
                d.[{{MembershipSchema.Descriptor.Role}}],
                d.[{{MembershipSchema.Descriptor.Metadata}}],
                CASE
                    WHEN l.[{{MembershipSchema.Liveness.LeftAt}}] IS NOT NULL THEN @DeadState
                    WHEN DATEDIFF_BIG(millisecond, l.[{{MembershipSchema.Liveness.LastBeat}}], SYSUTCDATETIME()) >= @DeadThresholdMs THEN @DeadState
                    WHEN DATEDIFF_BIG(millisecond, l.[{{MembershipSchema.Liveness.LastBeat}}], SYSUTCDATETIME()) >= @SuspicionThresholdMs THEN @SuspectedState
                    ELSE @AliveState
                END AS [state]
            FROM {{livenessTable}} l
            JOIN {{generationTable}} g
              ON g.[{{MembershipSchema.ClusterName}}] = l.[{{MembershipSchema.ClusterName}}]
             AND g.[{{MembershipSchema.NodeId}}] = l.[{{MembershipSchema.NodeId}}]
             AND g.[{{MembershipSchema.Generation.CurrentIncarnation}}] = l.[{{MembershipSchema.Incarnation}}]
            LEFT JOIN {{descriptorTable}} d
              ON d.[{{MembershipSchema.ClusterName}}] = l.[{{MembershipSchema.ClusterName}}]
             AND d.[{{MembershipSchema.NodeId}}] = l.[{{MembershipSchema.NodeId}}]
             AND d.[{{MembershipSchema.Incarnation}}] = l.[{{MembershipSchema.Incarnation}}]
            WHERE l.[{{MembershipSchema.ClusterName}}] = @ClusterName
            ORDER BY l.[{{MembershipSchema.NodeId}}], l.[{{MembershipSchema.Incarnation}}];
            """;

        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await _PruneExpiredRowsAsync(connection, clusterName, cancellationToken).ConfigureAwait(false);
        await using var command = _CreateCommand(connection, sql);
        command.Parameters.AddWithValue("ClusterName", clusterName);
        command.Parameters.AddWithValue("DeadThresholdMs", _ToMilliseconds(DeadThreshold));
        command.Parameters.AddWithValue("SuspicionThresholdMs", _ToMilliseconds(SuspicionThreshold));
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
        SqlConnection connection,
        string clusterName,
        CancellationToken cancellationToken
    )
    {
        var descriptorTable = _Qualified(MembershipSchema.Descriptor.Table);
        var livenessTable = _Qualified(MembershipSchema.Liveness.Table);
        var sql = $$"""
            DELETE FROM {{livenessTable}}
            WHERE [{{MembershipSchema.ClusterName}}] = @ClusterName
              AND DATEDIFF_BIG(millisecond, [{{MembershipSchema.Liveness.LastBeat}}], SYSUTCDATETIME()) >= @RetentionThresholdMs;

            DELETE d
            FROM {{descriptorTable}} d
            WHERE d.[{{MembershipSchema.ClusterName}}] = @ClusterName
              AND DATEDIFF_BIG(millisecond, d.[{{MembershipSchema.CreatedAt}}], SYSUTCDATETIME()) >= @RetentionThresholdMs
              AND NOT EXISTS (
                  SELECT 1
                  FROM {{livenessTable}} l
                  WHERE l.[{{MembershipSchema.ClusterName}}] = d.[{{MembershipSchema.ClusterName}}]
                    AND l.[{{MembershipSchema.NodeId}}] = d.[{{MembershipSchema.NodeId}}]
                    AND l.[{{MembershipSchema.Incarnation}}] = d.[{{MembershipSchema.Incarnation}}]
              );
            """;

        await using var command = _CreateCommand(connection, sql);
        command.Parameters.AddWithValue("ClusterName", clusterName);
        command.Parameters.AddWithValue("RetentionThresholdMs", _ToMilliseconds(DeadThreshold + DeadRetentionWindow));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private SqlCommand _CreateCommand(SqlConnection connection, string commandText, SqlTransaction? transaction = null)
    {
        return new SqlCommand(commandText, connection, transaction)
        {
            CommandType = CommandType.Text,
            CommandTimeout = _GetCommandTimeoutSeconds(providerOptions.Value.CommandTimeout),
        };
    }

    private string _Qualified(string table)
    {
        return SqlServerCoordinationIdentifier.Qualified(providerOptions.Value.Schema, table);
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

    private static long _ToMilliseconds(TimeSpan value)
    {
        return (long)Math.Ceiling(value.TotalMilliseconds);
    }

    private static int _GetCommandTimeoutSeconds(TimeSpan timeout)
    {
        return timeout.TotalSeconds >= int.MaxValue ? int.MaxValue : Math.Max(1, (int)Math.Ceiling(timeout.TotalSeconds));
    }
}
#pragma warning restore CA2100
