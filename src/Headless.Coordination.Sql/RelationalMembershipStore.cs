// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Checks;
using Headless.Serializer;
using Headless.Sql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Coordination;

/// <summary>
/// Provides the relational membership store, written once against <see cref="ISqlDialect" />. Every time
/// comparison runs on the database clock inside the statement that decides; the application clock never
/// classifies a node.
/// </summary>
/// <remarks>
/// <para>
/// The generation row is the incarnation authority. Allocation is one upsert of it; registration and
/// heartbeats first take its update-intent lock, so neither can interleave with an allocation that supersedes
/// the incarnation they write for. A heartbeat is then one fenced liveness update whose <c>WHERE</c> carries
/// the whole guard — current incarnation, not left, beat younger than the dead threshold — and it writes the
/// later of the stored beat and the clock, so a database clock that steps back never moves a beat backwards.
/// </para>
/// <para>
/// Every call runs on its own connection and READ COMMITTED transaction, and retries a transient fault raised
/// before the commit (see <see cref="SqlAutonomousTransaction" />). Every read treats a row at or past the
/// retention cutoff as absent, so what a read returns never depends on when rows were last deleted. The snapshot
/// read also prunes those rows, at most once a minute, in a transaction of its own whose failure is logged and
/// left to a later read.
/// </para>
/// </remarks>
#pragma warning disable CA2100 // SQL text is rendered once from validated identifiers and dialect statements; values are parameters.
internal sealed class RelationalMembershipStore : IMembershipStore
{
    private const int _Alive = (int)NodeLivenessState.Alive;
    private const int _Suspected = (int)NodeLivenessState.Suspected;
    private const int _Dead = (int)NodeLivenessState.Dead;

    private readonly RelationalCoordinationStorage _storage;
    private readonly ISqlDialect _dialect;
    private readonly CoordinationOptions _options;
    private readonly IJsonSerializer _serializer;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RelationalMembershipStore> _logger;
    private readonly string _allocateSql;
    private readonly string _lockGenerationSql;
    private readonly string _insertDescriptorSql;
    private readonly string _upsertLivenessSql;
    private readonly string _heartbeatSql;
    private readonly string _leaveSql;
    private readonly string[] _pruneSql;
    private readonly string _readLivenessSql;
    private readonly string _readNodeLivenessSql;
    private readonly string _readLiveNodesSql;

