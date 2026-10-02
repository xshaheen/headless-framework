// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Sql;

namespace Headless.Messaging.Persistence;

internal sealed partial class RelationalDataStorage
{
    /// <summary>
    /// How many times a redelivery that found its row gone looks for it again. A row disappears between the insert
    /// that found it and the read that locks it only when it is deleted in that instant.
    /// </summary>
    private const int _ReceivedStoreAttempts = 3;

    /// <summary>
    /// Persists a published outbox message to the published table. When <paramref name="transaction"/>
    /// is supplied the INSERT participates in the caller's database transaction (transactional outbox).
    /// </summary>
    /// <returns>The stored <c>MediumMessage</c> with its generated <c>StorageId</c> and timestamps populated.</returns>
    public ValueTask<MediumMessage> StoreMessageAsync(
        string name,
        MediumMessage message,
        DbTransaction? transaction = null,
        CancellationToken cancellationToken = default
    )
    {
        return _StoreMessageAsync(name, message, publishAt: null, transaction, cancellationToken);
    }

    public ValueTask<MediumMessage> StoreScheduledMessageAsync(
        string name,
        MediumMessage message,
        DateTimeOffset publishAt,
        DbTransaction? transaction = null,
        CancellationToken cancellationToken = default
    )
    {
        return _StoreMessageAsync(name, message, publishAt, transaction, cancellationToken);
    }

    /// <summary>
    /// Persists a published outbox message built from a raw <c>Message</c> payload to the published table.
    /// Convenience overload that wraps <paramref name="content"/> in a <c>MediumMessage</c> before storing.
    /// </summary>
    /// <returns>The stored <c>MediumMessage</c> with its generated <c>StorageId</c> and timestamps populated.</returns>
    public ValueTask<MediumMessage> StoreMessageAsync(
        string name,
        Message content,
        DbTransaction? transaction = null,
        CancellationToken cancellationToken = default
    )
    {
        return StoreMessageAsync(
            name,
            new MediumMessage
            {
                StorageId = Guid.Empty,
                Origin = content,
                Content = string.Empty,
                Lane = MessageLane.Bus,
            },
            transaction,
            cancellationToken
        );
    }

