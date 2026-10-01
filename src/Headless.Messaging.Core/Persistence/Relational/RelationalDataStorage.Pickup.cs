// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Sql;

namespace Headless.Messaging.Persistence;

#pragma warning disable CA2100 // SQL text is rendered from dialect output, table names, and fixed fragments; every value is a parameter.

internal sealed partial class RelationalDataStorage
{
    /// <summary>
    /// Fetches published messages eligible for retry dispatch, leasing them in the statement that selects them so no
    /// second replica can dispatch the same row.
    /// </summary>
    public ValueTask<IEnumerable<MediumMessage>> GetPublishedMessagesOfNeedRetryAsync(
        MessageLane lane,
        CancellationToken cancellationToken = default
    )
    {
        return _GetMessagesOfNeedRetryAsync(_publishedTable, lane, orphaned: false, cancellationToken);
    }

    /// <summary>
    /// Fetches received messages eligible for retry dispatch, leasing them in the statement that selects them so no
    /// second replica can dispatch the same row.
    /// </summary>
    public ValueTask<IEnumerable<MediumMessage>> GetReceivedMessagesOfNeedRetryAsync(
        MessageLane lane,
        CancellationToken cancellationToken = default
    )
    {
        return _GetMessagesOfNeedRetryAsync(_receivedTable, lane, orphaned: false, cancellationToken);
    }

    public ValueTask<IEnumerable<MediumMessage>> GetReceivedInboxOrphansOfNeedRetryAsync(
        MessageLane lane,
        CancellationToken cancellationToken = default
    ) => _GetMessagesOfNeedRetryAsync(_receivedTable, lane, orphaned: true, cancellationToken);

