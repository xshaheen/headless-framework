// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Primitives;
using Headless.Sql;

namespace Headless.Messaging.Persistence;

#pragma warning disable CA2100 // SQL text is rendered from dialect output, table names, and fixed fragments; every value is a parameter.
#pragma warning disable CA1849, VSTHRD103, AsyncFixer02, MA0042 // Buffered row reads cannot add blocking I/O.

internal sealed partial class RelationalDataStorage
{
    private const int _ActorMaxLength = 200;
    private const int _ReasonMaxLength = 1000;
    private const int _ResourceMaxLength = 255;
    private const string _InboxTargetKind = "Inbox";
    private const string _ScheduledTargetKind = "ScheduledDelivery";

    public async ValueTask<IndexPage<InboxGenerationView>> QueryAsync(
        InboxGenerationQuery query,
        OperatorAuthorizationContext authorization,
        CancellationToken cancellationToken = default
    )
    {
        authorization.Validate();
        var page = Math.Max(query.CurrentPage, 0);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var where = $"{_t.IsInboxRecord} = {_t.True}";
        if (query.IncarnationId is not null)
        {
            where += $" AND {_t.GenerationIncarnationId}=@IncarnationId";
        }

        if (!string.IsNullOrEmpty(query.ConsumerIdentity))
        {
            // The equality seeks; the LIKE of the escaped value keeps the match exact on an engine whose equality
            // ignores trailing spaces.
            where +=
                $" AND {_t.ConsumerIdentity}=@ConsumerIdentity AND {_t.ConsumerIdentity} LIKE @ConsumerIdentityPattern ESCAPE '\\'";
        }

        if (query.Lane is not null)
        {
            where += $" AND {_t.IntentType}=@IntentType";
        }

        if (query.Status is not null)
        {
            where += $" AND {_t.StatusName}=@StatusName";
        }

        if (query.IsOrphaned is not null)
        {
            where += $" AND {_t.IsInboxOrphaned}=@IsOrphaned";
        }

        if (query.IsHeld is not null)
        {
            where += $" AND {_t.IsHeld}=@IsHeld";
        }

        void bind(DbCommand command)
        {
            _dialect.AddParameter(command, "IncarnationId", SqlColumnType.Guid, query.IncarnationId);
            _dialect.AddParameter(
                command,
                "ConsumerIdentity",
                SqlColumnType.KeyText(_NameMaxLength),
                query.ConsumerIdentity
            );
            _dialect.AddParameter(
                command,
                "ConsumerIdentityPattern",
                SqlColumnType.KeyText(_Unbounded),
                query.ConsumerIdentity is null ? null : RelationalMonitoringApi.EscapeLike(query.ConsumerIdentity)
            );
            _dialect.AddParameter(
                command,
                "IntentType",
                SqlColumnType.Int16,
                query.Lane is null ? null : MessageLaneCompatibility.ToPersistedValue(query.Lane.Value)
            );
            _dialect.AddParameter(command, "StatusName", _StatusType, query.Status?.ToString());
            _dialect.AddParameter(command, "IsOrphaned", SqlColumnType.Boolean, query.IsOrphaned);
            _dialect.AddParameter(command, "IsHeld", SqlColumnType.Boolean, query.IsHeld);
        }

        await using var connection = _CreateConnection();
        var total = Convert.ToInt64(
            await RelationalCommand
                .ExecuteScalarAsync(
                    connection,
                    transaction: null,
                    $"SELECT COUNT(*) FROM {_receivedTable} WHERE {where};",
                    CommandTimeoutSeconds,
                    bind,
                    cancellationToken
                )
                .ConfigureAwait(false),
            CultureInfo.InvariantCulture
        );

        var items = await RelationalCommand
            .ExecuteReaderAsync(
                connection,
                transaction: null,
                $"""
                SELECT {_t.Id},{_t.GenerationIncarnationId},{_t.Generation},{_t.TenantPresent},{_t.TenantId},{_t.MessageId},{_t.IntentType},
                       {_t.ContractIdentity},{_t.ContractVersion},{_t.ConsumerIdentity},{_t.StatusName},{_t.IsCurrentGeneration},
                       {_t.IsInboxOrphaned},{_t.ReplayParentIncarnationId},{_t.ReplayOperationId},{_t.TerminalAt},{_t.EffectiveExpiresAt},
                       {_t.IsHeld},{_t.HeldAt},{_t.HeldBy},{_t.HoldReason}
                FROM {_receivedTable}
                WHERE {where}
                ORDER BY {_t.Added} DESC,{_t.Id}
                {_dialect.Limit("Limit", "Offset")};
                """,
                CommandTimeoutSeconds,
                command =>
                {
                    bind(command);
                    _dialect.AddParameter(command, "Offset", SqlColumnType.Int64, (long)page * pageSize);
                    _dialect.AddParameter(command, "Limit", SqlColumnType.Int32, pageSize);
                },
                static async (reader, ct) =>
                {
                    var views = new List<InboxGenerationView>();
                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        views.Add(
                            new InboxGenerationView(
                                reader.GetGuid(0),
                                reader.GetGuid(1),
                                reader.GetInt64(2),
                                reader.GetBoolean(3) ? reader.GetString(4) : null,
                                reader.GetString(5),
                                MessageLaneCompatibility.FromPersistedValue(reader.GetInt16(6)),
                                reader.GetString(7),
                                reader.GetString(8),
                                reader.GetString(9),
                                Enum.Parse<StatusName>(reader.GetString(10)),
                                reader.GetBoolean(11),
                                reader.GetBoolean(12),
                                reader.IsDBNull(13) ? null : reader.GetGuid(13),
                                reader.IsDBNull(14) ? null : reader.GetGuid(14),
                                reader.IsDBNull(15) ? null : reader.GetFieldValue<DateTimeOffset>(15),
                                reader.IsDBNull(16) ? null : reader.GetFieldValue<DateTimeOffset>(16),
                                reader.GetBoolean(17),
                                reader.IsDBNull(18) ? null : reader.GetFieldValue<DateTimeOffset>(18),
                                reader.IsDBNull(19) ? null : reader.GetString(19),
                                reader.IsDBNull(20) ? null : reader.GetString(20)
                            )
                        );
                    }

                    return views;
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        return new IndexPage<InboxGenerationView>(items, page, pageSize, checked((int)total));
    }

    public ValueTask<InboxOperationResult> HoldAsync(
        InboxOperationRequest request,
        CancellationToken cancellationToken = default
    ) => _ExecuteInboxOperationAsync(MessagingOperationType.Hold, request, cancellationToken);

    public ValueTask<InboxOperationResult> ReleaseHoldAsync(
        InboxOperationRequest request,
        CancellationToken cancellationToken = default
    ) => _ExecuteInboxOperationAsync(MessagingOperationType.ReleaseHold, request, cancellationToken);

    public ValueTask<InboxOperationResult> ForceReprocessAsync(
        InboxOperationRequest request,
        CancellationToken cancellationToken = default
    ) => _ExecuteInboxOperationAsync(MessagingOperationType.ForceReprocess, request, cancellationToken);

    public ValueTask<InboxOperationResult> PurgeAsync(
        InboxOperationRequest request,
        CancellationToken cancellationToken = default
    ) => _ExecuteInboxOperationAsync(MessagingOperationType.Purge, request, cancellationToken);

    /// <summary>
    /// Runs one audited inbox operation. The operation id's lock serializes concurrent requests that share it, so a
    /// retried request replays its stored receipt instead of applying twice, and a different request under a used id
    /// is refused as a conflict.
    /// </summary>
    private async ValueTask<InboxOperationResult> _ExecuteInboxOperationAsync(
        MessagingOperationType operationType,
        InboxOperationRequest request,
        CancellationToken cancellationToken
    )
    {
        request.Validate();

        var (result, row) = await SqlAutonomousTransaction
            .RunAsync(
                _CreateConnection,
                async (connection, transaction, ct) =>
                {
                    await _LockOperationAsync(connection, transaction, request.OperationId, ct).ConfigureAwait(false);

                    var prior = await _ReadInboxReceiptAsync(connection, transaction, request.OperationId, ct)
                        .ConfigureAwait(false);
                    if (prior is not null)
                    {
                        var replay = _ReplayOrConflict(prior, operationType, request);
                        if (replay.Outcome is InboxOperationOutcome.OperationConflict)
                        {
                            await _WriteConflictAuditAsync(
                                    connection,
                                    transaction,
                                    _InboxTargetKind,
                                    replay.OperationId,
                                    replay.ExpectedIncarnationId,
                                    replay.OperationType,
                                    replay.Actor,
                                    replay.Reason,
                                    replay.Outcome,
                                    ct
                                )
                                .ConfigureAwait(false);
                        }

                        return (replay, (InboxOperationRow?)null);
                    }

                    // The generation is locked before the clock is read, so whether its claim is live, the hold
                    // stamp, and a replay's due time are all decided on a clock read after any wait for that lock.
                    var row = await _ReadInboxOperationRowAsync(
                            connection,
                            transaction,
                            request.ExpectedIncarnationId,
                            ct
                        )
                        .ConfigureAwait(false);
                    var now = await _ReadNowAsync(connection, transaction, ct).ConfigureAwait(false);
                    row = row is null
                        ? null
                        : row with
                        {
                            State = row.State with { HasLiveClaim = row.LockedUntil > now },
                        };
                    var outcome = MessagingOperationEvaluator.Evaluate(
                        operationType,
                        request.ExpectedStatus,
                        row?.State
                    );
                    Guid? childStorageId = null;
                    long? childGeneration = null;
                    Guid? childIncarnationId = null;

                    if (outcome is InboxOperationOutcome.Applied && row is not null)
                    {
                        switch (operationType)
                        {
                            case MessagingOperationType.Hold:
                                await _ExecuteInboxMutationAsync(
                                        connection,
                                        transaction,
                                        $"UPDATE {_receivedTable} SET {_t.IsHeld}={_t.True},{_t.HeldAt}=@Instant,{_t.HeldBy}=@Actor,{_t.HoldReason}=@Reason,{_t.HoldOperationId}=@OperationId WHERE {_t.GenerationIncarnationId}=@IncarnationId;",
                                        request,
                                        now,
                                        ct
                                    )
                                    .ConfigureAwait(false);
                                break;
                            case MessagingOperationType.ReleaseHold:
                                await _ExecuteInboxMutationAsync(
                                        connection,
                                        transaction,
                                        $"UPDATE {_receivedTable} SET {_t.IsHeld}={_t.False},{_t.HeldAt}=NULL,{_t.HeldBy}=NULL,{_t.HoldReason}=NULL,{_t.HoldOperationId}=@OperationId WHERE {_t.GenerationIncarnationId}=@IncarnationId;",
                                        request,
                                        now,
                                        ct
                                    )
                                    .ConfigureAwait(false);
                                break;
                            case MessagingOperationType.ForceReprocess:
                                childStorageId = _guidGenerator.Create();
                                childIncarnationId = _guidGenerator.Create();
                                childGeneration = checked(row.State.Generation + 1);
                                await _CreateReplayChildAsync(
                                        connection,
                                        transaction,
                                        row,
                                        request,
                                        childStorageId.Value,
                                        childIncarnationId.Value,
                                        childGeneration.Value,
                                        now,
                                        ct
                                    )
                                    .ConfigureAwait(false);
                                break;
                            case MessagingOperationType.Purge:
                                await _ExecuteInboxMutationAsync(
                                        connection,
                                        transaction,
                                        $"DELETE FROM {_receivedTable} WHERE {_t.GenerationIncarnationId}=@IncarnationId;",
                                        request,
                                        now,
                                        ct
                                    )
                                    .ConfigureAwait(false);
                                break;
                        }
                    }

                    var applied = new InboxOperationResult(
                        request.OperationId,
                        operationType,
                        outcome,
                        request.ExpectedIncarnationId,
                        request.ExpectedStatus,
                        row?.StorageId,
                        childStorageId,
                        childGeneration,
                        childIncarnationId,
                        request.Actor,
                        request.Reason,
                        now
                    );
                    await _WriteInboxReceiptAndAuditAsync(connection, transaction, applied, ct).ConfigureAwait(false);

                    return (applied, row);
                },
                _timeProvider,
                cancellationToken
            )
            .ConfigureAwait(false);

        _RecordInboxOperation(row, operationType, result.Outcome);
        return result;
    }

    private void _RecordInboxOperation(
        InboxOperationRow? row,
        MessagingOperationType operationType,
        InboxOperationOutcome outcome
    )
    {
        if (row is null || outcome is not InboxOperationOutcome.Applied)
        {
            return;
        }

        var kind =
            operationType is MessagingOperationType.ForceReprocess ? InboxMetricKind.Replay : InboxMetricKind.Retention;
        var metricOutcome = operationType switch
        {
            MessagingOperationType.Hold => InboxMetricOutcome.Held,
            MessagingOperationType.ReleaseHold => InboxMetricOutcome.Released,
            MessagingOperationType.ForceReprocess => InboxMetricOutcome.Replayed,
            MessagingOperationType.Purge => InboxMetricOutcome.Purged,
            _ => throw new ArgumentOutOfRangeException(nameof(operationType), operationType, message: null),
        };
        MessagingMetrics.RecordInbox(
            kind,
            row.ConsumerIdentity,
            row.Lane,
            metricOutcome,
            Options.RequiredInboxCapability,
            _storage.ProviderName
        );
    }

    private static InboxOperationResult _ReplayOrConflict(
        InboxOperationResult prior,
        MessagingOperationType operationType,
        InboxOperationRequest request
    )
    {
        var matches =
            prior.OperationType == operationType
            && prior.ExpectedIncarnationId == request.ExpectedIncarnationId
            && prior.ExpectedStatus == request.ExpectedStatus
            && string.Equals(prior.Actor, request.Actor, StringComparison.Ordinal)
            && string.Equals(prior.Reason, request.Reason, StringComparison.Ordinal);
        return matches
            ? prior with
            {
                IsReplay = true,
            }
            : new InboxOperationResult(
                request.OperationId,
                operationType,
                InboxOperationOutcome.OperationConflict,
                request.ExpectedIncarnationId,
                request.ExpectedStatus,
                StorageId: null,
                ChildStorageId: null,
                ChildGeneration: null,
                ChildIncarnationId: null,
                request.Actor,
                request.Reason,
                prior.CreatedAt,
                IsReplay: true
            );
    }

    /// <summary>
    /// Takes the lock every request under <paramref name="operationId"/> shares, until the transaction ends. The
    /// receipt cleanup takes it too, so a receipt is never deleted under a request replaying it.
    /// </summary>
    private async Task _LockOperationAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid operationId,
        CancellationToken cancellationToken
    )
    {
        await RelationalCommand
            .ExecuteNonQueryAsync(
                connection,
                transaction,
                _dialect.Render(new SqlTransactionLock("Resource")),
                CommandTimeoutSeconds,
                command =>
                    _dialect.AddParameter(
                        command,
                        "Resource",
                        SqlColumnType.Text(_ResourceMaxLength),
                        $"headless.messaging.inbox.operation.{operationId:D}"
                    ),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private async Task<InboxOperationResult?> _ReadInboxReceiptAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid operationId,
        CancellationToken cancellationToken
    )
    {
        var sql = _dialect.Render(
            new SqlLockedRead(
                _t.Receipts,
                [new SqlKeyColumn(_t.OperationId, "OperationId")],
                [
                    _t.GenerationIncarnationId,
                    _t.OperationType,
                    _t.ExpectedStatus,
                    _t.Actor,
                    _t.Reason,
                    _t.Outcome,
                    _t.StorageId,
                    _t.ChildStorageId,
                    _t.ChildGeneration,
                    _t.ChildIncarnationId,
                    _t.CreatedAt,
                    _t.TargetKind,
                ]
            )
        );

        return await RelationalCommand
            .ExecuteReaderAsync(
                connection,
                transaction,
                sql,
                CommandTimeoutSeconds,
                command => _dialect.AddParameter(command, "OperationId", SqlColumnType.Guid, operationId),
                async (reader, ct) =>
                {
                    if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        return null;
                    }

                    var isInbox = string.Equals(reader.GetString(11), _InboxTargetKind, StringComparison.Ordinal);
                    var incarnationId = isInbox && !reader.IsDBNull(0) ? reader.GetGuid(0) : Guid.Empty;
                    var expectedStatus =
                        isInbox && !reader.IsDBNull(2) ? Enum.Parse<StatusName>(reader.GetString(2)) : default;

                    return new InboxOperationResult(
                        operationId,
                        Enum.Parse<MessagingOperationType>(reader.GetString(1)),
                        Enum.Parse<InboxOperationOutcome>(reader.GetString(5)),
                        incarnationId,
                        expectedStatus,
                        reader.IsDBNull(6) ? null : reader.GetGuid(6),
                        reader.IsDBNull(7) ? null : reader.GetGuid(7),
                        reader.IsDBNull(8) ? null : reader.GetInt64(8),
                        reader.IsDBNull(9) ? null : reader.GetGuid(9),
                        reader.GetString(3),
                        reader.GetString(4),
                        reader.GetFieldValue<DateTimeOffset>(10)
                    );
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private async Task<InboxOperationRow?> _ReadInboxOperationRowAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid incarnationId,
        CancellationToken cancellationToken
    )
    {
        var sql = _dialect.Render(
            new SqlLockedRead(
                _receivedTable,
                [new SqlKeyColumn(_t.GenerationIncarnationId, "IncarnationId")],
                [
                    _t.Id,
                    _t.StatusName,
                    _t.NextRetryAt,
                    _t.IsHeld,
                    _t.IsCurrentGeneration,
                    _t.Generation,
                    _t.IntentType,
                    _t.ConsumerIdentity,
                    _t.IsInboxOrphaned,
                    _t.LockedUntil,
                    _t.TenantPresent,
                    _t.TenantId,
                    _t.MessageId,
                    _t.ContractIdentity,
                    _t.ContractVersion,
                    _t.LifecycleId,
                ],
                $"{_t.IsInboxRecord} = {_t.True}"
            )
        );

        return await RelationalCommand
            .ExecuteReaderAsync(
                connection,
                transaction,
                sql,
                CommandTimeoutSeconds,
                command => _dialect.AddParameter(command, "IncarnationId", SqlColumnType.Guid, incarnationId),
                async (reader, ct) =>
                {
                    if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        return null;
                    }

                    var lockedUntil = reader.IsDBNull(9)
                        ? (DateTimeOffset?)null
                        : reader.GetFieldValue<DateTimeOffset>(9);
                    var lane = MessageLaneCompatibility.FromPersistedValue(reader.GetInt16(6));
                    var generation = reader.GetInt64(5);

                    return new InboxOperationRow(
                        reader.GetGuid(0),
                        new InboxOperationState(
                            Enum.Parse<StatusName>(reader.GetString(1)),
                            !reader.IsDBNull(2),
                            reader.GetBoolean(3),
                            reader.GetBoolean(4),
                            generation,
                            reader.GetBoolean(8)
                        ),
                        lane,
                        reader.GetString(7),
                        new InboxRootKey(
                            reader.GetBoolean(10),
                            reader.GetString(11),
                            reader.GetString(12),
                            MessageLaneCompatibility.ToPersistedValue(lane),
                            reader.GetString(13),
                            reader.GetString(14),
                            reader.GetString(7),
                            generation
                        ),
                        reader.GetGuid(15),
                        lockedUntil
                    );
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private async Task _ExecuteInboxMutationAsync(
        DbConnection connection,
        DbTransaction transaction,
        string sql,
        InboxOperationRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        await RelationalCommand
            .ExecuteNonQueryAsync(
                connection,
                transaction,
                sql,
                CommandTimeoutSeconds,
                command =>
                {
                    _dialect.AddParameter(command, "IncarnationId", SqlColumnType.Guid, request.ExpectedIncarnationId);
                    _dialect.AddParameter(command, "OperationId", SqlColumnType.Guid, request.OperationId);
                    _dialect.AddParameter(command, "Actor", SqlColumnType.KeyText(_ActorMaxLength), request.Actor);
                    _dialect.AddParameter(command, "Reason", SqlColumnType.Text(_ReasonMaxLength), request.Reason);
                    _dialect.AddParameter(command, "Instant", SqlColumnType.Timestamp, now);
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Starts the next generation of an inbox lineage: the parent stops being current, and a child copy of its
    /// message is scheduled for a fresh dispatch.
    /// </summary>
    private async Task _CreateReplayChildAsync(
        DbConnection connection,
        DbTransaction transaction,
        InboxOperationRow row,
        InboxOperationRequest request,
        Guid childStorageId,
        Guid childIncarnationId,
        long childGeneration,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        var hash = _CreateInboxKeyHash(row.Key with { Generation = childGeneration }, row.LifecycleId);
        var sql = $"""
            UPDATE {_receivedTable} SET {_t.IsCurrentGeneration}={_t.False} WHERE {_t.GenerationIncarnationId}=@ParentIncarnationId AND {_t.IsCurrentGeneration}={_t.True};
            INSERT INTO {_receivedTable}({_t.Id},{_t.Version},{_t.Name},{_t.Group},{_t.GroupKey},{_t.Content},{_t.IntentType},{_t.Retries},{_t.InlineAttempts},{_t.Added},{_t.ExpiresAt},{_t.NextRetryAt},{_t.LockedUntil},{_t.Owner},{_t.StatusName},{_t.MessageId},{_t.ExceptionInfo},{_t.IsInboxRecord},{_t.TenantPresent},{_t.TenantId},{_t.ContractIdentity},{_t.ContractVersion},{_t.ConsumerIdentity},{_t.Generation},{_t.GenerationIncarnationId},{_t.LifecycleId},{_t.AttemptId},{_t.IsInboxOrphaned},{_t.IsCurrentGeneration},{_t.ReplayParentIncarnationId},{_t.ReplayOperationId},{_t.TerminalAt},{_t.EffectiveExpiresAt},{_t.IsHeld},{_t.HeldAt},{_t.HeldBy},{_t.HoldReason},{_t.HoldOperationId},{_t.InboxKeyHash},{_t.InboxRetentionSeconds})
            SELECT @ChildStorageId,{_t.Version},{_t.Name},{_t.Group},{_t.GroupKey},{_t.Content},{_t.IntentType},0,0,@Instant,NULL,{_dialect.ShiftByDuration(
                "@Instant",
                "Grace"
            )},NULL,NULL,'{nameof(
                StatusName.Scheduled
            )}',{_t.MessageId},NULL,{_t.True},{_t.TenantPresent},{_t.TenantId},{_t.ContractIdentity},{_t.ContractVersion},{_t.ConsumerIdentity},@ChildGeneration,@ChildIncarnationId,{_t.LifecycleId},NULL,{_t.False},{_t.True},{_t.GenerationIncarnationId},@OperationId,NULL,NULL,{_t.False},NULL,NULL,NULL,NULL,@InboxKeyHash,{_t.InboxRetentionSeconds}
            FROM {_receivedTable} WHERE {_t.GenerationIncarnationId}=@ParentIncarnationId;
            """;

        await RelationalCommand
            .ExecuteNonQueryAsync(
                connection,
                transaction,
                sql,
                CommandTimeoutSeconds,
                command =>
                {
                    _dialect.AddParameter(
                        command,
                        "ParentIncarnationId",
                        SqlColumnType.Guid,
                        request.ExpectedIncarnationId
                    );
                    _dialect.AddParameter(command, "ChildStorageId", SqlColumnType.Guid, childStorageId);
                    _dialect.AddParameter(command, "ChildIncarnationId", SqlColumnType.Guid, childIncarnationId);
                    _dialect.AddParameter(command, "ChildGeneration", SqlColumnType.Int64, childGeneration);
                    _dialect.AddParameter(command, "OperationId", SqlColumnType.Guid, request.OperationId);
                    _dialect.AddParameter(command, "Instant", SqlColumnType.Timestamp, now);
                    _dialect.AddDuration(command, "Grace", Options.RetryPolicy.InitialDispatchGrace);
                    _dialect.AddParameter(
                        command,
                        "InboxKeyHash",
                        SqlColumnType.FixedBinary(_InboxKeyHashLength),
                        hash
                    );
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private async Task _WriteInboxReceiptAndAuditAsync(
        DbConnection connection,
        DbTransaction transaction,
        InboxOperationResult result,
        CancellationToken cancellationToken
    )
    {
        var sql = $"""
            INSERT INTO {_t.Receipts}({_t.OperationId},{_t.TargetKind},{_t.GenerationIncarnationId},{_t.OperationType},{_t.ExpectedStatus},{_t.Actor},{_t.Reason},{_t.Outcome},{_t.StorageId},{_t.ChildStorageId},{_t.ChildGeneration},{_t.ChildIncarnationId},{_t.CreatedAt})
            VALUES (@OperationId,'{_InboxTargetKind}',@IncarnationId,@OperationType,@ExpectedStatus,@Actor,@Reason,@Outcome,@StorageId,@ChildStorageId,@ChildGeneration,@ChildIncarnationId,@CreatedAt);
            INSERT INTO {_t.Audit}({_t.AuditId},{_t.OperationId},{_t.TargetKind},{_t.GenerationIncarnationId},{_t.OperationType},{_t.Actor},{_t.Reason},{_t.Outcome},{_t.CreatedAt})
            VALUES (@AuditId,@OperationId,'{_InboxTargetKind}',@IncarnationId,@OperationType,@Actor,@Reason,@Outcome,@CreatedAt);
            """;

        await RelationalCommand
            .ExecuteNonQueryAsync(
                connection,
                transaction,
                sql,
                CommandTimeoutSeconds,
                command =>
                {
                    _dialect.AddParameter(command, "AuditId", SqlColumnType.Guid, _guidGenerator.Create());
                    _dialect.AddParameter(command, "OperationId", SqlColumnType.Guid, result.OperationId);
                    _dialect.AddParameter(command, "IncarnationId", SqlColumnType.Guid, result.ExpectedIncarnationId);
                    _dialect.AddParameter(command, "OperationType", _StatusType, result.OperationType.ToString());
                    _dialect.AddParameter(command, "ExpectedStatus", _StatusType, result.ExpectedStatus.ToString());
                    _dialect.AddParameter(command, "Actor", SqlColumnType.KeyText(_ActorMaxLength), result.Actor);
                    _dialect.AddParameter(command, "Reason", SqlColumnType.Text(_ReasonMaxLength), result.Reason);
                    _dialect.AddParameter(command, "Outcome", _StatusType, result.Outcome.ToString());
                    _dialect.AddParameter(command, "StorageId", SqlColumnType.Guid, result.StorageId);
                    _dialect.AddParameter(command, "ChildStorageId", SqlColumnType.Guid, result.ChildStorageId);
                    _dialect.AddParameter(command, "ChildGeneration", SqlColumnType.Int64, result.ChildGeneration);
                    _dialect.AddParameter(command, "ChildIncarnationId", SqlColumnType.Guid, result.ChildIncarnationId);
                    _dialect.AddParameter(command, "CreatedAt", SqlColumnType.Timestamp, result.CreatedAt);
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Records a request refused because its operation id was already used by a different request. The audit references
    /// the receipt that holds the id, and is stamped by the database clock.
    /// </summary>
    private async Task _WriteConflictAuditAsync(
        DbConnection connection,
        DbTransaction transaction,
        string targetKind,
        Guid operationId,
        Guid? incarnationId,
        MessagingOperationType operationType,
        string actor,
        string reason,
        InboxOperationOutcome outcome,
        CancellationToken cancellationToken
    )
    {
        var sql = _dialect.Render(
            new SqlClockedStatement(
                $"INSERT INTO {_t.Audit}({_t.AuditId},{_t.OperationId},{_t.TargetKind},{_t.GenerationIncarnationId},{_t.OperationType},{_t.Actor},{_t.Reason},{_t.Outcome},{_t.CreatedAt}) VALUES (@AuditId,@OperationId,'{targetKind}',@IncarnationId,@OperationType,@Actor,@Reason,@Outcome,{SqlDialectTokens.Now});"
            )
        );

        await RelationalCommand
            .ExecuteNonQueryAsync(
                connection,
                transaction,
                sql,
                CommandTimeoutSeconds,
                command =>
                {
                    _dialect.AddParameter(command, "AuditId", SqlColumnType.Guid, _guidGenerator.Create());
                    _dialect.AddParameter(command, "OperationId", SqlColumnType.Guid, operationId);
                    _dialect.AddParameter(command, "IncarnationId", SqlColumnType.Guid, incarnationId);
                    _dialect.AddParameter(command, "OperationType", _StatusType, operationType.ToString());
                    _dialect.AddParameter(command, "Actor", SqlColumnType.KeyText(_ActorMaxLength), actor);
                    _dialect.AddParameter(command, "Reason", SqlColumnType.Text(_ReasonMaxLength), reason);
                    _dialect.AddParameter(command, "Outcome", _StatusType, outcome.ToString());
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// An inbox generation as an operator operation reads it, locked for the operation's transaction. Whether its claim
    /// is live is decided once the clock is read, after the lock.
    /// </summary>
    private sealed record InboxOperationRow(
        Guid StorageId,
        InboxOperationState State,
        MessageLane Lane,
        string ConsumerIdentity,
        InboxRootKey Key,
        Guid LifecycleId,
        DateTimeOffset? LockedUntil
    );
}

#pragma warning restore CA1849, VSTHRD103, AsyncFixer02, MA0042
#pragma warning restore CA2100