    public RelationalMembershipStore(
        RelationalCoordinationStorage storage,
        IOptions<CoordinationOptions> options,
        [FromKeyedServices(CoordinationOptions.JsonSerializerServiceKey)] IJsonSerializer serializer,
        TimeProvider timeProvider,
        ILogger<RelationalMembershipStore> logger
    )
    {
        _storage = storage;
        _dialect = storage.Dialect;
        _options = options.Value;
        _serializer = serializer;
        _timeProvider = timeProvider;
        _logger = logger;

        const string now = SqlDialectTokens.Now;
        const string stored = SqlDialectTokens.Stored;
        var t = storage.Tables;
        var dialect = _dialect;

        _allocateSql = dialect.Render(
            new SqlUpsert(
                t.Generation,
                t.NodeKey,
                [t.CurrentIncarnation, t.UpdatedAt],
                ["1", now],
                $"{t.CurrentIncarnation} = {stored}.{t.CurrentIncarnation} + 1, {t.UpdatedAt} = {now}",
                Guard: null,
                [t.CurrentIncarnation]
            )
        );

        _lockGenerationSql = dialect.Render(new SqlLockedRead(t.Generation, t.NodeKey, [t.CurrentIncarnation]));

        // Write-once: a descriptor already stored for the incarnation keeps its first content.
        _insertDescriptorSql = dialect.Render(
            new SqlInsertIfAbsent(
                t.Descriptor,
                t.IncarnationKey,
                [t.HostName, t.Endpoints, t.Role, t.Metadata, t.CreatedAt],
                ["@HostName", "@Endpoints", "@Role", "@Metadata", now],
                [t.CreatedAt]
            )
        );

        // Registering again revives the incarnation's liveness, but never moves its beat backwards.
        _upsertLivenessSql = dialect.Render(
            new SqlUpsert(
                t.Liveness,
                t.IncarnationKey,
                [t.LastBeat, t.LeftAt],
                [now, "NULL"],
                $"{t.LastBeat} = {_Later($"{stored}.{t.LastBeat}", now)}, {t.LeftAt} = NULL",
                Guard: null,
                [t.LastBeat]
            )
        );

        _heartbeatSql =
            _lockGenerationSql
            + dialect.Render(
                new SqlFencedTransition(
                    t.Liveness,
                    t.IncarnationKey,
                    Fence: $"""
                    {t.LeftAt} IS NULL
                        AND {t.LastBeat} > {dialect.ShiftByDuration(now, "DeadThreshold", subtract: true)}
                        AND EXISTS (
                            SELECT 1 FROM {t.Generation} g
                            WHERE g.{t.ClusterName} = @ClusterName
                              AND g.{t.NodeId} = @NodeId
                              AND g.{t.CurrentIncarnation} = @Incarnation
                        )
                    """,
                    Set: $"{t.LastBeat} = {_Later(t.LastBeat, now)}",
                    Returning: [t.LastBeat]
                )
            );

        // A left incarnation keeps the instant it first left; a repeated leave is a no-op.
        _leaveSql = dialect.Render(
            new SqlClockedStatement(
                $"""
                UPDATE {t.Liveness}
                SET {t.LeftAt} = {now}
                WHERE {t.ClusterName} = @ClusterName
                  AND {t.NodeId} = @NodeId
                  AND {t.Incarnation} = @Incarnation
                  AND {t.LeftAt} IS NULL
                """
            )
        );

        var retentionCutoff = dialect.ShiftByDuration(now, "Retention", subtract: true);

        // Two statements in one transaction, the second seeing the first's deletes: a descriptor goes once its
        // incarnation has no liveness row left. The outer table is named in full, because DELETE takes no alias on
        // both engines alike.
        var pruneLiveness = dialect.Render(
            new SqlClockedStatement(
                $"""
                DELETE FROM {t.Liveness}
                WHERE {t.ClusterName} = @ClusterName
                  AND {t.LastBeat} <= {retentionCutoff}
                """
            )
        );
        var pruneDescriptors = dialect.Render(
            new SqlClockedStatement(
                $"""
                DELETE FROM {t.Descriptor}
                WHERE {t.ClusterName} = @ClusterName
                  AND {t.CreatedAt} <= {retentionCutoff}
                  AND NOT EXISTS (
                      SELECT 1
                      FROM {t.Liveness} l
                      WHERE l.{t.ClusterName} = {t.Descriptor}.{t.ClusterName}
                        AND l.{t.NodeId} = {t.Descriptor}.{t.NodeId}
                        AND l.{t.Incarnation} = {t.Descriptor}.{t.Incarnation}
                  )
                """
            )
        );

        _pruneSql = [pruneLiveness, pruneDescriptors];

        var state = $"""
            CASE
                    WHEN l.{t.LeftAt} IS NOT NULL THEN {_Dead}
                    WHEN l.{t.LastBeat} <= {dialect.ShiftByDuration(now, "DeadThreshold", subtract: true)} THEN {_Dead}
                    WHEN l.{t.LastBeat} <= {dialect.ShiftByDuration(
                now,
                "SuspicionThreshold",
                subtract: true
            )} THEN {_Suspected}
                    ELSE {_Alive}
                END
            """;
        var currentGeneration = $"""
            JOIN {t.Generation} g
                  ON g.{t.ClusterName} = l.{t.ClusterName}
                 AND g.{t.NodeId} = l.{t.NodeId}
                 AND g.{t.CurrentIncarnation} = l.{t.Incarnation}
            """;

        _readLivenessSql = dialect.Render(
            new SqlClockedStatement(
                $"""
                SELECT l.{t.NodeId}, l.{t.Incarnation}, d.{t.Role}, d.{t.Metadata}, {state}
                FROM {t.Liveness} l
                {currentGeneration}
                LEFT JOIN {t.Descriptor} d
                  ON d.{t.ClusterName} = l.{t.ClusterName}
                 AND d.{t.NodeId} = l.{t.NodeId}
                 AND d.{t.Incarnation} = l.{t.Incarnation}
                WHERE l.{t.ClusterName} = @ClusterName
                  AND l.{t.LastBeat} > {retentionCutoff}
                """
            )
        );

        // Read-only: a row at or past the retention cutoff reads as absent, as it does in the snapshot, without
        // pruning it here.
        _readNodeLivenessSql = dialect.Render(
            new SqlClockedStatement(
                $"""
                SELECT {state}
                FROM {t.Liveness} l
                {currentGeneration}
                WHERE l.{t.ClusterName} = @ClusterName
                  AND l.{t.NodeId} = @NodeId
                  AND l.{t.Incarnation} = @Incarnation
                  AND l.{t.LastBeat} > {retentionCutoff}
                """
            )
        );

        _readLiveNodesSql = dialect.Render(
            new SqlClockedStatement(
                $"""
                SELECT l.{t.NodeId}, l.{t.Incarnation}
                FROM {t.Liveness} l
                {currentGeneration}
                WHERE l.{t.ClusterName} = @ClusterName
                  AND l.{t.LeftAt} IS NULL
                  AND l.{t.LastBeat} > {dialect.ShiftByDuration(now, "SuspicionThreshold", subtract: true)}
                """
            )
        );
    }

