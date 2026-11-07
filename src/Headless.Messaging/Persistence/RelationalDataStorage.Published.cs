// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Checks;
using Headless.Messaging.Internal;
using Headless.Sql;

namespace Headless.Messaging.Persistence;

#pragma warning disable CA1849 // Buffered row reads cannot add blocking I/O.

// Audited re-send and delete of published rows, single-row and as Operations over a frozen scope. The receipt is
// written in the transaction that changes the row; an additional outbox's database holds no ledger, so it serves
// the scope reads and refuses the writes.
internal sealed partial class RelationalDataStorage
{
    private const string _PublishedTargetKind = "Published";

    public IPublishedOperationsApi GetPublishedOperationsApi() => new RelationalPublishedOperationsApi(this);

    private sealed class RelationalPublishedOperationsApi(RelationalDataStorage storage) : IPublishedOperationsApi
    {
        public ValueTask<MessageCount> CountOperationScopeAsync(
            MessageOperationScope scope,
            int cap = MessageCount.DefaultCap,
            CancellationToken cancellationToken = default
        ) => storage._CountPublishedScopeAsync(scope, cap, cancellationToken);

        public ValueTask<MessageOperationChunk> ReadOperationScopeAsync(
            MessageOperationScope scope,
            Guid? afterId,
            int chunkSize,
            CancellationToken cancellationToken = default
        ) => storage._ReadPublishedScopeAsync(scope, afterId, chunkSize, cancellationToken);

        public ValueTask<PublishedOperationResult> ResendAsync(
            PublishedOperationRequest request,
            CancellationToken cancellationToken = default
        ) =>
            storage._ExecutePublishedOperationAsync(
                "messaging.published_resend",
                MessagingOperationType.Resend,
                request,
                cancellationToken
            );

        public ValueTask<PublishedOperationResult> PurgeAsync(
            PublishedOperationRequest request,
            CancellationToken cancellationToken = default
        ) =>
            storage._ExecutePublishedOperationAsync(
                "messaging.published_purge",
                MessagingOperationType.Purge,
                request,
                cancellationToken
            );
    }

