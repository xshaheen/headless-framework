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

                    var attempt = new RowOperationReceipt(
                        request.OperationId,
                        _PublishedTargetKind,
                        operationType,
                        default,
                        request.StorageId,
                        ExpectedDueAt: null,
                        MessageName: null,
                        MessageId: null,
                        Lane: null,
                        request.Actor,
                        request.StoredReason,
                        default
                    );
                    var replay = await _ReplayRowOperationAsync(connection, transaction, attempt, ct)
                        .ConfigureAwait(false);
                    if (replay is not null)
                    {
                        return _ToPublishedResult(replay, isReplay: true);
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

                    var receipt = attempt with
                    {
                        Outcome = outcome,
                        MessageName = row?.MessageName,
                        MessageId = row?.MessageId,
                        Lane = row?.Lane,
                        CreatedAt = now,
                    };
                    await _WriteRowOperationReceiptAndAuditAsync(connection, transaction, receipt, ct)
                        .ConfigureAwait(false);

                    return _ToPublishedResult(receipt, isReplay: false);
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

    private static PublishedOperationResult _ToPublishedResult(RowOperationReceipt receipt, bool isReplay) =>
        new(
            receipt.OperationId,
            receipt.OperationType,
            receipt.Outcome,
            receipt.StorageId,
            receipt.MessageName,
            receipt.MessageId,
            receipt.Lane,
            receipt.Actor,
            receipt.Reason,
            receipt.CreatedAt,
            isReplay
        );

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
}

#pragma warning restore CA1849