    private async ValueTask<IEnumerable<MediumMessage>> _GetMessagesOfNeedRetryAsync(
        string table,
        MessageLane lane,
        bool orphaned,
        CancellationToken cancellationToken
    )
    {
        var received = _IsReceived(table);
        var orphanFilter = received ? $" AND {_t.IsInboxOrphaned} = {(orphaned ? _t.True : _t.False)}" : string.Empty;
        // A received row starts a new inbox attempt with each claim, so a stale attempt is fenced out.
        var attemptAssignment = received
            ? $", {_t.AttemptId} = CASE WHEN {_t.IsInboxRecord} = {_t.True} THEN {_dialect.NewGuid()} ELSE NULL END"
            : string.Empty;
        string[] returning = received ? [.. _RetryClaimColumns(), .. _InboxClaimColumns()] : _RetryClaimColumns();

        // One claim statement selects the due rows in the requested recognized lane, skipping rows another
        // replica holds, and leases them as it returns them, so no second replica can pass the same filter
        // between a read and a lease. Unknown lanes are excluded inside the filter, so they neither consume
        // capacity nor get mutated.
        //
        // Due time and lease are both the database's: NextRetryAt is compared against the same clock that stamps
        // the lease, so a replica whose clock is skewed neither picks a row up early nor leaves it waiting.
        var sql = _dialect.Render(
            new SqlClaimNext(
                table,
                [_t.Id],
                $"{_t.Retries} <= @Retries AND {_t.Version} = @Version AND {_t.IntentType} = @IntentType AND {_t.NextRetryAt} IS NOT NULL AND {_t.NextRetryAt} <= {SqlDialectTokens.Now} AND ({_t.LockedUntil} IS NULL OR {_t.LockedUntil} <= {SqlDialectTokens.Now}){orphanFilter} AND {_terminalGuard}",
                [_t.NextRetryAt, _t.Id],
                $"{_t.LockedUntil} = {_dialect.ShiftByDuration(SqlDialectTokens.Now, "Lease")}, {_t.Owner} = @Owner{attemptAssignment}",
                returning,
                BatchSizeParameter: "BatchSize"
            )
        );

        return await SqlAutonomousTransaction
            .RunAsync(
                _CreateConnection,
                async (connection, transaction, ct) =>
                {
                    var poisonMessages = new List<PoisonMessage>();
                    var claimed = await RelationalCommand
                        .ExecuteReaderAsync(
                            connection,
                            transaction,
                            sql,
                            CommandTimeoutSeconds,
                            command =>
                            {
                                _dialect.AddParameter(
                                    command,
                                    "BatchSize",
                                    SqlColumnType.Int32,
                                    orphaned ? Options.OrphanProbeBatchSize : Options.RetryBatchSize
                                );
                                _dialect.AddParameter(
                                    command,
                                    "Retries",
                                    SqlColumnType.Int32,
                                    Options.RetryPolicy.MaxPersistedRetries
                                );
                                _BindVersion(command);
                                _dialect.AddParameter(
                                    command,
                                    "IntentType",
                                    SqlColumnType.Int16,
                                    MessageLaneCompatibility.ToPersistedValue(lane)
                                );
                                _dialect.AddDuration(command, "Lease", Options.RetryPolicy.DispatchTimeout);
                                _BindOwner(command, "Owner", hasLease: true);
                            },
                            (reader, token) =>
                                _ReadRetryClaimAsync(reader, table, lane, received, poisonMessages, token),
                            ct
                        )
                        .ConfigureAwait(false);

                    await _MarkPoisonMessagesTerminalAsync(connection, transaction, table, poisonMessages, ct)
                        .ConfigureAwait(false);

                    return (IEnumerable<MediumMessage>)claimed;
                },
                _timeProvider,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private string[] _RetryClaimColumns() =>
        [
            _t.Id,
            _t.Content,
            _t.IntentType,
            _t.Retries,
            _t.InlineAttempts,
            _t.Added,
            _t.NextRetryAt,
            _t.LockedUntil,
            _t.Owner,
        ];

    private string[] _InboxClaimColumns() =>
        [
            _t.IsInboxRecord,
            _t.TenantPresent,
            _t.TenantId,
            _t.MessageId,
            _t.ContractIdentity,
            _t.ContractVersion,
            _t.ConsumerIdentity,
            _t.Generation,
            _t.GenerationIncarnationId,
            _t.AttemptId,
            _t.IsInboxOrphaned,
        ];

    private async Task<List<MediumMessage>> _ReadRetryClaimAsync(
        DbDataReader reader,
        string table,
        MessageLane lane,
        bool received,
        List<PoisonMessage> poisonMessages,
        CancellationToken cancellationToken
    )
    {
        var messages = new List<MediumMessage>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var storageId = reader.GetGuid(0);
            var content = reader.GetString(1);
            var persistedLane = MessageLaneCompatibility.FromPersistedValue(reader.GetInt16(2));
            if (persistedLane != lane)
            {
                throw new InvalidOperationException(
                    $"Retry pickup for lane '{lane}' returned persisted lane '{persistedLane}'."
                );
            }

            MediumMessage mediumMessage;
            try
            {
                mediumMessage = new MediumMessage
                {
                    StorageId = storageId,
                    Origin = _serializer.Deserialize(content)!,
                    Content = content,
                    Lane = persistedLane,
                    Retries = reader.GetInt32(3),
                    InlineAttempts = reader.GetInt32(4),
#pragma warning disable CA1849, VSTHRD103, AsyncFixer02, MA0042 // the GetString(1) above already pulls the large Content column synchronously, so these remaining small columns cannot add blocking this row has not already paid for.
                    Added = reader.GetFieldValue<DateTimeOffset>(5),
                    NextRetryAt = reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
                    LockedUntil = reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
                    Owner = reader.IsDBNull(8) ? null : reader.GetString(8),
#pragma warning restore CA1849, VSTHRD103, AsyncFixer02, MA0042
                };

                if (received && reader.GetBoolean(9))
                {
                    var generation = reader.GetInt64(16);
                    var incarnationId = reader.GetGuid(17);
                    var lockedUntil =
                        mediumMessage.LockedUntil
                        ?? throw new InvalidOperationException("Claimed inbox row has no durable lease deadline.");
                    var attemptId = reader.GetGuid(18);
                    mediumMessage.InboxKey = new InboxKey(
                        reader.GetBoolean(10) ? reader.GetString(11) : null,
                        reader.GetString(12),
                        persistedLane,
                        reader.GetString(13),
                        reader.GetString(14),
                        reader.GetString(15),
                        generation
                    );
                    mediumMessage.InboxGeneration = new InboxGeneration(generation, incarnationId);
                    mediumMessage.InboxAttemptFence = new InboxAttemptFence(
                        storageId,
                        persistedLane,
                        generation,
                        incarnationId,
                        attemptId,
                        mediumMessage.Owner,
                        lockedUntil
                    );
                    mediumMessage.IsInboxOrphaned = reader.GetBoolean(19);
                }
            }
#pragma warning disable CA1031 // deliberately broad: one un-deserializable row must not abort or starve the batch
            catch (Exception ex)
#pragma warning restore CA1031
            {
                _logger.LogPoisonMessageSkipped(storageId, table, ex);
                poisonMessages.Add(_CreatePoisonMessage(storageId, ex));
                continue;
            }

            messages.Add(mediumMessage);
        }

        return messages;
    }

    /// <summary>
    /// Marks the rows a claim could not deserialize as Failed, so they stop occupying the claim, inside a savepoint
    /// of the claim's transaction: if marking fails, the healthy rows' claim still commits and the poison rows stay
    /// leased until their lease expires.
    /// </summary>
    private async ValueTask _MarkPoisonMessagesTerminalAsync(
        DbConnection connection,
        DbTransaction transaction,
        string table,
        IReadOnlyList<PoisonMessage> poisonMessages,
        CancellationToken cancellationToken
    )
    {
        if (poisonMessages.Count == 0)
        {
            return;
        }

        // Expiry is decided against the database clock by the purge, so the database clock also stamps it.
        var received = _IsReceived(table);
        var set =
            $"{_t.StatusName}=@StatusName, {_t.NextRetryAt}=NULL, {_t.LockedUntil}=NULL, {_t.Owner}=NULL, {_t.ExpiresAt}={_dialect.ShiftByDuration(SqlDialectTokens.Now, "ExpiresAfter")}";
        if (received)
        {
            // Each poison row records its own failure; the inbox generation ends, and its retention starts.
            var exceptions = string.Join(
                " ",
                poisonMessages.Select(
                    (_, index) =>
                        string.Create(CultureInfo.InvariantCulture, $"WHEN @Poison{index} THEN @PoisonInfo{index}")
                )
            );
            set +=
                $", {_t.ExceptionInfo}=CASE {_t.Id} {exceptions} END, {_t.AttemptId}=CASE WHEN {_t.IsInboxRecord} = {_t.True} THEN NULL ELSE {_t.AttemptId} END, {_t.TerminalAt}=CASE WHEN {_t.IsInboxRecord} = {_t.True} THEN {SqlDialectTokens.Now} ELSE {_t.TerminalAt} END, {_t.EffectiveExpiresAt}=CASE WHEN {_t.IsInboxRecord} = {_t.True} THEN {_dialect.ShiftBySeconds(SqlDialectTokens.Now, _t.InboxRetentionSeconds)} ELSE {_t.EffectiveExpiresAt} END";
        }

        var sql = _dialect.Render(
            new SqlClockedStatement(
                $"UPDATE {table} SET {set} WHERE {_dialect.InList(_t.Id, "Ids", SqlColumnType.Guid)} AND {_terminalGuard};"
            )
        );

        const string savepointName = "headless_poison_mark_batch";
        await transaction.SaveAsync(savepointName, cancellationToken).ConfigureAwait(false);

        try
        {
            await RelationalCommand
                .ExecuteNonQueryAsync(
                    connection,
                    transaction,
                    sql,
                    CommandTimeoutSeconds,
                    command =>
                    {
                        _dialect.AddListParameter(
                            command,
                            "Ids",
                            SqlColumnType.Guid,
                            poisonMessages.Select(static message => message.StorageId).ToArray()
                        );
                        _dialect.AddParameter(command, "StatusName", _StatusType, nameof(StatusName.Failed));
                        _dialect.AddDuration(
                            command,
                            "ExpiresAfter",
                            TimeSpan.FromSeconds(Options.FailedMessageExpiredAfter)
                        );

                        if (!received)
                        {
                            return;
                        }

                        for (var index = 0; index < poisonMessages.Count; index++)
                        {
                            var suffix = index.ToString(CultureInfo.InvariantCulture);
                            _dialect.AddParameter(
                                command,
                                "Poison" + suffix,
                                SqlColumnType.Guid,
                                poisonMessages[index].StorageId
                            );
                            _dialect.AddParameter(
                                command,
                                "PoisonInfo" + suffix,
                                _ContentType,
                                poisonMessages[index].ExceptionInfo
                            );
                        }
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);
            await transaction.ReleaseAsync(savepointName, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            foreach (var poisonMessage in poisonMessages)
            {
                _logger.LogPoisonMessageTerminalMarkFailed(poisonMessage.StorageId, table, ex);
            }

            await transaction.RollbackAsync(savepointName, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Shortens the remaining lease on published messages owned by nodes in <paramref name="deadOwners"/>
    /// by setting <c>LockedUntil</c> to the current time, allowing live replicas to re-claim them.
    /// </summary>
    /// <returns>The number of rows whose leases were reclaimed.</returns>
    public ValueTask<int> ReclaimDeadPublishedOwnersAsync(
        IReadOnlyCollection<string> deadOwners,
        CancellationToken cancellationToken = default
    )
    {
        return _ReclaimDeadOwnersAsync(_publishedTable, deadOwners, cancellationToken);
    }

    /// <summary>
    /// Shortens the remaining lease on received messages owned by nodes in <paramref name="deadOwners"/>
    /// by setting <c>LockedUntil</c> to the current time, allowing live replicas to re-claim them.
    /// </summary>
    /// <returns>The number of rows whose leases were reclaimed.</returns>
    public ValueTask<int> ReclaimDeadReceivedOwnersAsync(
        IReadOnlyCollection<string> deadOwners,
        CancellationToken cancellationToken = default
    )
    {
        return _ReclaimDeadOwnersAsync(_receivedTable, deadOwners, cancellationToken);
    }

    private async ValueTask<int> _ReclaimDeadOwnersAsync(
        string table,
        IReadOnlyCollection<string> deadOwners,
        CancellationToken cancellationToken
    )
    {
        // An empty list matches no row, so the early return only skips the round trip.
        if (deadOwners.Count == 0)
        {
            return 0;
        }

        // Intentionally version-agnostic: reclaim only shortens leases on rows owned by dead node incarnations, then
        // the normal version-filtered pickup decides what this service version may dispatch. One list parameter keeps
        // the update's text, and its cached plan, the same whatever the number of dead owners.
        var sql = _dialect.Render(
            new SqlClockedStatement(
                $"UPDATE {table} SET {_t.LockedUntil} = {SqlDialectTokens.Now} WHERE {_t.Owner} IS NOT NULL AND {_dialect.InList(_t.Owner, "DeadOwners", _ownerType)} AND {_t.LockedUntil} > {SqlDialectTokens.Now} AND {_t.IntentType} IN (0, 1) AND {_terminalGuard};"
            )
        );

        return await SqlAutonomousTransaction
            .RunAsync(
                _CreateConnection,
                async (connection, transaction, ct) =>
                {
                    // The dead owners' rows are locked first, waiting out any writer that holds one, so the update
                    // reads the database clock after that wait and decides which leases are still live on it.
                    foreach (var chunk in deadOwners.Chunk(_MaxCommandParameters))
                    {
                        var lockSql = string.Join(
                            "\n",
                            chunk.Select(
                                (_, i) =>
                                    _dialect.Render(
                                        new SqlLockedRead(
                                            table,
                                            [
                                                new SqlKeyColumn(
                                                    _t.Owner,
                                                    "Owner" + i.ToString(CultureInfo.InvariantCulture)
                                                ),
                                            ],
                                            [_t.Id],
                                            $"{_t.Owner} IS NOT NULL"
                                        )
                                    )
                            )
                        );

                        await RelationalCommand
                            .ExecuteNonQueryAsync(
                                connection,
                                transaction,
                                lockSql,
                                CommandTimeoutSeconds,
                                command =>
                                {
                                    for (var i = 0; i < chunk.Length; i++)
                                    {
                                        _dialect.AddParameter(
                                            command,
                                            "Owner" + i.ToString(CultureInfo.InvariantCulture),
                                            _ownerType,
                                            chunk[i]
                                        );
                                    }
                                },
                                ct
                            )
                            .ConfigureAwait(false);
                    }

                    return await RelationalCommand
                        .ExecuteNonQueryAsync(
                            connection,
                            transaction,
                            sql,
                            CommandTimeoutSeconds,
                            command => _dialect.AddListParameter(command, "DeadOwners", _ownerType, deadOwners),
                            ct
                        )
                        .ConfigureAwait(false);
                },
                _timeProvider,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes expired terminal messages from the specified table in batches.
    /// Only rows in <c>Succeeded</c> or <c>Failed</c> state with <c>ExpiresAt</c> before
    /// <paramref name="timeout"/> and no pending retry (<c>NextRetryAt IS NULL</c>) are removed.
    /// </summary>
    /// <returns>The number of rows deleted.</returns>
    public async ValueTask<int> DeleteExpiresAsync(
        string table,
        DateTimeOffset timeout,
        int batchCount = 1000,
        CancellationToken cancellationToken = default
    )
    {
        if (_IsReceived(table))
        {
            return await _DeleteExpiredReceivedAsync(timeout, batchCount, cancellationToken).ConfigureAwait(false);
        }

        // The cutoff is the caller's retention decision, so it stays a parameter rather than the database clock. The
        // purge skips locked rows, which SQL Server allows only at READ COMMITTED, so it runs in a transaction that
        // sets the level itself.
        var sql = _dialect.Render(
            new SqlDeleteBatch(
                table,
                [_t.Id],
                $"{_t.IntentType} IN (0, 1) AND {_t.ExpiresAt} < @Timeout AND {_t.StatusName} IN ('{nameof(StatusName.Succeeded)}','{nameof(StatusName.Failed)}') AND {_t.NextRetryAt} IS NULL",
                "BatchCount"
            )
        );

        return await SqlAutonomousTransaction
            .RunAsync(
                _CreateConnection,
                (connection, transaction, ct) =>
                    RelationalCommand.ExecuteNonQueryAsync(
                        connection,
                        transaction,
                        sql,
                        CommandTimeoutSeconds,
                        command =>
                        {
                            _dialect.AddParameter(command, "Timeout", SqlColumnType.Timestamp, timeout);
                            _dialect.AddParameter(command, "BatchCount", SqlColumnType.Int32, batchCount);
                        },
                        ct
                    ),
                _timeProvider,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes expired received rows, writing a cleanup receipt and audit for every inbox generation it removes, so an
    /// inbox's history records why a generation is gone.
    /// </summary>
    /// <remarks>
    /// A plain row expires at the caller's cutoff; an inbox generation expires at its own retention deadline, on the
    /// database clock, unless an operator holds it. Each kind is selected by its own statement so each reads its own
    /// expiry index, and both skip rows another transaction holds.
    /// </remarks>
    private async ValueTask<int> _DeleteExpiredReceivedAsync(
        DateTimeOffset timeout,
        int batchCount,
        CancellationToken cancellationToken
    )
    {
        string[] columns =
        [
            _t.Id,
            _t.IsInboxRecord,
            _t.GenerationIncarnationId,
            _t.StatusName,
            _t.ConsumerIdentity,
            _t.IntentType,
            _t.EffectiveExpiresAt,
        ];
        var terminal =
            $"{_t.StatusName} IN ('{nameof(StatusName.Succeeded)}','{nameof(StatusName.Failed)}') AND {_t.NextRetryAt} IS NULL AND {_t.IntentType} IN (0, 1)";
        var plainSql = _dialect.Render(
            new SqlLockBatch(
                _receivedTable,
                columns,
                $"{_t.IsInboxRecord} = {_t.False} AND {_t.ExpiresAt} < @Timeout AND {terminal}",
                [_t.ExpiresAt, _t.Id],
                "BatchCount"
            )
        );
        var inboxSql = _dialect.Render(
            new SqlLockBatch(
                _receivedTable,
                columns,
                $"{_t.IsInboxRecord} = {_t.True} AND {_t.IsHeld} = {_t.False} AND {_t.EffectiveExpiresAt} < @Instant AND {terminal}",
                [_t.EffectiveExpiresAt, _t.Id],
                "BatchCount"
            )
        );

        var deleted = await SqlAutonomousTransaction
            .RunAsync(
                _CreateConnection,
                async (connection, transaction, ct) =>
                {
                    var now = await _ReadNowAsync(connection, transaction, ct).ConfigureAwait(false);
                    var plain = await _ReadExpiredReceivedAsync(
                            connection,
                            transaction,
                            plainSql,
                            command => _dialect.AddParameter(command, "Timeout", SqlColumnType.Timestamp, timeout),
                            batchCount,
                            ct
                        )
                        .ConfigureAwait(false);
                    var inbox = await _ReadExpiredReceivedAsync(
                            connection,
                            transaction,
                            inboxSql,
                            command => _dialect.AddParameter(command, "Instant", SqlColumnType.Timestamp, now),
                            batchCount,
                            ct
                        )
                        .ConfigureAwait(false);
                    // Both selections are bounded by the batch, then merged in expiry order and trimmed to it; a row
                    // locked past the trim stays where it is and is unlocked when this transaction ends.
                    var candidates = plain
                        .Concat(inbox)
                        .OrderBy(static row => row.EffectiveExpiresAt ?? DateTimeOffset.MaxValue)
                        .ThenBy(static row => row.Id)
                        .Take(batchCount)
                        .ToList();

                    if (candidates.Count == 0)
                    {
                        return candidates;
                    }

                    await _WriteCleanupHistoryAsync(
                            connection,
                            transaction,
                            [.. candidates.Where(static row => row.IsInbox)],
                            now,
                            ct
                        )
                        .ConfigureAwait(false);
                    await _DeleteLockedAsync(
                            connection,
                            transaction,
                            _receivedTable,
                            _t.Id,
                            candidates.Select(static row => row.Id).ToArray(),
                            ct
                        )
                        .ConfigureAwait(false);

                    return candidates;
                },
                _timeProvider,
                cancellationToken
            )
            .ConfigureAwait(false);

        foreach (var row in deleted.Where(static row => row.IsInbox))
        {
            MessagingMetrics.RecordInbox(
                InboxMetricKind.Retention,
                row.ConsumerIdentity,
                row.Lane,
                InboxMetricOutcome.Expired,
                Options.RequiredInboxCapability,
                _storage.ProviderName
            );
        }

        return deleted.Count;
    }

    private async Task<List<ExpiredReceivedRow>> _ReadExpiredReceivedAsync(
        DbConnection connection,
        DbTransaction transaction,
        string sql,
        Action<DbCommand> bindCutoff,
        int batchCount,
        CancellationToken cancellationToken
    )
    {
        return await RelationalCommand
            .ExecuteReaderAsync(
                connection,
                transaction,
                sql,
                CommandTimeoutSeconds,
                command =>
                {
                    bindCutoff(command);
                    _dialect.AddParameter(command, "BatchCount", SqlColumnType.Int32, batchCount);
                },
                static async (reader, ct) =>
                {
                    var rows = new List<ExpiredReceivedRow>();
                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        rows.Add(
                            new ExpiredReceivedRow(
                                reader.GetGuid(0),
                                reader.GetBoolean(1),
                                await reader.IsDBNullAsync(2, ct).ConfigureAwait(false) ? null : reader.GetGuid(2),
                                reader.GetString(3),
                                reader.GetString(4),
                                MessageLaneCompatibility.FromPersistedValue(reader.GetInt16(5)),
                                await _ReadInstantAsync(reader, 6, ct).ConfigureAwait(false)
                            )
                        );
                    }

                    return rows;
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Writes one cleanup receipt and its audit per expired inbox generation, chunked so a command stays under the
    /// engine's parameter limit.
    /// </summary>
    private async Task _WriteCleanupHistoryAsync(
        DbConnection connection,
        DbTransaction transaction,
        IReadOnlyList<ExpiredReceivedRow> rows,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        // Each row binds five parameters; a VALUES list is also capped at 1,000 rows on SQL Server.
        const int chunkSize = 300;
        foreach (var chunk in rows.Chunk(chunkSize))
        {
            var receipts = new List<string>(chunk.Length);
            var audits = new List<string>(chunk.Length);
            for (var index = 0; index < chunk.Length; index++)
            {
                var i = index.ToString(CultureInfo.InvariantCulture);
                receipts.Add(
                    $"(@Op{i},@Incarnation{i},'{nameof(MessagingOperationType.Cleanup)}',@Status{i},'{_CleanupActor}','{_CleanupReason}','{nameof(InboxOperationOutcome.Applied)}',@Storage{i},@Instant)"
                );
                audits.Add(
                    $"(@Audit{i},@Op{i},@Incarnation{i},'{nameof(MessagingOperationType.Cleanup)}','{_CleanupActor}','{_CleanupReason}','{nameof(InboxOperationOutcome.Applied)}',@Instant)"
                );
            }

            var sql = $"""
                INSERT INTO {_t.Receipts} ({_t.OperationId},{_t.GenerationIncarnationId},{_t.OperationType},{_t.ExpectedStatus},{_t.Actor},{_t.Reason},{_t.Outcome},{_t.StorageId},{_t.CreatedAt})
                VALUES {string.Join(",", receipts)};
                INSERT INTO {_t.Audit} ({_t.AuditId},{_t.OperationId},{_t.GenerationIncarnationId},{_t.OperationType},{_t.Actor},{_t.Reason},{_t.Outcome},{_t.CreatedAt})
                VALUES {string.Join(",", audits)};
                """;

            await RelationalCommand
                .ExecuteNonQueryAsync(
                    connection,
                    transaction,
                    sql,
                    CommandTimeoutSeconds,
                    command =>
                    {
                        _dialect.AddParameter(command, "Instant", SqlColumnType.Timestamp, now);
                        for (var index = 0; index < chunk.Length; index++)
                        {
                            var i = index.ToString(CultureInfo.InvariantCulture);
                            var row = chunk[index];
                            _dialect.AddParameter(command, "Op" + i, SqlColumnType.Guid, _guidGenerator.Create());
                            _dialect.AddParameter(command, "Audit" + i, SqlColumnType.Guid, _guidGenerator.Create());
                            _dialect.AddParameter(
                                command,
                                "Incarnation" + i,
                                SqlColumnType.Guid,
                                row.GenerationIncarnationId
                            );
                            _dialect.AddParameter(command, "Status" + i, _StatusType, row.StatusName);
                            _dialect.AddParameter(command, "Storage" + i, SqlColumnType.Guid, row.Id);
                        }
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
    }

    private const string _CleanupActor = "headless.messaging.collector";
    private const string _CleanupReason = "retention_expired";

    private sealed record ExpiredReceivedRow(
        Guid Id,
        bool IsInbox,
        Guid? GenerationIncarnationId,
        string StatusName,
        string ConsumerIdentity,
        MessageLane Lane,
        DateTimeOffset? EffectiveExpiresAt
    );

    /// <summary>Reads the database clock once, for a transaction that stamps several rows with one instant.</summary>
    private async Task<DateTimeOffset> _ReadNowAsync(
        DbConnection connection,
        DbTransaction? transaction,
        CancellationToken cancellationToken
    )
    {
        return await RelationalCommand
            .ExecuteReaderAsync(
                connection,
                transaction,
                _dialect.Render(new SqlClockedStatement($"SELECT {SqlDialectTokens.Now};")),
                CommandTimeoutSeconds,
                bind: null,
                static async (reader, ct) =>
                {
                    if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        throw new InvalidOperationException("The database clock read returned no row.");
                    }

                    return await reader.GetFieldValueAsync<DateTimeOffset>(0, ct).ConfigureAwait(false);
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Selects delayed and stale-queued messages inside a transaction and invokes <paramref name="scheduleTask"/> to
    /// re-enqueue them, committing after it completes. The selection locks the rows it returns and skips rows another
    /// replica is scheduling.
    /// </summary>
    public async ValueTask ScheduleMessagesOfDelayedAsync(
        Func<DbTransaction?, IEnumerable<MediumMessage>, ValueTask> scheduleTask,
        CancellationToken cancellationToken = default
    )
    {
        // Due time is the database's: the windows are measured from the database clock, the clock every other due
        // decision reads, so a replica whose clock is skewed neither schedules a message early nor holds it back.
        // Each status is selected by its own bounded statement, so each reads its own index in due order.
        string[] columns = [_t.Id, _t.Content, _t.IntentType, _t.Retries, _t.InlineAttempts, _t.Added, _t.ExpiresAt];
        var delayedSql = _dialect.Render(
            new SqlLockBatch(
                _publishedTable,
                columns,
                $"{_t.Version} = @Version AND {_t.IntentType} IN (0, 1) AND {_t.StatusName} = '{nameof(StatusName.Delayed)}' AND {_t.ExpiresAt} < {_dialect.ShiftByDuration(SqlDialectTokens.Now, "Lookahead")}",
                [_t.ExpiresAt, _t.Id],
                "BatchSize"
            )
        );
        var queuedSql = _dialect.Render(
            new SqlLockBatch(
                _publishedTable,
                columns,
                $"{_t.Version} = @Version AND {_t.IntentType} IN (0, 1) AND {_t.StatusName} = '{nameof(StatusName.Queued)}' AND {_t.ExpiresAt} < {_dialect.ShiftByDuration(SqlDialectTokens.Now, "Lookback", subtract: true)}",
                [_t.ExpiresAt, _t.Id],
                "BatchSize"
            )
        );

        await using var connection = _CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        // Explicit, so the selection runs at READ COMMITTED whatever a pooled session last used: SQL Server refuses to
        // skip locked rows at a stricter level.
        await using var transaction = await connection
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            .ConfigureAwait(false);

        var poisonMessages = new List<PoisonMessage>();
        var batchSize = Options.SchedulerBatchSize;
        var messages = new List<MediumMessage>();
        foreach (var sql in (string[])[delayedSql, queuedSql])
        {
            messages.AddRange(
                await RelationalCommand
                    .ExecuteReaderAsync(
                        connection,
                        transaction,
                        sql,
                        CommandTimeoutSeconds,
                        command =>
                        {
                            _BindVersion(command);
                            _dialect.AddDuration(command, "Lookahead", _DelayedMessageLookahead);
                            _dialect.AddDuration(command, "Lookback", _QueuedMessageLookback);
                            _dialect.AddParameter(command, "BatchSize", SqlColumnType.Int32, batchSize);
                        },
                        (reader, ct) => _ReadScheduledAsync(reader, poisonMessages, ct),
                        cancellationToken
                    )
                    .ConfigureAwait(false)
            );
        }

        var messageList = messages
            .OrderBy(static message => message.ExpiresAt)
            .ThenBy(static message => message.StorageId)
            .Take(batchSize)
            .ToList();

        _logger.LogSchedulerBatchFetched(messageList.Count, _publishedTable);

        await _MarkPoisonMessagesTerminalAsync(
                connection,
                transaction,
                _publishedTable,
                poisonMessages,
                cancellationToken
            )
            .ConfigureAwait(false);

        await scheduleTask(transaction, messageList).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<List<MediumMessage>> _ReadScheduledAsync(
        DbDataReader reader,
        List<PoisonMessage> poisonMessages,
        CancellationToken cancellationToken
    )
    {
        var messages = new List<MediumMessage>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var storageId = reader.GetGuid(0);
            var content = reader.GetString(1);

            try
            {
                messages.Add(
                    new MediumMessage
                    {
                        StorageId = storageId,
                        Origin = _serializer.Deserialize(content)!,
                        Content = content,
                        Lane = MessageLaneCompatibility.FromPersistedValue(reader.GetInt16(2)),
                        Retries = reader.GetInt32(3),
                        InlineAttempts = reader.GetInt32(4),
                        Added = await reader
                            .GetFieldValueAsync<DateTimeOffset>(5, cancellationToken)
                            .ConfigureAwait(false),
                        ExpiresAt = await _ReadInstantAsync(reader, 6, cancellationToken).ConfigureAwait(false),
                    }
                );
            }
#pragma warning disable CA1031 // deliberately broad: one un-deserializable row must not abort the schedule batch
            catch (Exception ex)
#pragma warning restore CA1031
            {
                _logger.LogPoisonMessageSkipped(storageId, _publishedTable, ex);
                poisonMessages.Add(_CreatePoisonMessage(storageId, ex));
            }
        }

        return messages;
    }

    public async ValueTask<IReadOnlyList<MediumMessage>> ClaimDelayedMessagesAsync(
        CancellationToken cancellationToken = default
    )
    {
        // One claim statement moves due delayed (and stale queued) rows to Queued and leases them past their due
        // time, skipping rows another replica holds. Due time and lease are both the database's clock.
        var sql = _dialect.Render(
            new SqlClaimNext(
                _publishedTable,
                [_t.Id],
                $"""
                {_t.Version}=@Version
                AND {_t.IntentType} IN (0, 1)
                AND ({_t.LockedUntil} IS NULL OR {_t.LockedUntil} <= {SqlDialectTokens.Now})
                AND (
                    ({_t.StatusName}=@DelayedStatusName AND {_t.ExpiresAt} < {_dialect.ShiftByDuration(
                    SqlDialectTokens.Now,
                    "Lookahead"
                )})
                    OR ({_t.StatusName}=@QueuedStatusName AND {_t.ExpiresAt} < {_dialect.ShiftByDuration(
                    SqlDialectTokens.Now,
                    "Lookback",
                    subtract: true
                )})
                )
                AND {_terminalGuard}
                """,
                [_t.ExpiresAt, _t.Id],
                $"{_t.StatusName}=@QueuedStatusName, {_t.LockedUntil}={_dialect.ShiftByDuration($"CASE WHEN {_t.ExpiresAt} > {SqlDialectTokens.Now} THEN {_t.ExpiresAt} ELSE {SqlDialectTokens.Now} END", "Lease")}, {_t.Owner}=@Owner",
                [
                    _t.Id,
                    _t.Content,
                    _t.IntentType,
                    _t.Retries,
                    _t.InlineAttempts,
                    _t.Added,
                    _t.ExpiresAt,
                    _t.LockedUntil,
                    _t.Owner,
                ],
                BatchSizeParameter: "BatchSize"
            )
        );

        // The transaction commits without the caller's token: an engine may commit after accepting COMMIT even when
        // the client then observes cancellation, and the claim must not lose winners it already leased.
        var claimed = await SqlAutonomousTransaction
            .RunAsync(
                _CreateConnection,
                async (connection, transaction, ct) =>
                {
                    var poisonMessages = new List<PoisonMessage>();
                    var messages = await RelationalCommand
                        .ExecuteReaderAsync(
                            connection,
                            transaction,
                            sql,
                            CommandTimeoutSeconds,
                            command =>
                            {
                                _BindVersion(command);
                                _dialect.AddParameter(
                                    command,
                                    "DelayedStatusName",
                                    _StatusType,
                                    nameof(StatusName.Delayed)
                                );
                                _dialect.AddParameter(
                                    command,
                                    "QueuedStatusName",
                                    _StatusType,
                                    nameof(StatusName.Queued)
                                );
                                _dialect.AddDuration(command, "Lookahead", _DelayedMessageLookahead);
                                _dialect.AddDuration(command, "Lookback", _QueuedMessageLookback);
                                _dialect.AddParameter(
                                    command,
                                    "BatchSize",
                                    SqlColumnType.Int32,
                                    Options.SchedulerBatchSize
                                );
                                _dialect.AddDuration(command, "Lease", Options.RetryPolicy.DispatchTimeout);
                                _BindOwner(command, "Owner", hasLease: true);
                            },
                            (reader, token) => _ReadDelayedClaimAsync(reader, poisonMessages, token),
                            ct
                        )
                        .ConfigureAwait(false);

                    await _MarkPoisonMessagesTerminalAsync(connection, transaction, _publishedTable, poisonMessages, ct)
                        .ConfigureAwait(false);
                    ct.ThrowIfCancellationRequested();

                    return messages;
                },
                _timeProvider,
                cancellationToken
            )
            .ConfigureAwait(false);

        // The claim reports rows in no particular order; callers dispatch in due order.
        claimed.Sort(
            static (left, right) =>
            {
                var expiresComparison = Nullable.Compare(left.ExpiresAt, right.ExpiresAt);
                return expiresComparison != 0 ? expiresComparison : left.StorageId.CompareTo(right.StorageId);
            }
        );
        return claimed;
    }

    private async Task<List<MediumMessage>> _ReadDelayedClaimAsync(
        DbDataReader reader,
        List<PoisonMessage> poisonMessages,
        CancellationToken cancellationToken
    )
    {
        var messages = new List<MediumMessage>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var storageId = reader.GetGuid(0);
            var content = reader.GetString(1);
            try
            {
                messages.Add(
                    new MediumMessage
                    {
                        StorageId = storageId,
                        Origin = _serializer.Deserialize(content)!,
                        Content = content,
                        Lane = MessageLaneCompatibility.FromPersistedValue(reader.GetInt16(2)),
                        Retries = reader.GetInt32(3),
                        InlineAttempts = reader.GetInt32(4),
                        Added = await reader
                            .GetFieldValueAsync<DateTimeOffset>(5, cancellationToken)
                            .ConfigureAwait(false),
                        ExpiresAt = await reader
                            .GetFieldValueAsync<DateTimeOffset>(6, cancellationToken)
                            .ConfigureAwait(false),
                        LockedUntil = await reader
                            .GetFieldValueAsync<DateTimeOffset>(7, cancellationToken)
                            .ConfigureAwait(false),
                        Owner = await _ReadStringAsync(reader, 8, cancellationToken).ConfigureAwait(false),
                    }
                );
            }
#pragma warning disable CA1031 // one un-deserializable row must not abort or starve the batch
            catch (Exception ex)
#pragma warning restore CA1031
            {
                _logger.LogPoisonMessageSkipped(storageId, _publishedTable, ex);
                poisonMessages.Add(_CreatePoisonMessage(storageId, ex));
            }
        }

        return messages;
    }
}

#pragma warning restore CA2100
