// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Headless.Coordination.SqlServer;

#pragma warning disable CA2100 // SQL text is built from validated schema plus internal table constants.
internal sealed class SqlServerMembershipStore(
    IOptions<SqlServerCoordinationOptions> providerOptions,
    IOptions<CoordinationOptions> coordinationOptions
) : DatabaseMembershipStoreBase(coordinationOptions.Value)
{
    protected override async ValueTask<NodeIncarnation> AllocateIncarnationCoreAsync(
        string clusterName,
        NodeId nodeId,
        CancellationToken cancellationToken
    )
    {
        var options = providerOptions.Value;
        var generationTable = _Qualified(SqlServerMembershipSchema.Generation.Table);
        var sql = $$"""
            DECLARE @allocated table ([value] bigint NOT NULL);

            UPDATE {{generationTable}} WITH (UPDLOCK, HOLDLOCK)
            SET [{{SqlServerMembershipSchema.Generation.CurrentIncarnation}}] = [{{SqlServerMembershipSchema.Generation.CurrentIncarnation}}] + 1,
                [{{SqlServerMembershipSchema.DateUpdated}}] = SYSUTCDATETIME()
            OUTPUT inserted.[{{SqlServerMembershipSchema.Generation.CurrentIncarnation}}] INTO @allocated
            WHERE [{{SqlServerMembershipSchema.ClusterName}}] = @ClusterName
              AND [{{SqlServerMembershipSchema.NodeId}}] = @NodeId;

            IF NOT EXISTS (SELECT 1 FROM @allocated)
            BEGIN
                BEGIN TRY
                    INSERT INTO {{generationTable}} (
                        [{{SqlServerMembershipSchema.ClusterName}}],
                        [{{SqlServerMembershipSchema.NodeId}}],
                        [{{SqlServerMembershipSchema.Generation.CurrentIncarnation}}],
                        [{{SqlServerMembershipSchema.DateUpdated}}]
                    )
                    VALUES (@ClusterName, @NodeId, 1, SYSUTCDATETIME());

                    INSERT INTO @allocated ([value]) VALUES (1);
                END TRY
                BEGIN CATCH
                    IF ERROR_NUMBER() NOT IN (2601, 2627) THROW;

                    UPDATE {{generationTable}} WITH (UPDLOCK, HOLDLOCK)
                    SET [{{SqlServerMembershipSchema.Generation.CurrentIncarnation}}] = [{{SqlServerMembershipSchema.Generation.CurrentIncarnation}}] + 1,
                        [{{SqlServerMembershipSchema.DateUpdated}}] = SYSUTCDATETIME()
                    OUTPUT inserted.[{{SqlServerMembershipSchema.Generation.CurrentIncarnation}}] INTO @allocated
                    WHERE [{{SqlServerMembershipSchema.ClusterName}}] = @ClusterName
                      AND [{{SqlServerMembershipSchema.NodeId}}] = @NodeId;
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
        var generationTable = _Qualified(SqlServerMembershipSchema.Generation.Table);
        var descriptorTable = _Qualified(SqlServerMembershipSchema.Descriptor.Table);
        var livenessTable = _Qualified(SqlServerMembershipSchema.Liveness.Table);

        // Register writes both rows under one generation lock: the write-once cold descriptor and an
        // initial Alive liveness row stamped with the store clock. Both are gated on the freshly-allocated
        // incarnation still being current, so a stale/impossible incarnation establishes neither.
        var sql = $$"""
            DECLARE @currentIncarnation bigint;

            SELECT @currentIncarnation = [{{SqlServerMembershipSchema.Generation.CurrentIncarnation}}]
            FROM {{generationTable}} WITH (UPDLOCK, HOLDLOCK)
            WHERE [{{SqlServerMembershipSchema.ClusterName}}] = @ClusterName
              AND [{{SqlServerMembershipSchema.NodeId}}] = @NodeId;

            IF @currentIncarnation IS NULL OR @currentIncarnation <> @Incarnation
            BEGIN
                RETURN;
            END;

            INSERT INTO {{descriptorTable}} (
                [{{SqlServerMembershipSchema.ClusterName}}],
                [{{SqlServerMembershipSchema.NodeId}}],
                [{{SqlServerMembershipSchema.Incarnation}}],
                [{{SqlServerMembershipSchema.Descriptor.HostName}}],
                [{{SqlServerMembershipSchema.Descriptor.Endpoints}}],
                [{{SqlServerMembershipSchema.Descriptor.Role}}],
                [{{SqlServerMembershipSchema.Descriptor.Metadata}}],
                [{{SqlServerMembershipSchema.DateCreated}}]
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
                WHERE [{{SqlServerMembershipSchema.ClusterName}}] = @ClusterName
                  AND [{{SqlServerMembershipSchema.NodeId}}] = @NodeId
                  AND [{{SqlServerMembershipSchema.Incarnation}}] = @Incarnation
            );

            UPDATE {{livenessTable}} WITH (UPDLOCK, HOLDLOCK)
            SET [{{SqlServerMembershipSchema.Liveness.LastBeat}}] = SYSUTCDATETIME(),
                [{{SqlServerMembershipSchema.Liveness.LeftAt}}] = NULL
            WHERE [{{SqlServerMembershipSchema.ClusterName}}] = @ClusterName
              AND [{{SqlServerMembershipSchema.NodeId}}] = @NodeId
              AND [{{SqlServerMembershipSchema.Incarnation}}] = @Incarnation;

            IF @@ROWCOUNT = 0
            BEGIN
                INSERT INTO {{livenessTable}} (
                    [{{SqlServerMembershipSchema.ClusterName}}],
                    [{{SqlServerMembershipSchema.NodeId}}],
                    [{{SqlServerMembershipSchema.Incarnation}}],
                    [{{SqlServerMembershipSchema.Liveness.LastBeat}}],
                    [{{SqlServerMembershipSchema.Liveness.LeftAt}}]
                )
                SELECT @ClusterName, @NodeId, @Incarnation, SYSUTCDATETIME(), NULL
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM {{livenessTable}} WITH (UPDLOCK, HOLDLOCK)
                    WHERE [{{SqlServerMembershipSchema.ClusterName}}] = @ClusterName
                      AND [{{SqlServerMembershipSchema.NodeId}}] = @NodeId
                      AND [{{SqlServerMembershipSchema.Incarnation}}] = @Incarnation
                );
            END;
            """;

        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqlTransaction)await connection
            .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);
        await using var command = _CreateCommand(connection, sql, transaction);
        command.Parameters.AddWithValue("ClusterName", clusterName);
        command.Parameters.AddWithValue("NodeId", descriptor.Identity.NodeId.Value);
        command.Parameters.AddWithValue("Incarnation", descriptor.Identity.Incarnation.Value);
        command.Parameters.AddWithValue("HostName", (object?)descriptor.HostName ?? DBNull.Value);
        command.Parameters.AddWithValue("Endpoints", SerializeDictionary(descriptor.Endpoints));
        command.Parameters.AddWithValue("Role", (object?)descriptor.Role ?? DBNull.Value);
        command.Parameters.AddWithValue("Metadata", SerializeDictionary(descriptor.Metadata));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async ValueTask<bool> HeartbeatCoreAsync(
        string clusterName,
        NodeIdentity identity,
        CancellationToken cancellationToken
    )
    {
        var generationTable = _Qualified(SqlServerMembershipSchema.Generation.Table);
        var livenessTable = _Qualified(SqlServerMembershipSchema.Liveness.Table);
        var sql = $$"""
            DECLARE @currentIncarnation bigint;

            SELECT @currentIncarnation = [{{SqlServerMembershipSchema.Generation.CurrentIncarnation}}]
            FROM {{generationTable}} WITH (UPDLOCK, HOLDLOCK)
            WHERE [{{SqlServerMembershipSchema.ClusterName}}] = @ClusterName
              AND [{{SqlServerMembershipSchema.NodeId}}] = @NodeId;

            IF @currentIncarnation IS NULL OR @currentIncarnation <> @Incarnation
            BEGIN
                SELECT CAST(0 AS bit);
                RETURN;
            END;

            UPDATE {{livenessTable}} WITH (UPDLOCK, HOLDLOCK)
            SET [{{SqlServerMembershipSchema.Liveness.LastBeat}}] = SYSUTCDATETIME(),
                [{{SqlServerMembershipSchema.Liveness.LeftAt}}] = NULL
            WHERE [{{SqlServerMembershipSchema.ClusterName}}] = @ClusterName
              AND [{{SqlServerMembershipSchema.NodeId}}] = @NodeId
              AND [{{SqlServerMembershipSchema.Incarnation}}] = @Incarnation;

            IF @@ROWCOUNT = 0
            BEGIN
                INSERT INTO {{livenessTable}} (
                    [{{SqlServerMembershipSchema.ClusterName}}],
                    [{{SqlServerMembershipSchema.NodeId}}],
                    [{{SqlServerMembershipSchema.Incarnation}}],
                    [{{SqlServerMembershipSchema.Liveness.LastBeat}}],
                    [{{SqlServerMembershipSchema.Liveness.LeftAt}}]
                )
                SELECT @ClusterName, @NodeId, @Incarnation, SYSUTCDATETIME(), NULL
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM {{livenessTable}} WITH (UPDLOCK, HOLDLOCK)
                    WHERE [{{SqlServerMembershipSchema.ClusterName}}] = @ClusterName
                      AND [{{SqlServerMembershipSchema.NodeId}}] = @NodeId
                      AND [{{SqlServerMembershipSchema.Incarnation}}] = @Incarnation
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

        // Retention pruning runs once per tick on the read path; the heartbeat path no longer prunes.
        return accepted;
    }

    protected override async ValueTask LeaveCoreAsync(
        string clusterName,
        NodeIdentity identity,
        CancellationToken cancellationToken
    )
    {
        var livenessTable = _Qualified(SqlServerMembershipSchema.Liveness.Table);
        var sql = $"""
            UPDATE {livenessTable}
            SET [{SqlServerMembershipSchema.Liveness.LeftAt}] = SYSUTCDATETIME()
            WHERE [{SqlServerMembershipSchema.ClusterName}] = @ClusterName
              AND [{SqlServerMembershipSchema.NodeId}] = @NodeId
              AND [{SqlServerMembershipSchema.Incarnation}] = @Incarnation;
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
        var generationTable = _Qualified(SqlServerMembershipSchema.Generation.Table);
        var descriptorTable = _Qualified(SqlServerMembershipSchema.Descriptor.Table);
        var livenessTable = _Qualified(SqlServerMembershipSchema.Liveness.Table);
        var sql = $$"""
            SELECT
                l.[{{SqlServerMembershipSchema.NodeId}}],
                l.[{{SqlServerMembershipSchema.Incarnation}}],
                d.[{{SqlServerMembershipSchema.Descriptor.Role}}],
                d.[{{SqlServerMembershipSchema.Descriptor.Metadata}}],
                CASE
                    WHEN l.[{{SqlServerMembershipSchema.Liveness.LeftAt}}] IS NOT NULL THEN @DeadState
                    WHEN DATEDIFF_BIG(millisecond, l.[{{SqlServerMembershipSchema.Liveness.LastBeat}}], SYSUTCDATETIME()) >= @DeadThresholdMs THEN @DeadState
                    WHEN DATEDIFF_BIG(millisecond, l.[{{SqlServerMembershipSchema.Liveness.LastBeat}}], SYSUTCDATETIME()) >= @SuspicionThresholdMs THEN @SuspectedState
                    ELSE @AliveState
                END AS [state]
            FROM {{livenessTable}} l
            JOIN {{generationTable}} g
              ON g.[{{SqlServerMembershipSchema.ClusterName}}] = l.[{{SqlServerMembershipSchema.ClusterName}}]
             AND g.[{{SqlServerMembershipSchema.NodeId}}] = l.[{{SqlServerMembershipSchema.NodeId}}]
             AND g.[{{SqlServerMembershipSchema.Generation.CurrentIncarnation}}] = l.[{{SqlServerMembershipSchema.Incarnation}}]
            LEFT JOIN {{descriptorTable}} d
              ON d.[{{SqlServerMembershipSchema.ClusterName}}] = l.[{{SqlServerMembershipSchema.ClusterName}}]
             AND d.[{{SqlServerMembershipSchema.NodeId}}] = l.[{{SqlServerMembershipSchema.NodeId}}]
             AND d.[{{SqlServerMembershipSchema.Incarnation}}] = l.[{{SqlServerMembershipSchema.Incarnation}}]
            WHERE l.[{{SqlServerMembershipSchema.ClusterName}}] = @ClusterName
            ORDER BY l.[{{SqlServerMembershipSchema.NodeId}}], l.[{{SqlServerMembershipSchema.Incarnation}}];
            """;

        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        // Pruning is best-effort cleanup; do not abort it on the caller's read cancellation.
        await _PruneExpiredRowsAsync(connection, clusterName, CancellationToken.None).ConfigureAwait(false);
        await using var command = _CreateCommand(connection, sql);
        command.Parameters.AddWithValue("ClusterName", clusterName);
        command.Parameters.AddWithValue("DeadThresholdMs", _ToMilliseconds(DeadThreshold));
        command.Parameters.AddWithValue("SuspicionThresholdMs", _ToMilliseconds(SuspicionThreshold));
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
        SqlConnection connection,
        string clusterName,
        CancellationToken cancellationToken
    )
    {
        var descriptorTable = _Qualified(SqlServerMembershipSchema.Descriptor.Table);
        var livenessTable = _Qualified(SqlServerMembershipSchema.Liveness.Table);
        var sql = $$"""
            DELETE FROM {{livenessTable}}
            WHERE [{{SqlServerMembershipSchema.ClusterName}}] = @ClusterName
              AND DATEDIFF_BIG(millisecond, [{{SqlServerMembershipSchema.Liveness.LastBeat}}], SYSUTCDATETIME()) >= @RetentionThresholdMs;

            DELETE d
            FROM {{descriptorTable}} d
            WHERE d.[{{SqlServerMembershipSchema.ClusterName}}] = @ClusterName
              AND DATEDIFF_BIG(millisecond, d.[{{SqlServerMembershipSchema.DateCreated}}], SYSUTCDATETIME()) >= @RetentionThresholdMs
              AND NOT EXISTS (
                  SELECT 1
                  FROM {{livenessTable}} l
                  WHERE l.[{{SqlServerMembershipSchema.ClusterName}}] = d.[{{SqlServerMembershipSchema.ClusterName}}]
                    AND l.[{{SqlServerMembershipSchema.NodeId}}] = d.[{{SqlServerMembershipSchema.NodeId}}]
                    AND l.[{{SqlServerMembershipSchema.Incarnation}}] = d.[{{SqlServerMembershipSchema.Incarnation}}]
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
            CommandTimeout = DatabaseAdoHelpers.GetCommandTimeoutSeconds(providerOptions.Value.CommandTimeout),
        };
    }

    private string _Qualified(string table)
    {
        return SqlServerCoordinationIdentifier.Qualified(providerOptions.Value.Schema, table);
    }

    private static long _ToMilliseconds(TimeSpan value)
    {
        return (long)Math.Ceiling(value.TotalMilliseconds);
    }
}
#pragma warning restore CA2100
