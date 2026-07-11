// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.Serializer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace Headless.Coordination.SqlServer;

#pragma warning disable CA2100 // SQL text is built from validated schema plus internal table constants.
internal sealed class SqlServerMembershipStore(
    IOptions<SqlServerCoordinationOptions> providerOptions,
    IOptions<CoordinationOptions> coordinationOptions,
    [FromKeyedServices(CoordinationOptions.JsonSerializerServiceKey)] IJsonSerializer serializer,
    TimeProvider timeProvider,
    ILogger<SqlServerMembershipStore> logger
) : DatabaseMembershipStoreBase(coordinationOptions.Value, serializer)
{
    private const int _MaxDeadlockRetryAttempts = 2;
    private readonly ResiliencePipeline _deadlockRetryPipeline = _BuildDeadlockRetryPipeline(timeProvider, logger);

    // Membership SQL is invariant after construction (the schema is fixed in IOptions and the store is a DI
    // singleton), so the per-tick paths (heartbeat, liveness reads, retention prune) precompute their command
    // text once instead of rebuilding ~40-line strings every beat. The lifecycle paths (allocate/upsert/leave)
    // run once per node and keep building inline.
    private readonly string _heartbeatSql = _BuildHeartbeatSql(providerOptions.Value.Schema);
    private readonly string _readLivenessSql = _BuildReadLivenessSql(providerOptions.Value.Schema);
    private readonly string _readNodeLivenessSql = _BuildReadNodeLivenessSql(providerOptions.Value.Schema);
    private readonly string _readLiveNodesSql = _BuildReadLiveNodesSql(providerOptions.Value.Schema);
    private readonly string _pruneExpiredRowsSql = _BuildPruneExpiredRowsSql(providerOptions.Value.Schema);

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

        return await _ExecuteWithDeadlockRetryAsync(
                "AllocateIncarnation",
                async ct =>
                {
                    await using var connection = options.CreateConnection();
                    await connection.OpenAsync(ct).ConfigureAwait(false);
                    await using var transaction = (SqlTransaction)
                        await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct).ConfigureAwait(false);
                    await using var command = _CreateCommand(connection, sql, transaction);
                    command.Parameters.AddWithValue("ClusterName", clusterName);
                    command.Parameters.AddWithValue("NodeId", nodeId.Value);

                    var value = Convert.ToInt64(
                        await command.ExecuteScalarAsync(ct).ConfigureAwait(false),
                        CultureInfo.InvariantCulture
                    );
                    await transaction.CommitAsync(ct).ConfigureAwait(false);

                    return new NodeIncarnation(value);
                },
                cancellationToken
            )
            .ConfigureAwait(false);
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

        await _ExecuteWithDeadlockRetryAsync(
                "UpsertDescriptor",
                async ct =>
                {
                    await using var connection = providerOptions.Value.CreateConnection();
                    await connection.OpenAsync(ct).ConfigureAwait(false);
                    await using var transaction = (SqlTransaction)
                        await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct).ConfigureAwait(false);
                    await using var command = _CreateCommand(connection, sql, transaction);
                    command.Parameters.AddWithValue("ClusterName", clusterName);
                    command.Parameters.AddWithValue("NodeId", descriptor.Identity.NodeId.Value);
                    command.Parameters.AddWithValue("Incarnation", descriptor.Identity.Incarnation.Value);
                    command.Parameters.AddWithValue("HostName", (object?)descriptor.HostName ?? DBNull.Value);
                    command.Parameters.AddWithValue("Endpoints", SerializeDictionary(descriptor.Endpoints));
                    command.Parameters.AddWithValue("Role", (object?)descriptor.Role ?? DBNull.Value);
                    command.Parameters.AddWithValue("Metadata", SerializeDictionary(descriptor.Metadata));

                    await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    await transaction.CommitAsync(ct).ConfigureAwait(false);
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    protected override async ValueTask<bool> HeartbeatCoreAsync(
        string clusterName,
        NodeIdentity identity,
        CancellationToken cancellationToken
    )
    {
        return await _ExecuteWithDeadlockRetryAsync(
                "Heartbeat",
                async ct =>
                {
                    await using var connection = providerOptions.Value.CreateConnection();
                    await connection.OpenAsync(ct).ConfigureAwait(false);
                    await using var transaction = (SqlTransaction)
                        await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct).ConfigureAwait(false);
                    await using var command = _CreateCommand(connection, _heartbeatSql, transaction);
                    command.Parameters.AddWithValue("ClusterName", clusterName);
                    command.Parameters.AddWithValue("NodeId", identity.NodeId.Value);
                    command.Parameters.AddWithValue("Incarnation", identity.Incarnation.Value);
                    command.Parameters.AddWithValue("DeadThresholdMs", _ToMilliseconds(DeadThreshold));

                    var accepted = (bool)await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
                    await transaction.CommitAsync(ct).ConfigureAwait(false);

                    // Retention pruning runs once per tick on the read path; the heartbeat path no longer prunes.
                    return accepted;
                },
                cancellationToken
            )
            .ConfigureAwait(false);
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
        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // Pruning is best-effort cleanup: isolate its failure (for example a deadlock victim, error 1205) so
        // the liveness read for this tick still returns instead of being discarded along with the prune. The
        // next read tick retries the prune. Not cancelled on the caller's read token.
        try
        {
            await _PruneExpiredRowsAsync(connection, clusterName, CancellationToken.None).ConfigureAwait(false);
        }
        catch (SqlException ex)
        {
            logger.LogSqlServerMembershipPruneFailed(ex);
        }

        await using var command = _CreateCommand(connection, _readLivenessSql);
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
        command.Parameters.AddWithValue("DeadThresholdMs", _ToMilliseconds(DeadThreshold));
        command.Parameters.AddWithValue("SuspicionThresholdMs", _ToMilliseconds(SuspicionThreshold));
        command.Parameters.AddWithValue("RetentionThresholdMs", _ToMilliseconds(DeadThreshold + DeadRetentionWindow));
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
        command.Parameters.AddWithValue("SuspicionThresholdMs", _ToMilliseconds(SuspicionThreshold));

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
        SqlConnection connection,
        string clusterName,
        CancellationToken cancellationToken
    )
    {
        await using var command = _CreateCommand(connection, _pruneExpiredRowsSql);
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

    private static string _BuildHeartbeatSql(string schema)
    {
        var generationTable = SqlServerCoordinationIdentifier.Qualified(
            schema,
            SqlServerMembershipSchema.Generation.Table
        );
        var livenessTable = SqlServerCoordinationIdentifier.Qualified(schema, SqlServerMembershipSchema.Liveness.Table);

        return $$"""
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
            SET [{{SqlServerMembershipSchema.Liveness.LastBeat}}] = SYSUTCDATETIME()
            WHERE [{{SqlServerMembershipSchema.ClusterName}}] = @ClusterName
              AND [{{SqlServerMembershipSchema.NodeId}}] = @NodeId
              AND [{{SqlServerMembershipSchema.Incarnation}}] = @Incarnation
              AND [{{SqlServerMembershipSchema.Liveness.LeftAt}}] IS NULL
              AND DATEDIFF_BIG(
                    millisecond,
                    [{{SqlServerMembershipSchema.Liveness.LastBeat}}],
                    SYSUTCDATETIME()
                  ) < @DeadThresholdMs;

            SELECT CAST(CASE WHEN @@ROWCOUNT = 1 THEN 1 ELSE 0 END AS bit);
            """;
    }

    private static string _BuildReadLivenessSql(string schema)
    {
        var generationTable = SqlServerCoordinationIdentifier.Qualified(
            schema,
            SqlServerMembershipSchema.Generation.Table
        );
        var descriptorTable = SqlServerCoordinationIdentifier.Qualified(
            schema,
            SqlServerMembershipSchema.Descriptor.Table
        );
        var livenessTable = SqlServerCoordinationIdentifier.Qualified(schema, SqlServerMembershipSchema.Liveness.Table);

        return $$"""
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
    }

    private static string _BuildReadNodeLivenessSql(string schema)
    {
        var generationTable = SqlServerCoordinationIdentifier.Qualified(
            schema,
            SqlServerMembershipSchema.Generation.Table
        );
        var livenessTable = SqlServerCoordinationIdentifier.Qualified(schema, SqlServerMembershipSchema.Liveness.Table);

        // Targeted single-row read: join to the generation authority so a non-current incarnation yields no
        // row (absent -> null), classify with the store clock identically to ReadCurrentLivenessCoreAsync, and
        // exclude retention-expired rows in the WHERE so they read as absent (null) exactly as the snapshot's
        // prune would remove them. Plain read: no SERIALIZABLE transaction, no deadlock retry, no prune.
        return $$"""
            SELECT
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
            WHERE l.[{{SqlServerMembershipSchema.ClusterName}}] = @ClusterName
              AND l.[{{SqlServerMembershipSchema.NodeId}}] = @NodeId
              AND l.[{{SqlServerMembershipSchema.Incarnation}}] = @Incarnation
              AND DATEDIFF_BIG(millisecond, l.[{{SqlServerMembershipSchema.Liveness.LastBeat}}], SYSUTCDATETIME()) < @RetentionThresholdMs;
            """;
    }

    private static string _BuildReadLiveNodesSql(string schema)
    {
        var generationTable = SqlServerCoordinationIdentifier.Qualified(
            schema,
            SqlServerMembershipSchema.Generation.Table
        );
        var livenessTable = SqlServerCoordinationIdentifier.Qualified(schema, SqlServerMembershipSchema.Liveness.Table);

        // Alive-only, current-generation, identities only: join the generation authority so superseded
        // incarnations are excluded, keep rows not left and whose store-clock beat age is below the suspicion
        // threshold. No descriptor join, no prune; the base orders the result.
        return $$"""
            SELECT l.[{{SqlServerMembershipSchema.NodeId}}], l.[{{SqlServerMembershipSchema.Incarnation}}]
            FROM {{livenessTable}} l
            JOIN {{generationTable}} g
              ON g.[{{SqlServerMembershipSchema.ClusterName}}] = l.[{{SqlServerMembershipSchema.ClusterName}}]
             AND g.[{{SqlServerMembershipSchema.NodeId}}] = l.[{{SqlServerMembershipSchema.NodeId}}]
             AND g.[{{SqlServerMembershipSchema.Generation.CurrentIncarnation}}] = l.[{{SqlServerMembershipSchema.Incarnation}}]
            WHERE l.[{{SqlServerMembershipSchema.ClusterName}}] = @ClusterName
              AND l.[{{SqlServerMembershipSchema.Liveness.LeftAt}}] IS NULL
              AND DATEDIFF_BIG(millisecond, l.[{{SqlServerMembershipSchema.Liveness.LastBeat}}], SYSUTCDATETIME()) < @SuspicionThresholdMs;
            """;
    }

    private static string _BuildPruneExpiredRowsSql(string schema)
    {
        var descriptorTable = SqlServerCoordinationIdentifier.Qualified(
            schema,
            SqlServerMembershipSchema.Descriptor.Table
        );
        var livenessTable = SqlServerCoordinationIdentifier.Qualified(schema, SqlServerMembershipSchema.Liveness.Table);

        return $$"""
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
    }

    private static long _ToMilliseconds(TimeSpan value)
    {
        return (long)Math.Ceiling(value.TotalMilliseconds);
    }

    private async ValueTask _ExecuteWithDeadlockRetryAsync(
        string operationName,
        Func<CancellationToken, ValueTask> action,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await _deadlockRetryPipeline
                .ExecuteAsync(
                    static async (action, ct) => await action(ct).ConfigureAwait(false),
                    action,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        catch (SqlException ex) when (_IsDeadlockVictim(ex))
        {
            logger.LogSqlServerMembershipDeadlock(operationName, ex);

            throw;
        }
    }

    private async ValueTask<TResult> _ExecuteWithDeadlockRetryAsync<TResult>(
        string operationName,
        Func<CancellationToken, ValueTask<TResult>> action,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await _deadlockRetryPipeline
                .ExecuteAsync(
                    static async (action, ct) => await action(ct).ConfigureAwait(false),
                    action,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        catch (SqlException ex) when (_IsDeadlockVictim(ex))
        {
            logger.LogSqlServerMembershipDeadlock(operationName, ex);

            throw;
        }
    }

    private static ResiliencePipeline _BuildDeadlockRetryPipeline(TimeProvider timeProvider, ILogger logger)
    {
        return new ResiliencePipelineBuilder { TimeProvider = timeProvider }
            .AddRetry(
                new RetryStrategyOptions
                {
                    ShouldHandle = static args => new ValueTask<bool>(
                        args.Outcome.Exception is SqlException ex && _IsDeadlockVictim(ex)
                    ),
                    MaxRetryAttempts = _MaxDeadlockRetryAttempts,
                    BackoffType = DelayBackoffType.Exponential,
                    Delay = TimeSpan.FromMilliseconds(50),
                    MaxDelay = TimeSpan.FromMilliseconds(500),
                    UseJitter = true,
                    OnRetry = args =>
                    {
                        logger.LogSqlServerMembershipDeadlockRetry(
                            args.AttemptNumber + 1,
                            _MaxDeadlockRetryAttempts + 1,
                            args.RetryDelay,
                            args.Outcome.Exception
                        );

                        return default;
                    },
                }
            )
            .Build();
    }

    private static bool _IsDeadlockVictim(SqlException exception)
    {
        return exception.Number == 1205;
    }
}
#pragma warning restore CA2100

internal static partial class SqlServerMembershipStoreLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "SqlServerMembershipDeadlockRetry",
        Level = LogLevel.Warning,
        Message = "SQL Server coordination membership write hit deadlock victim error 1205; retrying attempt {AttemptNumber}/{MaxAttempts} after {Delay}."
    )]
    public static partial void LogSqlServerMembershipDeadlockRetry(
        this ILogger logger,
        int attemptNumber,
        int maxAttempts,
        TimeSpan delay,
        Exception? exception
    );

    [LoggerMessage(
        EventId = 2,
        EventName = "SqlServerMembershipDeadlock",
        Level = LogLevel.Error,
        Message = "SQL Server coordination membership operation {OperationName} exhausted deadlock victim retries."
    )]
    public static partial void LogSqlServerMembershipDeadlock(
        this ILogger logger,
        string operationName,
        Exception exception
    );

    [LoggerMessage(
        EventId = 3,
        EventName = "SqlServerMembershipPruneFailed",
        Level = LogLevel.Warning,
        Message = "SQL Server coordination retention prune failed; the liveness read for this tick still returns and the next tick retries the prune."
    )]
    public static partial void LogSqlServerMembershipPruneFailed(this ILogger logger, Exception exception);
}
