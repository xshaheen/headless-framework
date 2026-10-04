// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Messaging.Internal;
using Headless.Primitives;
using Headless.Sql;

namespace Headless.Messaging.Persistence;

#pragma warning disable CA1849 // Buffered row reads cannot add blocking I/O.

internal sealed partial class RelationalDataStorage
{
    /// <summary>
    /// A scheduled publish that never started: no inline attempt, no persisted retry, and not permanently done. Only such
    /// a row may still be revoked or dispatched early.
    /// </summary>
    private string ScheduledEligibility =>
        $"{_terminalGuard} AND {_t.InlineAttempts}=0 AND {_t.Retries}=0 AND {_t.NextRetryAt} IS NULL";

    /// <summary>The scheduled publishes of this service version still waiting for their due time.</summary>
    private string ScheduledPending =>
        $"{_t.Version}=@Version AND {_t.StatusName} IN ('{nameof(StatusName.Delayed)}','{nameof(StatusName.Queued)}') AND {_t.InlineAttempts}=0 AND {_t.Retries}=0 AND {_t.NextRetryAt} IS NULL AND {_t.ExpiresAt} IS NOT NULL AND {_t.IntentType} IN (0, 1)";

    public async ValueTask<MessageRevocationResult> RevokeAsync(
        Guid storageId,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        void bind(DbCommand command)
        {
            _dialect.AddParameter(command, "Id", SqlColumnType.Guid, storageId);
            _BindVersion(command);
        }

        await using var connection = _CreateConnection();
        var revoked = await RelationalCommand
            .ExecuteNonQueryAsync(
                connection,
                transaction: null,
                $"DELETE FROM {_publishedTable} WHERE {_t.Id}=@Id AND {_t.Version}=@Version AND {ScheduledEligibility};",
                CommandTimeoutSeconds,
                bind,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (revoked > 0)
        {
            return MessageRevocationResult.Revoked;
        }

        // Not revoked: tell a row that has already started apart from one that does not exist.
        var exists = Convert.ToInt32(
            await RelationalCommand
                .ExecuteScalarAsync(
                    connection,
                    transaction: null,
                    $"SELECT COUNT(*) FROM {_publishedTable} WHERE {_t.Id}=@Id AND {_t.Version}=@Version;",
                    CommandTimeoutSeconds,
                    bind,
                    cancellationToken
                )
                .ConfigureAwait(false),
            CultureInfo.InvariantCulture
        );

        return exists > 0 ? MessageRevocationResult.AttemptReserved : MessageRevocationResult.NotFound;
    }

    public async ValueTask<IndexPage<ScheduledDeliveryView>> QueryAsync(
        ScheduledDeliveryQuery query,
        OperatorAuthorizationContext authorization,
        CancellationToken cancellationToken = default
    )
    {
        authorization.Validate();
        query.Validate();
        var page = Math.Max(query.CurrentPage, 0);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var where = ScheduledPending;

        if (!string.IsNullOrWhiteSpace(query.MessageName))
        {
            where += $" AND {_t.Name}=@Name";
        }

        if (query.Lane is not null)
        {
            where += $" AND {_t.IntentType}=@IntentType";
        }

        if (query.DueFrom is not null)
        {
            where += $" AND {_t.ExpiresAt}>=@DueFrom";
        }

        if (query.DueTo is not null)
        {
            where += $" AND {_t.ExpiresAt}<=@DueTo";
        }

        if (query.StorageIds is { Count: > 0 })
        {
            where += $" AND {_dialect.InList(_t.Id, "StorageIds", SqlColumnType.Guid)}";
        }

        void bind(DbCommand command)
        {
            _BindVersion(command);
            if (!string.IsNullOrWhiteSpace(query.MessageName))
            {
                _dialect.AddParameter(command, "Name", _NameType, query.MessageName);
            }

            if (query.Lane is { } lane)
            {
                _dialect.AddParameter(command, "IntentType", SqlColumnType.Int16, (short)lane);
            }

            if (query.DueFrom is { } dueFrom)
            {
                _dialect.AddParameter(command, "DueFrom", SqlColumnType.Timestamp, dueFrom);
            }

            if (query.DueTo is { } dueTo)
            {
                _dialect.AddParameter(command, "DueTo", SqlColumnType.Timestamp, dueTo);
            }

            if (query.StorageIds is { Count: > 0 } storageIds)
            {
                _dialect.AddListParameter(
                    command,
                    "StorageIds",
                    SqlColumnType.Guid,
                    storageIds as Guid[] ?? [.. storageIds]
                );
            }
        }

        await using var connection = _CreateConnection();
        var total = Convert.ToInt64(
            await RelationalCommand
                .ExecuteScalarAsync(
                    connection,
                    transaction: null,
                    $"SELECT COUNT(*) FROM {_publishedTable} WHERE {where};",
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
                _dialect.Render(
                    new SqlClockedStatement(
                        $"""
                        SELECT {_t.Id},{_t.MessageId},{_t.Name},{_t.IntentType},{_t.ExpiresAt},{_t.LockedUntil},{_t.Owner},{_t.InlineAttempts},CASE WHEN {_t.LockedUntil} > {SqlDialectTokens.Now} THEN 1 ELSE 0 END
                        FROM {_publishedTable}
                        WHERE {where}
                        ORDER BY {_t.ExpiresAt} ASC,{_t.Id} ASC
                        {_dialect.Limit("PageSize", "Offset")};
                        """
                    )
                ),
                CommandTimeoutSeconds,
                command =>
                {
                    bind(command);
                    _dialect.AddParameter(command, "PageSize", SqlColumnType.Int32, pageSize);
                    _dialect.AddParameter(command, "Offset", SqlColumnType.Int32, checked(page * pageSize));
                },
                static async (reader, ct) =>
                {
                    var views = new List<ScheduledDeliveryView>();
                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        views.Add(
                            new ScheduledDeliveryView(
                                reader.GetGuid(0),
                                reader.GetString(1),
                                reader.GetString(2),
                                MessageLaneCompatibility.FromPersistedValue(reader.GetInt16(3)),
                                reader.GetFieldValue<DateTimeOffset>(4),
                                "Pending",
                                Convert.ToInt32(reader.GetValue(8), CultureInfo.InvariantCulture) == 1,
                                reader.IsDBNull(6) ? null : reader.GetString(6),
                                reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
                                reader.GetInt32(7)
                            )
                        );
                    }

                    return views;
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        return new IndexPage<ScheduledDeliveryView>(items, page, pageSize, checked((int)total));
    }

    public ValueTask<ScheduledDeliveryOperationResult> RevokeAsync(
        ScheduledDeliveryOperationRequest request,
        CancellationToken cancellationToken = default
    ) =>
        _ExecuteScheduledOperationAsync(
            "messaging.scheduled_revoke",
            MessagingOperationType.Revoke,
            request,
            cancellationToken
        );

    public ValueTask<ScheduledDeliveryOperationResult> DispatchNowAsync(
        ScheduledDeliveryOperationRequest request,
        CancellationToken cancellationToken = default
    ) =>
        _ExecuteScheduledOperationAsync(
            "messaging.scheduled_dispatch_now",
            MessagingOperationType.DispatchNow,
            request,
            cancellationToken
        );

    /// <summary>
    /// Runs one audited scheduled-delivery operation under its operation id's lock, so a retried request replays its
    /// stored receipt and a different request under a used id is refused as a conflict.
    /// </summary>
    private async ValueTask<ScheduledDeliveryOperationResult> _ExecuteScheduledOperationAsync(
        string operation,
        MessagingOperationType operationType,
        ScheduledDeliveryOperationRequest request,
        CancellationToken cancellationToken
    )
    {
        request.Validate();

        var (result, row) = await SqlAutonomousTransaction
            .RunAsync(
                operation,
                _CreateConnection,
                async (connection, transaction, ct) =>
                {
                    await _LockOperationAsync(connection, transaction, request.OperationId, ct).ConfigureAwait(false);

                    var prior = await _ReadScheduledReceiptAsync(connection, transaction, request.OperationId, ct)
                        .ConfigureAwait(false);
                    if (prior is not null)
                    {
                        var replay = _ReplayOrConflict(prior, operationType, request);
                        if (replay.Outcome is InboxOperationOutcome.OperationConflict)
                        {
                            await _WriteConflictAuditAsync(
                                    connection,
                                    transaction,
                                    _ScheduledTargetKind,
                                    replay.OperationId,
                                    incarnationId: null,
                                    replay.OperationType,
                                    replay.Actor,
                                    replay.Reason,
                                    replay.Outcome,
                                    ct
                                )
                                .ConfigureAwait(false);
                        }

                        return (replay, (ScheduledOperationRow?)null);
                    }

                    // The row is locked before the clock is read, so whether a dispatch lease is live and the new due
                    // time are decided on a clock read after any wait for that lock.
                    var row = await _ReadScheduledOperationRowAsync(connection, transaction, request.StorageId, ct)
                        .ConfigureAwait(false);
                    var now = await _ReadNowAsync(connection, transaction, ct).ConfigureAwait(false);
                    row = row is null
                        ? null
                        : row with
                        {
                            State = row.State with { HasLiveLease = row.LockedUntil > now },
                        };
                    var outcome = MessagingOperationEvaluator.Evaluate(
                        operationType,
                        request.ExpectedDueAt,
                        row?.State
                    );

                    if (outcome is InboxOperationOutcome.Applied && row is not null)
                    {
                        await RelationalCommand
                            .ExecuteNonQueryAsync(
                                connection,
                                transaction,
                                operationType is MessagingOperationType.Revoke
                                    ? $"DELETE FROM {_publishedTable} WHERE {_t.Id}=@StorageId AND {_t.Version}=@Version AND {_t.ExpiresAt}=@ExpectedDueAt AND {ScheduledEligibility};"
                                    // Due now: the delayed claim picks the row up on its next pass. A live lease
                                    // means a dispatch already started, so the row is left to it.
                                    : $"UPDATE {_publishedTable} SET {_t.StatusName}='{nameof(StatusName.Delayed)}',{_t.ExpiresAt}=@Instant,{_t.LockedUntil}=NULL,{_t.Owner}=NULL WHERE {_t.Id}=@StorageId AND {_t.Version}=@Version AND {_t.ExpiresAt}=@ExpectedDueAt AND ({_t.LockedUntil} IS NULL OR {_t.LockedUntil} <= @Instant) AND {ScheduledEligibility};",
                                CommandTimeoutSeconds,
                                command =>
                                {
                                    _dialect.AddParameter(command, "StorageId", SqlColumnType.Guid, request.StorageId);
                                    _BindVersion(command);
                                    _dialect.AddParameter(
                                        command,
                                        "ExpectedDueAt",
                                        SqlColumnType.Timestamp,
                                        request.ExpectedDueAt
                                    );
                                    _dialect.AddParameter(command, "Instant", SqlColumnType.Timestamp, now);
                                },
                                ct
                            )
                            .ConfigureAwait(false);
                    }

                    var applied = new ScheduledDeliveryOperationResult(
                        request.OperationId,
                        operationType,
                        outcome,
                        request.StorageId,
                        request.ExpectedDueAt,
                        row?.MessageName,
                        row?.MessageId,
                        row?.Lane,
                        request.Actor,
                        request.Reason,
                        now
                    );
                    await _WriteScheduledReceiptAndAuditAsync(connection, transaction, applied, ct)
                        .ConfigureAwait(false);

                    return (applied, row);
                },
                _timeProvider,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (result.Outcome is InboxOperationOutcome.Applied && row is not null)
        {
            MessagingMetrics.RecordScheduledOperation(operationType, row.Lane, result.Outcome, _storage.ProviderName);
        }

        return result;
    }

    private async Task<ScheduledDeliveryOperationResult?> _ReadScheduledReceiptAsync(
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
                    _t.TargetKind,
                    _t.OperationType,
                    _t.Outcome,
                    _t.StorageId,
                    _t.ExpectedDueAt,
                    _t.MessageName,
                    _t.MessageId,
                    _t.Lane,
                    _t.Actor,
                    _t.Reason,
                    _t.CreatedAt,
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

                    return new ScheduledDeliveryOperationResult(
                        operationId,
                        Enum.Parse<MessagingOperationType>(reader.GetString(1)),
                        Enum.Parse<InboxOperationOutcome>(reader.GetString(2)),
                        reader.IsDBNull(3) ? Guid.Empty : reader.GetGuid(3),
                        reader.IsDBNull(4) ? DateTimeOffset.MinValue : reader.GetFieldValue<DateTimeOffset>(4),
                        reader.IsDBNull(5) ? null : reader.GetString(5),
                        reader.IsDBNull(6) ? null : reader.GetString(6),
                        reader.IsDBNull(7) ? null : Enum.Parse<MessageLane>(reader.GetString(7)),
                        reader.GetString(8),
                        reader.GetString(9),
                        reader.GetFieldValue<DateTimeOffset>(10)
                    );
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private static ScheduledDeliveryOperationResult _ReplayOrConflict(
        ScheduledDeliveryOperationResult prior,
        MessagingOperationType operationType,
        ScheduledDeliveryOperationRequest request
    )
    {
        var matches =
            prior.OperationType == operationType
            && prior.StorageId == request.StorageId
            && prior.ExpectedDueAt == request.ExpectedDueAt
            && string.Equals(prior.Actor, request.Actor, StringComparison.Ordinal)
            && string.Equals(prior.Reason, request.Reason, StringComparison.Ordinal);

        return matches
            ? prior with
            {
                IsReplay = true,
            }
            : new ScheduledDeliveryOperationResult(
                request.OperationId,
                operationType,
                InboxOperationOutcome.OperationConflict,
                request.StorageId,
                request.ExpectedDueAt,
                prior.MessageName,
                prior.MessageId,
                prior.Lane,
                request.Actor,
                request.Reason,
                prior.CreatedAt,
                IsReplay: true
            );
    }

    /// <summary>
    /// A scheduled publish as an operator operation reads it, locked for the operation's transaction. Whether a dispatch
    /// lease is live is decided once the clock is read, after the lock.
    /// </summary>
    private sealed record ScheduledOperationRow(
        ScheduledDeliveryOperationState State,
        string MessageName,
        string MessageId,
        MessageLane Lane,
        DateTimeOffset? LockedUntil
    );

    private async Task<ScheduledOperationRow?> _ReadScheduledOperationRowAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid storageId,
        CancellationToken cancellationToken
    )
    {
        var sql = _dialect.Render(
            new SqlLockedRead(
                _publishedTable,
                [new SqlKeyColumn(_t.Id, "Id")],
                [
                    _t.Version,
                    _t.StatusName,
                    _t.InlineAttempts,
                    _t.Retries,
                    _t.NextRetryAt,
                    _t.LockedUntil,
                    _t.ExpiresAt,
                    _t.Name,
                    _t.MessageId,
                    _t.IntentType,
                ]
            )
        );

        return await RelationalCommand
            .ExecuteReaderAsync(
                connection,
                transaction,
                sql,
                CommandTimeoutSeconds,
                command => _dialect.AddParameter(command, "Id", SqlColumnType.Guid, storageId),
                async (reader, ct) =>
                {
                    if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        return null;
                    }

                    var lockedUntil = reader.IsDBNull(5)
                        ? (DateTimeOffset?)null
                        : reader.GetFieldValue<DateTimeOffset>(5);

                    return new ScheduledOperationRow(
                        new ScheduledDeliveryOperationState(
                            Enum.Parse<StatusName>(reader.GetString(1)),
                            reader.GetInt32(2),
                            reader.GetInt32(3),
                            reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4),
                            HasLiveLease: false,
                            Options.Version,
                            reader.GetString(0),
                            reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6)
                        ),
                        reader.GetString(7),
                        reader.GetString(8),
                        MessageLaneCompatibility.FromPersistedValue(reader.GetInt16(9)),
                        lockedUntil
                    );
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private async Task _WriteScheduledReceiptAndAuditAsync(
        DbConnection connection,
        DbTransaction transaction,
        ScheduledDeliveryOperationResult result,
        CancellationToken cancellationToken
    )
    {
        var sql = $"""
            INSERT INTO {_t.Receipts}({_t.OperationId},{_t.TargetKind},{_t.OperationType},{_t.Outcome},{_t.Actor},{_t.Reason},{_t.ExpectedDueAt},{_t.StorageId},{_t.MessageName},{_t.MessageId},{_t.Lane},{_t.CreatedAt})
            VALUES (@OperationId,'{_ScheduledTargetKind}',@OperationType,@Outcome,@Actor,@Reason,@ExpectedDueAt,@StorageId,@MessageName,@MessageId,@Lane,@CreatedAt);
            INSERT INTO {_t.Audit}({_t.AuditId},{_t.OperationId},{_t.TargetKind},{_t.OperationType},{_t.Actor},{_t.Reason},{_t.Outcome},{_t.CreatedAt})
            VALUES (@AuditId,@OperationId,'{_ScheduledTargetKind}',@OperationType,@Actor,@Reason,@Outcome,@CreatedAt);
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
                    _dialect.AddParameter(command, "OperationType", _StatusType, result.OperationType.ToString());
                    _dialect.AddParameter(command, "Outcome", _StatusType, result.Outcome.ToString());
                    _dialect.AddParameter(command, "Actor", SqlColumnType.KeyText(_ActorMaxLength), result.Actor);
                    _dialect.AddParameter(command, "Reason", SqlColumnType.Text(_ReasonMaxLength), result.Reason);
                    _dialect.AddParameter(command, "ExpectedDueAt", SqlColumnType.Timestamp, result.ExpectedDueAt);
                    _dialect.AddParameter(command, "StorageId", SqlColumnType.Guid, result.StorageId);
                    _dialect.AddParameter(command, "MessageName", _NameType, result.MessageName);
                    _dialect.AddParameter(command, "MessageId", _MessageIdType, result.MessageId);
                    _dialect.AddParameter(command, "Lane", _StatusType, result.Lane?.ToString());
                    _dialect.AddParameter(command, "CreatedAt", SqlColumnType.Timestamp, result.CreatedAt);
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }
}

#pragma warning restore CA1849