    public ValueTask<NodeIncarnation> AllocateIncarnationAsync(
        NodeId nodeId,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        return _RunAsync(
            "coordination.allocate_incarnation",
            async (connection, transaction, ct) =>
            {
                await using var command = _Command(_allocateSql, connection, transaction);
                _AddNode(command, nodeId);

                var upserted = await _ExecuteAsync(
                        () =>
                            SqlUpsertCommand.ExecuteAsync(
                                command,
                                static (reader, _) => ValueTask.FromResult(reader.GetInt64(1)),
                                ct
                            ),
                        ct
                    )
                    .ConfigureAwait(false);

                return upserted.Match(
                    static (value, _) => new NodeIncarnation(value),
                    static () =>
                        throw new InvalidOperationException("An unguarded generation upsert reported a refusal.")
                );
            },
            cancellationToken
        );
    }

    public async ValueTask UpsertDescriptorAsync(
        NodeDescriptor descriptor,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        Argument.IsNotNullOrEmpty(descriptor.Identity.NodeId.Value, "Descriptor identity must include a node id.");

        var endpoints = _Serialize(descriptor.Endpoints);
        var metadata = _Serialize(descriptor.Metadata);

        await _RunAsync(
                "coordination.upsert_descriptor",
                async (connection, transaction, ct) =>
                {
                    // The generation lock is held to the end of the transaction, so the incarnation checked here stays
                    // current until both rows are written; a stale or impossible incarnation establishes neither.
                    await using (var locked = _Command(_lockGenerationSql, connection, transaction))
                    {
                        _AddNode(locked, descriptor.Identity.NodeId);

                        var current = await _ExecuteAsync(() => locked.ExecuteScalarAsync(ct), ct)
                            .ConfigureAwait(false);

                        if (current is not long incarnation || incarnation != descriptor.Identity.Incarnation.Value)
                        {
                            return false;
                        }
                    }

                    await using (var insert = _Command(_insertDescriptorSql, connection, transaction))
                    {
                        _AddIncarnation(insert, descriptor.Identity);
                        _dialect.AddParameter(insert, "HostName", SqlColumnType.Text(-1), descriptor.HostName);
                        _dialect.AddParameter(insert, "Endpoints", SqlColumnType.Json, endpoints);
                        _dialect.AddParameter(
                            insert,
                            "Role",
                            SqlColumnType.Text(CoordinationTables.RoleMaxLength),
                            descriptor.Role
                        );
                        _dialect.AddParameter(insert, "Metadata", SqlColumnType.Json, metadata);
                        await _ExecuteAsync(() => insert.ExecuteNonQueryAsync(ct), ct).ConfigureAwait(false);
                    }

                    await using var liveness = _Command(_upsertLivenessSql, connection, transaction);
                    _AddIncarnation(liveness, descriptor.Identity);
                    await _ExecuteAsync(() => liveness.ExecuteNonQueryAsync(ct), ct).ConfigureAwait(false);

                    return true;
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public ValueTask<bool> HeartbeatAsync(NodeIdentity identity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return _RunAsync(
            "coordination.heartbeat",
            async (connection, transaction, ct) =>
            {
                await using var command = _Command(_heartbeatSql, connection, transaction);
                _AddIncarnation(command, identity);
                _dialect.AddDuration(command, "DeadThreshold", _options.DeadThreshold);

                var beat = await _ExecuteAsync(
                        () =>
                            SqlFencedCommand.ExecuteAsync(
                                command,
                                lockedRead: true,
                                static (reader, _) => ValueTask.FromResult(new LockedGeneration(reader.GetInt64(0))),
                                static (reader, token) =>
                                    new ValueTask<DateTimeOffset>(reader.GetFieldValueAsync<DateTimeOffset>(1, token)),
                                ct
                            ),
                        ct
                    )
                    .ConfigureAwait(false);

                // The fence re-checks the generation itself, so a refusal needs no classification: superseded, left,
                // dead, and pruned all mean the incarnation is over.
                return beat.Match(static (_, _) => true, static _ => false);
            },
            cancellationToken
        );
    }

    public ValueTask LeaveAsync(NodeIdentity identity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return _ExecuteNonQueryAsync("coordination.leave", _leaveSql, identity, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<NodeLivenessSnapshot>> ReadLivenessAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _PruneAsync().ConfigureAwait(false);

        var snapshots = await _RunAsync(
                "coordination.read_liveness",
                async (connection, transaction, ct) =>
                {
                    await using var command = _Command(_readLivenessSql, connection, transaction);
                    _AddThresholds(command);
                    // The prune is throttled, so this cutoff, not the prune, keeps expired rows out of the snapshot.
                    _dialect.AddDuration(command, "Retention", RetentionThreshold);

                    var rows = new List<NodeLivenessSnapshot>();
                    await using var reader = await _ExecuteAsync(() => command.ExecuteReaderAsync(ct), ct)
                        .ConfigureAwait(false);

                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        rows.Add(await _ReadSnapshotAsync(reader, ct).ConfigureAwait(false));
                    }

                    return rows;
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        // Ordered here rather than in SQL: identities compare as "node@incarnation" strings, ordinally, on every store.
        return snapshots.OrderBy(static snapshot => snapshot.Identity.ToString(), StringComparer.Ordinal).ToArray();
    }

    public ValueTask<NodeLivenessState?> ReadNodeLivenessAsync(
        NodeIdentity identity,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        return _RunAsync(
            "coordination.read_node_liveness",
            async (connection, transaction, ct) =>
            {
                await using var command = _Command(_readNodeLivenessSql, connection, transaction);
                _AddIncarnation(command, identity);
                _AddThresholds(command);
                _dialect.AddDuration(command, "Retention", RetentionThreshold);

                var state = await _ExecuteAsync(() => command.ExecuteScalarAsync(ct), ct).ConfigureAwait(false);

                return state is null or DBNull ? (NodeLivenessState?)null : _State(state);
            },
            cancellationToken
        );
    }

    public async ValueTask<IReadOnlyList<NodeIdentity>> ReadLiveNodesAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var identities = await _RunAsync(
                "coordination.read_live_nodes",
                async (connection, transaction, ct) =>
                {
                    await using var command = _Command(_readLiveNodesSql, connection, transaction);
                    _dialect.AddDuration(command, "SuspicionThreshold", _options.SuspicionThreshold);

                    var rows = new List<NodeIdentity>();
                    await using var reader = await _ExecuteAsync(() => command.ExecuteReaderAsync(ct), ct)
                        .ConfigureAwait(false);

                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        rows.Add(
                            new NodeIdentity(new NodeId(reader.GetString(0)), new NodeIncarnation(reader.GetInt64(1)))
                        );
                    }

                    return rows;
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        // Same order as ReadLivenessAsync, whose Alive entries this set must equal.
        return identities.OrderBy(static identity => identity.ToString(), StringComparer.Ordinal).ToArray();
    }

    private TimeSpan RetentionThreshold => _options.DeadThreshold + _options.DeadRetentionWindow;

    // The prune removes rows older than the retention threshold, so running it more often than a fraction of
    // that window cannot remove anything new; it ran as two DELETE transactions before EVERY liveness read
    // (every heartbeat of every node). The Redis provider moved its prune to a background service for the
    // same reason; here a time-based throttle keeps it on the read path without per-tick transactions.
    private long _lastPruneTicks;

    // Deterministic seam for the membership oracle, which compares the stored rows after every step and so needs
    // each snapshot read to prune. Production keeps the one-minute throttle.
    internal TimeSpan MinPruneInterval { get; set; } = TimeSpan.FromMinutes(1);

    private async ValueTask _PruneAsync()
    {
        var now = DateTimeOffset.UtcNow;

        if (now.UtcTicks - Volatile.Read(ref _lastPruneTicks) < MinPruneInterval.Ticks)
        {
            return;
        }

        // Best-effort cleanup the next read retries: not cancelled with the caller's read, and a failure that outlives
        // the deadlock retries is logged instead of failing the read it precedes.
        try
        {
            await _RunAsync(
                    "coordination.prune",
                    async (connection, transaction, ct) =>
                    {
                        foreach (var sql in _pruneSql)
                        {
                            await using var command = _Command(sql, connection, transaction);
                            _dialect.AddDuration(command, "Retention", RetentionThreshold);
                            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                        }

                        return true;
                    },
                    CancellationToken.None
                )
                .ConfigureAwait(false);

            Volatile.Write(ref _lastPruneTicks, now.UtcTicks);
        }
        catch (DbException ex)
        {
            _logger.LogMembershipPruneFailed(ex);
        }
    }

    private async ValueTask _ExecuteNonQueryAsync(
        string operation,
        string sql,
        NodeIdentity identity,
        CancellationToken cancellationToken
    )
    {
        await _RunAsync(
                operation,
                async (connection, transaction, ct) =>
                {
                    await using var command = _Command(sql, connection, transaction);
                    _AddIncarnation(command, identity);

                    return await _ExecuteAsync(() => command.ExecuteNonQueryAsync(ct), ct).ConfigureAwait(false);
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private ValueTask<T> _RunAsync<T>(
        string operation,
        Func<DbConnection, DbTransaction, CancellationToken, Task<T>> body,
        CancellationToken cancellationToken
    )
    {
        return SqlAutonomousTransaction.RunAsync(
            operation,
            _storage.CreateConnection,
            body,
            _timeProvider,
            cancellationToken
        );
    }

    private DbCommand _Command(string sql, DbConnection connection, DbTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        command.CommandTimeout = _storage.CommandTimeoutSeconds;
        _dialect.AddParameter(
            command,
            "ClusterName",
            SqlColumnType.KeyText(CoordinationTables.ClusterNameMaxLength),
            _options.ClusterName
        );

        return command;
    }

    private void _AddNode(DbCommand command, NodeId nodeId)
    {
        _dialect.AddParameter(
            command,
            "NodeId",
            SqlColumnType.KeyText(CoordinationTables.NodeIdMaxLength),
            nodeId.Value
        );
    }

    private void _AddIncarnation(DbCommand command, NodeIdentity identity)
    {
        _AddNode(command, identity.NodeId);
        _dialect.AddParameter(command, "Incarnation", SqlColumnType.Int64, identity.Incarnation.Value);
    }

    private void _AddThresholds(DbCommand command)
    {
        _dialect.AddDuration(command, "DeadThreshold", _options.DeadThreshold);
        _dialect.AddDuration(command, "SuspicionThreshold", _options.SuspicionThreshold);
    }

    private async ValueTask<NodeLivenessSnapshot> _ReadSnapshotAsync(
        DbDataReader reader,
        CancellationToken cancellationToken
    )
    {
        var identity = new NodeIdentity(new NodeId(reader.GetString(0)), new NodeIncarnation(reader.GetInt64(1)));
        var role = await reader.IsDBNullAsync(2, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(2);
        var metadata = await reader.IsDBNullAsync(3, cancellationToken).ConfigureAwait(false)
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : _Deserialize(reader.GetString(3));

        return new NodeLivenessSnapshot(identity, _State(reader.GetValue(4)), role, metadata);
    }

    private string _Serialize(IReadOnlyDictionary<string, string> value)
    {
        return _serializer.SerializeToString(value) ?? "{}";
    }

    private Dictionary<string, string> _Deserialize(string value)
    {
        return _serializer.Deserialize<Dictionary<string, string>>(value)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    // The state is an integer literal in the CASE: int on both engines, but read through Convert so a driver that
    // widens it still maps.
    private static NodeLivenessState _State(object value)
    {
        return (NodeLivenessState)Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static string _Later(string stored, string now)
    {
        // CASE rather than GREATEST, which SQL Server only has from 2022.
        return $"CASE WHEN {stored} > {now} THEN {stored} ELSE {now} END";
    }

    private static async Task<T> _ExecuteAsync<T>(Func<Task<T>> execute, CancellationToken cancellationToken)
    {
        try
        {
            return await execute().ConfigureAwait(false);
        }
        // SqlClient reports a command it cancelled mid-flight as a provider error; surface the cancellation asked for.
        catch (DbException ex) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(ex.Message, ex, cancellationToken);
        }
    }

    private sealed record LockedGeneration(long CurrentIncarnation);
}
#pragma warning restore CA2100

internal static partial class RelationalMembershipStoreLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "MembershipPruneFailed",
        Level = LogLevel.Warning,
        Message = "Coordination membership retention prune failed; the liveness read still returns and the next read retries the prune."
    )]
    public static partial void LogMembershipPruneFailed(this ILogger logger, Exception exception);
}