    private async ValueTask<MessageCount> _CountPublishedScopeAsync(
        MessageOperationScope scope,
        int cap,
        CancellationToken cancellationToken
    )
    {
        Argument.IsPositive(cap);
        var (where, bind) = _PublishedScopeFilter(scope);

        return await _CountScopeAsync(_publishedTable, where, bind, cap, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<MessageOperationChunk> _ReadPublishedScopeAsync(
        MessageOperationScope scope,
        Guid? afterId,
        int chunkSize,
        CancellationToken cancellationToken
    )
    {
        Argument.IsInclusiveBetween(chunkSize, 1, MessageOperationChunk.MaxChunkSize);
        var (where, bind) = _PublishedScopeFilter(scope);

        return await _ReadScopeChunkAsync(
                _publishedTable,
                where,
                bind,
                afterId,
                chunkSize,
                withIncarnation: false,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The predicate of a published Operation's frozen scope: the query's rows, terminal, stamped at or before the
    /// high-water mark, and still eligible for the command.
    /// </summary>
    private (string Where, Action<DbCommand> Bind) _PublishedScopeFilter(MessageOperationScope scope)
    {
        Argument.IsNotNull(scope);
        Argument.IsNotNull(scope.Query);

        if (scope.Query.MessageType != MessageType.Publish)
        {
            throw new ArgumentException("A published Operation scope reads published rows only.", nameof(scope));
        }

        var (where, bind) = RelationalMessageFilter.Build(_t, _dialect, scope.Query);
        where += $" AND {RelationalMessageFilter.Terminal(_t)} AND {_t.ChangedAt} <= @HighWaterMark";
        where += scope.Kind switch
        {
            MessageOperationKind.Resend => $" AND {_t.StatusName} = '{nameof(StatusName.Failed)}'",
            MessageOperationKind.Delete => "",
            _ => throw new ArgumentOutOfRangeException(
                nameof(scope),
                scope.Kind,
                "A published Operation re-sends or deletes."
            ),
        };

        return (
            where,
            command =>
            {
                bind(command);
                _dialect.AddParameter(command, "HighWaterMark", SqlColumnType.Timestamp, scope.HighWaterMark);
            }
        );
    }

    /// <summary>
    /// Runs one audited published-row operation under its operation id's lock, so a retried request replays its stored
    /// receipt and a different request under a used id is refused as a conflict.
    /// </summary>
    private async ValueTask<PublishedOperationResult> _ExecutePublishedOperationAsync(
        string operation,
        MessagingOperationType operationType,
        PublishedOperationRequest request,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(request);
        request.Validate();

        if (!_holdsReceived)
        {
            throw new NotSupportedException(
                "An additional outbox's database holds no operator ledger, so it cannot record a published-message operation."
            );
        }

        return await SqlAutonomousTransaction
            .RunAsync(
                operation,
                _CreateConnection,
                async (connection, transaction, ct) =>
                {
                    await _LockOperationAsync(connection, transaction, request.OperationId, ct).ConfigureAwait(false);

                    var prior = await _ReadPublishedReceiptAsync(connection, transaction, request.OperationId, ct)
                        .ConfigureAwait(false);
                    if (prior is not null)
                    {
                        var replay = _ReplayOrConflict(prior, operationType, request);
                        if (replay.Outcome is InboxOperationOutcome.OperationConflict)
                        {
                            await _WriteConflictAuditAsync(
                                    connection,
                                    transaction,
                                    _PublishedTargetKind,
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

                        return replay;
                    }

                    var (row, now) = await _ReadPublishedOperationRowAsync(
                            connection,
                            transaction,
                            request.StorageId,
                            ct
                        )
                        .ConfigureAwait(false);
                    var outcome = MessagingOperationEvaluator.EvaluatePublished(
                        operationType,
                        request.ExpectedChangedAt,
                        row is null ? null : row.State with { HasLiveLease = row.LockedUntil > now }
                    );

                    if (outcome is InboxOperationOutcome.Applied)
                    {
                        var written = await _WritePublishedOperationAsync(
                                connection,
                                transaction,
                                operationType,
                                request,
                                now,
                                ct
                            )
                            .ConfigureAwait(false);

                        // The locked read decided the row is eligible; a write that still changes nothing is a refusal,
                        // never a second change.
                        if (written == 0)
                        {
                            outcome = InboxOperationOutcome.StateConflict;
                        }
                    }

                    var result = new PublishedOperationResult(
                        request.OperationId,
                        operationType,
                        outcome,
                        request.StorageId,
                        row?.MessageName,
                        row?.MessageId,
                        row?.Lane,
                        request.Actor,
                        request.StoredReason,
                        now
                    );
                    await _WritePublishedReceiptAndAuditAsync(connection, transaction, result, ct)
                        .ConfigureAwait(false);

                    return result;
                },
                _timeProvider,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Re-sends or deletes the row with its eligibility, and the caller's change marker when given, in the statement's
    /// own <c>WHERE</c>.
    /// </summary>
    private async Task<int> _WritePublishedOperationAsync(
        DbConnection connection,
        DbTransaction transaction,
        MessagingOperationType operationType,
        PublishedOperationRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        var fence =
            $"{_t.Id}=@StorageId AND {RelationalMessageFilter.Terminal(_t)} AND ({_t.LockedUntil} IS NULL OR {_t.LockedUntil} <= @Instant)"
            + (request.ExpectedChangedAt is null ? "" : $" AND {_t.ChangedAt}=@ExpectedChangedAt");

        var sql =
            operationType is MessagingOperationType.Resend
                ? _dialect.Render(
                    new SqlClockedStatement(
                        $"UPDATE {_publishedTable} SET {_t.NextRetryAt}=@Instant,{_t.Retries}=0,{_t.InlineAttempts}=0,{_t.ExpiresAt}=NULL,{_t.LockedUntil}=NULL,{_t.Owner}=NULL,{_t.StampChange} WHERE {fence} AND {_t.StatusName}='{nameof(StatusName.Failed)}';"
                    )
                )
                : $"DELETE FROM {_publishedTable} WHERE {fence};";

        return await RelationalCommand
            .ExecuteNonQueryAsync(
                connection,
                transaction,
                sql,
                CommandTimeoutSeconds,
                command =>
                {
                    _dialect.AddParameter(command, "StorageId", SqlColumnType.Guid, request.StorageId);
                    _dialect.AddParameter(command, "Instant", SqlColumnType.Timestamp, now);
                    if (request.ExpectedChangedAt is { } expected)
                    {
                        _dialect.AddParameter(
                            command,
                            "ExpectedChangedAt",
                            SqlColumnType.Timestamp,
                            expected.ToUniversalTime()
                        );
                    }
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private async Task<PublishedOperationResult?> _ReadPublishedReceiptAsync(
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

                    // A receipt of another target kind under this id never replays as a published one.
                    var isPublished = string.Equals(
                        reader.GetString(0),
                        _PublishedTargetKind,
                        StringComparison.Ordinal
                    );

                    return new PublishedOperationResult(
                        operationId,
                        Enum.Parse<MessagingOperationType>(reader.GetString(1)),
                        Enum.Parse<InboxOperationOutcome>(reader.GetString(2)),
                        isPublished && !reader.IsDBNull(3) ? reader.GetGuid(3) : Guid.Empty,
                        reader.IsDBNull(4) ? null : reader.GetString(4),
                        reader.IsDBNull(5) ? null : reader.GetString(5),
                        reader.IsDBNull(6) ? null : Enum.Parse<MessageLane>(reader.GetString(6)),
                        reader.GetString(7),
                        reader.IsDBNull(8) ? null : reader.GetString(8),
                        reader.GetFieldValue<DateTimeOffset>(9)
                    );
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private static PublishedOperationResult _ReplayOrConflict(
        PublishedOperationResult prior,
        MessagingOperationType operationType,
        PublishedOperationRequest request
    )
    {
        var matches =
            prior.OperationType == operationType
            && prior.StorageId == request.StorageId
            && string.Equals(prior.Actor, request.Actor, StringComparison.Ordinal)
            && string.Equals(prior.Reason, request.StoredReason, StringComparison.Ordinal);

        return matches
            ? prior with
            {
                IsReplay = true,
            }
            : new PublishedOperationResult(
                request.OperationId,
                operationType,
                InboxOperationOutcome.OperationConflict,
                request.StorageId,
                prior.MessageName,
                prior.MessageId,
                prior.Lane,
                request.Actor,
                request.StoredReason,
                prior.CreatedAt,
                IsReplay: true
            );
    }

    /// <summary>
    /// A published row as an operator operation reads it, locked for the operation's transaction. Whether a send lease
    /// is live is decided once the clock is read, after the lock.
    /// </summary>
    private sealed record PublishedOperationRow(
        PublishedOperationState State,
        string MessageName,
        string MessageId,
        MessageLane Lane,
        DateTimeOffset? LockedUntil
    );

    /// <summary>
    /// Reads the row under its lock, then the database clock in the same batch. The clock statement runs after the
    /// locked read, so whether a send lease is live is decided on a clock read after any wait for that lock.
    /// </summary>
    private async Task<(PublishedOperationRow? Row, DateTimeOffset Now)> _ReadPublishedOperationRowAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid storageId,
        CancellationToken cancellationToken
    )
    {
        var sql =
            _dialect.Render(
                new SqlLockedRead(
                    _publishedTable,
                    [new SqlKeyColumn(_t.Id, "Id")],
                    [_t.StatusName, _t.NextRetryAt, _t.LockedUntil, _t.ChangedAt, _t.Name, _t.MessageId, _t.IntentType]
                )
            ) + _dialect.Render(new SqlClockedStatement($"SELECT {SqlDialectTokens.Now};"));

        return await RelationalCommand
            .ExecuteReaderAsync(
                connection,
                transaction,
                sql,
                CommandTimeoutSeconds,
                command => _dialect.AddParameter(command, "Id", SqlColumnType.Guid, storageId),
                static async (reader, ct) =>
                {
                    PublishedOperationRow? row = null;
                    if (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        row = new PublishedOperationRow(
                            new PublishedOperationState(
                                Enum.Parse<StatusName>(reader.GetString(0)),
                                HasNextRetry: !reader.IsDBNull(1),
                                HasLiveLease: false,
                                (
                                    await reader.GetFieldValueAsync<DateTimeOffset>(3, ct).ConfigureAwait(false)
                                ).ToUniversalTime()
                            ),
                            reader.GetString(4),
                            reader.GetString(5),
                            MessageLaneCompatibility.FromPersistedValue(reader.GetInt16(6)),
                            reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2)
                        );
                    }

                    // The clock's own result set follows the locked read's, whether or not it found the row.
                    if (
                        !await reader.NextResultAsync(ct).ConfigureAwait(false)
                        || !await reader.ReadAsync(ct).ConfigureAwait(false)
                    )
                    {
                        throw new InvalidOperationException("The database clock read returned no row.");
                    }

                    return (row, await reader.GetFieldValueAsync<DateTimeOffset>(0, ct).ConfigureAwait(false));
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private async Task _WritePublishedReceiptAndAuditAsync(
        DbConnection connection,
        DbTransaction transaction,
        PublishedOperationResult result,
        CancellationToken cancellationToken
    )
    {
        var sql = $"""
            INSERT INTO {_t.Receipts}({_t.OperationId},{_t.TargetKind},{_t.OperationType},{_t.Outcome},{_t.Actor},{_t.Reason},{_t.StorageId},{_t.MessageName},{_t.MessageId},{_t.Lane},{_t.CreatedAt})
            VALUES (@OperationId,'{_PublishedTargetKind}',@OperationType,@Outcome,@Actor,@Reason,@StorageId,@MessageName,@MessageId,@Lane,@CreatedAt);
            INSERT INTO {_t.Audit}({_t.AuditId},{_t.OperationId},{_t.TargetKind},{_t.OperationType},{_t.Actor},{_t.Reason},{_t.Outcome},{_t.CreatedAt})
            VALUES (@AuditId,@OperationId,'{_PublishedTargetKind}',@OperationType,@Actor,@Reason,@Outcome,@CreatedAt);
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