    private async ValueTask<MediumMessage> _StoreMessageAsync(
        string name,
        MediumMessage message,
        DateTimeOffset? publishAt,
        DbTransaction? transaction,
        CancellationToken cancellationToken
    )
    {
        // The database clock stamps Added and decides when the row falls due, the same clock the retry pickup and
        // the delayed claim compare against, so a replica whose clock is skewed cannot make a row due early or late.
        // A publish time within a minute of now goes straight to Queued; a later one waits as Delayed.
        var sql = _dialect.Render(
            new SqlInsert(
                _publishedTable,
                [
                    _t.Id,
                    _t.Version,
                    _t.Name,
                    _t.Content,
                    _t.IntentType,
                    _t.Retries,
                    _t.InlineAttempts,
                    _t.Added,
                    _t.ExpiresAt,
                    _t.NextRetryAt,
                    _t.LockedUntil,
                    _t.Owner,
                    _t.StatusName,
                    _t.MessageId,
                ],
                [
                    "@Id",
                    "@Version",
                    "@Name",
                    "@Content",
                    "@IntentType",
                    "0",
                    "0",
                    SqlDialectTokens.Now,
                    "@ExpiresAt",
                    $"CASE WHEN @ExpiresAt IS NULL THEN {_dialect.ShiftByDuration(SqlDialectTokens.Now, "Grace")} END",
                    "NULL",
                    "NULL",
                    $"CASE WHEN @ExpiresAt IS NULL THEN '{nameof(StatusName.Scheduled)}' WHEN @ExpiresAt <= {_dialect.ShiftByDuration(SqlDialectTokens.Now, "QueueWindow")} THEN '{nameof(StatusName.Queued)}' ELSE '{nameof(StatusName.Delayed)}' END",
                    "@MessageId",
                ],
                [_t.Added, _t.NextRetryAt]
            )
        );

        var stored = new MediumMessage
        {
            StorageId = _guidGenerator.Create(),
            Origin = message.Origin,
            Content = _serializer.Serialize(message.Origin),
            Lane = message.Lane,
            ExpiresAt = publishAt,
            LockedUntil = null,
            Owner = null,
            Retries = 0,
            InlineAttempts = 0,
        };

        await using var ownedConnection = transaction is null ? _CreateConnection() : null;
        var connection =
            transaction?.Connection
            ?? ownedConnection
            ?? throw new InvalidOperationException(
                "The supplied DbTransaction has no active Connection — it may have already been committed or rolled back."
            );

        var (added, nextRetryAt) = await RelationalCommand
            .ExecuteReaderAsync(
                connection,
                transaction,
                sql,
                CommandTimeoutSeconds,
                command =>
                {
                    _dialect.AddParameter(command, "Id", SqlColumnType.Guid, stored.StorageId);
                    _BindVersion(command);
                    _dialect.AddParameter(command, "Name", _NameType, name);
                    _dialect.AddParameter(command, "Content", _ContentType, stored.Content);
                    _dialect.AddParameter(
                        command,
                        "IntentType",
                        SqlColumnType.Int16,
                        MessageLaneCompatibility.ToPersistedValue(stored.Lane)
                    );
                    _dialect.AddParameter(command, "ExpiresAt", SqlColumnType.Timestamp, publishAt);
                    _dialect.AddDuration(command, "Grace", Options.RetryPolicy.InitialDispatchGrace);
                    _dialect.AddDuration(command, "QueueWindow", TimeSpan.FromMinutes(1));
                    _dialect.AddParameter(command, "MessageId", _MessageIdType, message.Origin.Id);
                },
                static async (reader, ct) =>
                {
                    if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        throw new InvalidOperationException("The message insert returned no row.");
                    }

                    return (
                        await reader.GetFieldValueAsync<DateTimeOffset>(0, ct).ConfigureAwait(false),
                        await _ReadInstantAsync(reader, 1, ct).ConfigureAwait(false)
                    );
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        stored.Added = added;
        stored.NextRetryAt = nextRetryAt;

        return stored;
    }

    /// <summary>
    /// Stores a received message that failed before it could be dispatched, using its raw serialized
    /// <paramref name="content"/> string. The row is written directly into the <c>Failed</c> state with
    /// the maximum retry count so it will not be re-picked up by the normal retry path.
    /// </summary>
    /// <returns><see langword="true"/> if a new row was inserted or an existing non-terminal row was updated.</returns>
    public async ValueTask<bool> StoreReceivedExceptionMessageAsync(
        string name,
        string consumerIdentity,
        string content,
        string? exceptionInfo = null,
        CancellationToken cancellationToken = default
    )
    {
        var origin = _serializer.Deserialize(content)!;
        return await StoreReceivedExceptionMessageAsync(
                name,
                consumerIdentity,
                new MediumMessage
                {
                    StorageId = Guid.Empty,
                    Origin = origin,
                    Content = content,
                    Lane = MessageLane.Bus,
                },
                exceptionInfo,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Stores a received message that failed before it could be dispatched, using a pre-built
    /// <c>MediumMessage</c>. The row is written directly into the <c>Failed</c> state with the maximum
    /// retry count so it will not be re-picked up by the normal retry path.
    /// </summary>
    /// <returns><see langword="true"/> if a new row was inserted or an existing non-terminal row was updated.</returns>
    public async ValueTask<bool> StoreReceivedExceptionMessageAsync(
        string name,
        string consumerIdentity,
        MediumMessage message,
        string? exceptionInfo = null,
        CancellationToken cancellationToken = default
    )
    {
        var row = new ReceivedRow(
            _guidGenerator.Create(),
            name,
            consumerIdentity,
            string.IsNullOrEmpty(message.Content) ? _serializer.Serialize(message.Origin) : message.Content,
            message.Lane,
            message.Origin.Id,
            Retries: Options.RetryPolicy.MaxPersistedRetries,
            message.InlineAttempts,
            StatusName.Failed,
            exceptionInfo,
            ExpiresAfter: TimeSpan.FromSeconds(Options.FailedMessageExpiredAfter),
            DueAfter: null
        );

        var stored = await _StoreReceivedAsync("messaging.store_received_exception_message", row, cancellationToken)
            .ConfigureAwait(false);
        return stored is not null;
    }

    /// <summary>
    /// Persists an inbound message to the received table. Concurrent broker redeliveries of the same message to one
    /// consumer collapse to one row on <c>(Version, MessageId, ConsumerIdentity, IntentType)</c>, while each consumer
    /// that receives the message keeps its own row; the terminal-row guard ensures already-completed rows are never
    /// overwritten.
    /// </summary>
    /// <returns>The stored <c>MediumMessage</c> with its generated <c>StorageId</c> and timestamps populated.</returns>
    public async ValueTask<MediumMessage> StoreReceivedMessageAsync(
        string name,
        string consumerIdentity,
        MediumMessage message,
        CancellationToken cancellationToken = default
    )
    {
        // A guard-blocked redelivery writes nothing, so its snapshot keeps these application-clock values; the
        // stored row's own times are adopted below whenever the store wrote it.
        var added = _timeProvider.GetUtcNow();
        var mediumMessage = new MediumMessage
        {
            StorageId = _guidGenerator.Create(),
            Origin = message.Origin,
            Content = _serializer.Serialize(message.Origin),
            Lane = message.Lane,
            Added = added,
            ExpiresAt = null,
            NextRetryAt = added.Add(Options.RetryPolicy.InitialDispatchGrace),
            LockedUntil = null,
            Owner = null,
            Retries = 0,
            InlineAttempts = 0,
        };

        var row = new ReceivedRow(
            mediumMessage.StorageId,
            name,
            consumerIdentity,
            mediumMessage.Content,
            mediumMessage.Lane,
            message.Origin.Id,
            Retries: 0,
            InlineAttempts: 0,
            StatusName.Scheduled,
            ExceptionInfo: null,
            ExpiresAfter: null,
            DueAfter: Options.RetryPolicy.InitialDispatchGrace
        );

        // Adopt the authoritative persisted row id: a redelivery that rewrites an existing row keeps that row's id, so
        // the freshly generated StorageId would be stale and the caller's later state change (by id) would no-op.
        if (
            await _StoreReceivedAsync("messaging.store_received_message", row, cancellationToken)
                .ConfigureAwait(false) is
            { } stored
        )
        {
            mediumMessage.StorageId = stored.Id;
            mediumMessage.Added = stored.Added;
            mediumMessage.NextRetryAt = stored.NextRetryAt;
        }

        return mediumMessage;
    }

    /// <summary>
    /// Persists an inbound message built from a raw <c>Message</c> payload to the received table.
    /// Convenience overload that wraps <paramref name="message"/> in a <c>MediumMessage</c> before storing.
    /// </summary>
    /// <returns>The stored <c>MediumMessage</c> with its generated <c>StorageId</c> and timestamps populated.</returns>
    public ValueTask<MediumMessage> StoreReceivedMessageAsync(
        string name,
        string consumerIdentity,
        Message message,
        CancellationToken cancellationToken = default
    )
    {
        return StoreReceivedMessageAsync(
            name,
            consumerIdentity,
            new MediumMessage
            {
                StorageId = Guid.Empty,
                Origin = message,
                Content = string.Empty,
                Lane = MessageLane.Bus,
            },
            cancellationToken
        );
    }

    /// <summary>A received row as a store writes it.</summary>
    /// <param name="ExpiresAfter">How long past the database clock the row expires, or <see langword="null"/> for never.</param>
    /// <param name="DueAfter">How long past the database clock the row falls due, or <see langword="null"/> for never.</param>
    private sealed record ReceivedRow(
        Guid Id,
        string Name,
        string ConsumerIdentity,
        string Content,
        MessageLane Lane,
        string MessageId,
        int Retries,
        int InlineAttempts,
        StatusName Status,
        string? ExceptionInfo,
        TimeSpan? ExpiresAfter,
        TimeSpan? DueAfter
    );

    /// <summary>Inserts a received message, or rewrites its redelivered non-terminal, unleased row.</summary>
    /// <returns>The written row's id and database-stamped times, or <see langword="null"/> when a guard refused it.</returns>
    /// <remarks>
    /// The row's identity is a partial unique index over non-inbox rows keyed by the consumer it was delivered to, so a
    /// redelivery to the same consumer finds the same row. The first delivery is one insert. A
    /// redelivery locks the existing row and rewrites it only while it is neither terminal (a redelivered message must
    /// not turn a Succeeded row back to Failed and fire OnExhausted again) nor leased (releasing the lease of an
    /// in-flight attempt would let the retry processor pick the row up mid-attempt). The rewrite never resets the
    /// durable retry counters.
    /// </remarks>
    private async ValueTask<(Guid Id, DateTimeOffset Added, DateTimeOffset? NextRetryAt)?> _StoreReceivedAsync(
        string operation,
        ReceivedRow row,
        CancellationToken cancellationToken
    )
    {
        var expiresAt = row.ExpiresAfter is null
            ? "NULL"
            : _dialect.ShiftByDuration(SqlDialectTokens.Now, "ExpiresAfter");
        var nextRetryAt = row.DueAfter is null ? "NULL" : _dialect.ShiftByDuration(SqlDialectTokens.Now, "DueAfter");
        SqlKeyColumn[] identity =
        [
            new(_t.Version, "Version"),
            new(_t.MessageId, "MessageId"),
            new(_t.ConsumerIdentity, "ConsumerIdentity"),
            new(_t.IntentType, "IntentType"),
        ];
        var nonInbox = $"{_t.IsInboxRecord} = {_t.False}";

        var insertSql = _dialect.Render(
            new SqlInsertIfAbsent(
                _receivedTable,
                identity,
                [
                    _t.Id,
                    _t.Name,
                    _t.Content,
                    _t.Retries,
                    _t.InlineAttempts,
                    _t.Added,
                    _t.ExpiresAt,
                    _t.NextRetryAt,
                    _t.LockedUntil,
                    _t.Owner,
                    _t.StatusName,
                    _t.ExceptionInfo,
                ],
                [
                    "@Id",
                    "@Name",
                    "@Content",
                    "@Retries",
                    "@InlineAttempts",
                    SqlDialectTokens.Now,
                    expiresAt,
                    nextRetryAt,
                    "NULL",
                    "NULL",
                    "@StatusName",
                    "@ExceptionInfo",
                ],
                [_t.Id, _t.Added, _t.NextRetryAt],
                nonInbox
            )
        );
        var lockSql = _dialect.Render(new SqlLockedRead(_receivedTable, identity, [_t.Id], nonInbox));
        var rewriteSql = _dialect.Render(
            new SqlFencedTransition(
                _receivedTable,
                [new SqlKeyColumn(_t.Id, "Id")],
                $"{nonInbox} AND {_terminalGuard} AND ({_t.LockedUntil} IS NULL OR {_t.LockedUntil} <= {SqlDialectTokens.Now})",
                $"{_t.StatusName}=@StatusName, {_t.ExpiresAt}={expiresAt}, {_t.NextRetryAt}={nextRetryAt}, {_t.LockedUntil}=NULL, {_t.Owner}=NULL, {_t.Content}=@Content, {_t.ExceptionInfo}=@ExceptionInfo",
                [_t.Id, _t.Added, _t.NextRetryAt]
            )
        );

        void bindIdentity(DbCommand command)
        {
            _BindVersion(command);
            _dialect.AddParameter(command, "MessageId", _MessageIdType, row.MessageId);
            _dialect.AddParameter(command, "ConsumerIdentity", _IdentityType, row.ConsumerIdentity);
            _dialect.AddParameter(
                command,
                "IntentType",
                SqlColumnType.Int16,
                MessageLaneCompatibility.ToPersistedValue(row.Lane)
            );
        }

        void bindValues(DbCommand command)
        {
            _dialect.AddParameter(command, "Content", _ContentType, row.Content);
            _dialect.AddParameter(command, "StatusName", _StatusType, row.Status.ToString("G"));
            _dialect.AddParameter(command, "ExceptionInfo", _ContentType, row.ExceptionInfo);

            if (row.ExpiresAfter is { } expiresAfter)
            {
                _dialect.AddDuration(command, "ExpiresAfter", expiresAfter);
            }

            if (row.DueAfter is { } dueAfter)
            {
                _dialect.AddDuration(command, "DueAfter", dueAfter);
            }
        }

        for (var attempt = 1; ; attempt++)
        {
            await using (var connection = _CreateConnection())
            {
                // Autocommit: a first delivery is the single insert, and the insert waits out a concurrent insert of
                // the same identity instead of failing on it.
                var (inserted, insertedRow) = await RelationalCommand
                    .ExecuteReaderAsync(
                        connection,
                        transaction: null,
                        insertSql,
                        CommandTimeoutSeconds,
                        command =>
                        {
                            bindIdentity(command);
                            bindValues(command);
                            _dialect.AddParameter(command, "Id", SqlColumnType.Guid, row.Id);
                            _dialect.AddParameter(command, "Name", _NameType, row.Name);
                            _dialect.AddParameter(command, "Retries", SqlColumnType.Int32, row.Retries);
                            _dialect.AddParameter(command, "InlineAttempts", SqlColumnType.Int32, row.InlineAttempts);
                        },
                        _ReadStoredReceivedAsync,
                        cancellationToken
                    )
                    .ConfigureAwait(false);

                if (inserted)
                {
                    return insertedRow;
                }
            }

            var (found, stored) = await SqlAutonomousTransaction
                .RunAsync(
                    operation,
                    _CreateConnection,
                    async (connection, transaction, ct) =>
                    {
                        // The locking read waits out any other writer of the row and finds its id, which the
                        // transition is keyed by.
                        var storedId = await RelationalCommand
                            .ExecuteReaderAsync(
                                connection,
                                transaction,
                                lockSql,
                                CommandTimeoutSeconds,
                                bindIdentity,
                                static async (reader, token) =>
                                    await reader.ReadAsync(token).ConfigureAwait(false)
                                        ? reader.GetGuid(0)
                                        : (Guid?)null,
                                ct
                            )
                            .ConfigureAwait(false);

                        if (storedId is not { } id)
                        {
                            return (Found: false, Stored: ((Guid, DateTimeOffset, DateTimeOffset?)?)null);
                        }

                        var (transitioned, transitionedRow) = await RelationalCommand
                            .ExecuteReaderAsync(
                                connection,
                                transaction,
                                rewriteSql,
                                CommandTimeoutSeconds,
                                command =>
                                {
                                    bindValues(command);
                                    _dialect.AddParameter(command, "Id", SqlColumnType.Guid, id);
                                },
                                _ReadStoredReceivedAsync,
                                ct
                            )
                            .ConfigureAwait(false);

                        return (Found: true, Stored: transitioned ? transitionedRow : null);
                    },
                    _timeProvider,
                    cancellationToken
                )
                .ConfigureAwait(false);

            if (found || attempt == _ReceivedStoreAttempts)
            {
                return stored;
            }
        }
    }

    private static async Task<(bool Applied, (Guid, DateTimeOffset, DateTimeOffset?)? Row)> _ReadStoredReceivedAsync(
        DbDataReader reader,
        CancellationToken cancellationToken
    )
    {
        if (!await _ReadAppliedAsync(reader, cancellationToken).ConfigureAwait(false))
        {
            return (false, null);
        }

        return (
            true,
            (
                reader.GetGuid(1),
                await reader.GetFieldValueAsync<DateTimeOffset>(2, cancellationToken).ConfigureAwait(false),
                await _ReadInstantAsync(reader, 3, cancellationToken).ConfigureAwait(false)
            )
        );
    }

    /// <summary>Deletes a single received message by its storage identifier.</summary>
    /// <returns>1 if the row was deleted; 0 if not found.</returns>
    public ValueTask<int> DeleteReceivedMessageAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _DeleteByIdsAsync(_receivedTable, [id], cancellationToken);
    }

    /// <summary>Deletes a single published message by its storage identifier.</summary>
    /// <returns>1 if the row was deleted; 0 if not found.</returns>
    public ValueTask<int> DeletePublishedMessageAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _DeleteByIdsAsync(_publishedTable, [id], cancellationToken);
    }

    /// <summary>Deletes a batch of received messages by their storage identifiers.</summary>
    /// <returns>The number of rows deleted.</returns>
    public ValueTask<int> DeleteReceivedMessagesAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken = default
    )
    {
        return _DeleteByIdsAsync(_receivedTable, ids, cancellationToken);
    }

    /// <summary>Deletes a batch of published messages by their storage identifiers.</summary>
    /// <returns>The number of rows deleted.</returns>
    public ValueTask<int> DeletePublishedMessagesAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken = default
    )
    {
        return _DeleteByIdsAsync(_publishedTable, ids, cancellationToken);
    }

    private async ValueTask<int> _DeleteByIdsAsync(
        string table,
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken
    )
    {
        if (ids.Count == 0)
        {
            return 0;
        }

        // One list parameter keeps the statement text, and its cached plan, the same whatever the count.
        var sql = $"DELETE FROM {table} WHERE {_dialect.InList(_t.Id, "Ids", SqlColumnType.Guid)};";

        await using var connection = _CreateConnection();
        return await RelationalCommand
            .ExecuteNonQueryAsync(
                connection,
                transaction: null,
                sql,
                CommandTimeoutSeconds,
                command => _dialect.AddListParameter(command, "Ids", SqlColumnType.Guid, ids),
                cancellationToken
            )
            .ConfigureAwait(false);
    }
}
