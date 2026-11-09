// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Fencing;
using Headless.Messaging.Internal;
using Headless.Sql;

namespace Headless.Messaging.Persistence;

internal sealed partial class RelationalDataStorage
{
    // The prefix of the lease take's grant parameters: TakeDuration and TakeHolder.
    private const string _TakePrefix = "Take";

    /// <summary>Reads the five lease columns a fenced write returns, the first at ordinal 1.</summary>
    private static readonly Func<DbDataReader, CancellationToken, ValueTask<RowLeaseSnapshot>> _ReadReturnedLease =
        static (reader, cancellationToken) => RowLeaseReader.ReadAsync(reader, 1, cancellationToken);

    /// <summary>Reads nothing back: the caller needs only whether the write applied.</summary>
    private static readonly Func<DbDataReader, CancellationToken, ValueTask<bool>> _ReadNothing = static (_, _) =>
        ValueTask.FromResult(true);

    /// <summary>
    /// The pre-rendered parts of one state change: the assignments and condition a fenced write composes with the lease,
    /// and the whole statement of an unfenced write.
    /// </summary>
    private sealed record ChangeStateSql(string Set, string Condition, string Unfenced);

    /// <summary>
    /// Hands published messages back to <c>Delayed</c> status, each only while its row still carries the lease
    /// generation the message recorded. No-op when <paramref name="messages"/> is empty.
    /// </summary>
    public async ValueTask ChangePublishStateToDelayedAsync(
        IReadOnlyCollection<MediumMessage> messages,
        CancellationToken cancellationToken = default
    )
    {
        if (messages.Count == 0)
        {
            return;
        }

        // A leased copy releases its own generation with the status flip, so the row is re-claimable at once instead of
        // after the lease expires. The shutdown flush also holds unleased hand-off copies, and the row behind one may
        // since have been claimed by another node that is sending it: resetting that row would let a third node send it
        // again. So an unleased copy flips only a row no attempt holds, still at the generation the copy read.
        // Rows are locked in id order so two flushes over the same rows cannot deadlock.
        var leased = messages
            .Where(static message => message.Lease.State == RowLeaseState.Active)
            .OrderBy(static message => message.StorageId)
            .ToArray();
        var unleased = messages
            .Where(static message => message.Lease.State != RowLeaseState.Active)
            .OrderBy(static message => message.StorageId)
            .ToArray();
        var leaseTable = _t.PublishedLease;

        await _RunAsync(
                "messaging.change_publish_state_to_delayed",
                transaction: null,
                async (connection, transaction, ct) =>
                {
                    foreach (var message in leased)
                    {
                        var lane = MessageLaneCompatibility.ToPersistedValue(message.Lane);
                        await FencedRows
                            .ReleaseAsync(
                                leaseTable,
                                connection,
                                transaction,
                                _LeaseRow(leaseTable, message),
                                _Write(
                                    set: $"{_t.StatusName}=@StatusName",
                                    condition: _releaseLeaseCondition[0],
                                    bind: command =>
                                    {
                                        _dialect.AddParameter(
                                            command,
                                            "StatusName",
                                            _StatusType,
                                            nameof(StatusName.Delayed)
                                        );
                                        _dialect.AddParameter(command, "IntentType", SqlColumnType.Int16, lane);
                                    }
                                ),
                                _ReadNothing,
                                ct
                            )
                            .ConfigureAwait(false);
                    }

                    // Each unleased row binds three parameters.
                    var batchSize = Math.Min(_LeaseReleaseBatchSize, (_MaxCommandParameters - 1) / 3);
                    foreach (var batch in unleased.Chunk(batchSize))
                    {
                        await _DelayUnleasedAsync(connection, transaction, batch, ct).ConfigureAwait(false);
                    }

                    return true;
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private Task<int> _DelayUnleasedAsync(
        DbConnection connection,
        DbTransaction transaction,
        MediumMessage[] batch,
        CancellationToken cancellationToken
    )
    {
        var predicates = new string[batch.Length];
        for (var index = 0; index < batch.Length; index++)
        {
            var suffix = index.ToString(CultureInfo.InvariantCulture);
            predicates[index] =
                $"({_t.Id}=@Id{suffix} AND {_t.IntentType}=@IntentType{suffix} AND {_t.LeaseGeneration}=@Generation{suffix})";
        }

        var sql =
            $"UPDATE {_publishedTable} SET {_t.StatusName}=@StatusName WHERE {_t.LeaseState} <> {_t.ActiveLease} AND {_terminalGuard} AND ({string.Join(" OR ", predicates)});";

        return RelationalCommand.ExecuteNonQueryAsync(
            connection,
            transaction,
            sql,
            CommandTimeoutSeconds,
            command =>
            {
                _dialect.AddParameter(command, "StatusName", _StatusType, nameof(StatusName.Delayed));
                for (var index = 0; index < batch.Length; index++)
                {
                    var message = batch[index];
                    var suffix = index.ToString(CultureInfo.InvariantCulture);
                    _dialect.AddParameter(command, "Id" + suffix, SqlColumnType.Guid, message.StorageId);
                    _dialect.AddParameter(
                        command,
                        "IntentType" + suffix,
                        SqlColumnType.Int16,
                        MessageLaneCompatibility.ToPersistedValue(message.Lane)
                    );
                    _dialect.AddParameter(
                        command,
                        "Generation" + suffix,
                        SqlColumnType.Int64,
                        message.Lease.Generation
                    );
                }
            },
            cancellationToken
        );
    }

    /// <summary>
    /// Updates the status of a published message. Respects the terminal-row guard — rows already
    /// in a permanent <c>Succeeded</c> or <c>Failed</c> state with no pending retry are not mutated.
    /// </summary>
    /// <returns><see langword="true"/> if a row was updated; <see langword="false"/> if the guard blocked it.</returns>
    public ValueTask<bool> ChangePublishStateAsync(
        MediumMessage message,
        StatusName state,
        MessageContentWrite contentWrite = MessageContentWrite.Preserve,
        DbTransaction? transaction = null,
        RetryDelay? retryDelay = null,
        bool keepLease = false,
        int? originalRetries = null,
        CancellationToken cancellationToken = default
    )
    {
        return _ChangeStateAsync(
            "messaging.change_publish_state",
            _publishedTable,
            message,
            state,
            contentWrite,
            transaction,
            retryDelay,
            keepLease,
            originalRetries,
            originalInlineAttempts: null,
            cancellationToken
        );
    }

    public ValueTask<bool> ChangePublishRetryStateAsync(
        MediumMessage message,
        StatusName state,
        MessageContentWrite contentWrite,
        RetryDelay? retryDelay,
        bool keepLease,
        int originalRetries,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
    )
    {
        return _ChangeStateAsync(
            "messaging.change_publish_retry_state",
            _publishedTable,
            message,
            state,
            contentWrite,
            transaction: null,
            retryDelay,
            keepLease,
            originalRetries,
            originalInlineAttempts,
            cancellationToken
        );
    }

    /// <summary>
    /// Updates the status of a received message, including writing <c>ExceptionInfo</c> when the
    /// message faulted. Respects the terminal-row guard — permanently completed rows are not mutated.
    /// </summary>
    /// <returns><see langword="true"/> if a row was updated; <see langword="false"/> if the guard blocked it.</returns>
    public ValueTask<bool> ChangeReceiveStateAsync(
        MediumMessage message,
        StatusName state,
        MessageContentWrite contentWrite = MessageContentWrite.Preserve,
        RetryDelay? retryDelay = null,
        bool keepLease = false,
        int? originalRetries = null,
        CancellationToken cancellationToken = default
    )
    {
        return _ChangeStateAsync(
            "messaging.change_receive_state",
            _receivedTable,
            message,
            state,
            contentWrite,
            transaction: null,
            retryDelay,
            keepLease,
            originalRetries,
            originalInlineAttempts: null,
            cancellationToken
        );
    }

    public ValueTask<bool> ChangeReceiveRetryStateAsync(
        MediumMessage message,
        StatusName state,
        MessageContentWrite contentWrite,
        RetryDelay? retryDelay,
        bool keepLease,
        int originalRetries,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
    )
    {
        return _ChangeStateAsync(
            "messaging.change_receive_retry_state",
            _receivedTable,
            message,
            state,
            contentWrite,
            transaction: null,
            retryDelay,
            keepLease,
            originalRetries,
            originalInlineAttempts,
            cancellationToken
        );
    }

    ValueTask<bool> ITransactionalInboxStorage.CompleteReceivedInboxAsync(
        MediumMessage message,
        DbTransaction transaction,
        CancellationToken cancellationToken
    )
    {
        return _ChangeStateAsync(
            "messaging.complete_received_inbox",
            _receivedTable,
            message,
            StatusName.Succeeded,
            message.HasUnpersistedOriginChanges ? MessageContentWrite.Refresh : MessageContentWrite.Preserve,
            transaction,
            retryDelay: null,
            keepLease: false,
            originalRetries: message.Retries,
            originalInlineAttempts: message.InlineAttempts,
            cancellationToken
        );
    }

    async ValueTask<InboxCommitProbe> ITransactionalInboxStorage.ProbeReceivedInboxCommitAsync(
        MediumMessage message,
        CancellationToken cancellationToken
    )
    {
        if (message.InboxAttemptFence is not { } fence)
        {
            return InboxCommitProbe.Indeterminate;
        }

        var sql = $"""
            SELECT CASE WHEN {_t.StatusName}='{nameof(
                StatusName.Succeeded
            )}' AND {_t.NextRetryAt} IS NULL AND {_t.AttemptId} IS NULL THEN 1 ELSE 0 END
            FROM {_receivedTable}
            WHERE {_t.Id}=@Id
              AND {_t.IsInboxRecord} = {_t.True}
              AND {_t.IntentType}=@IntentType
              AND {_t.Generation}=@Generation
              AND {_t.GenerationIncarnationId}=@GenerationIncarnationId;
            """;

        try
        {
            await using var connection = _CreateConnection();
            return await RelationalCommand
                .ExecuteReaderAsync(
                    connection,
                    transaction: null,
                    sql,
                    CommandTimeoutSeconds,
                    command =>
                    {
                        _dialect.AddParameter(command, "Id", SqlColumnType.Guid, fence.StorageId);
                        _dialect.AddParameter(
                            command,
                            "IntentType",
                            SqlColumnType.Int16,
                            MessageLaneCompatibility.ToPersistedValue(fence.Lane)
                        );
                        _dialect.AddParameter(command, "Generation", SqlColumnType.Int64, fence.Generation);
                        _dialect.AddParameter(
                            command,
                            "GenerationIncarnationId",
                            SqlColumnType.Guid,
                            fence.GenerationIncarnationId
                        );
                    },
                    static async (reader, ct) =>
                        await reader.ReadAsync(ct).ConfigureAwait(false)
                        && Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture) == 1
                            ? InboxCommitProbe.Committed
                            : InboxCommitProbe.Indeterminate,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogInboxCommitProbeFailed(ex);
            return InboxCommitProbe.Indeterminate;
        }
    }

    /// <summary>
    /// Renders the state change of a row in the received or published table, with or without rewriting its content.
    /// </summary>
    private ChangeStateSql _RenderChangeState(bool received, bool refreshContent)
    {
        var table = received ? _receivedTable : _publishedTable;
        var set = new List<string>(capacity: 10);
        if (refreshContent)
        {
            set.Add($"{_t.Content}=@Content");
        }

        set.Add($"{_t.Retries}=@Retries");
        set.Add($"{_t.InlineAttempts}=@InlineAttempts");
        set.Add($"{_t.ExpiresAt}=@ExpiresAt");
        set.Add($"{_t.NextRetryAt}={_nextRetryAtAssignment}");
        set.Add($"{_t.StatusName}=@StatusName");

        var condition = _terminalGuardWithRetries;

        if (received)
        {
            // The received table also records the failure, ends the inbox attempt with the lease, and stamps an inbox
            // generation's terminal time and the retention it starts.
            set.Add($"{_t.ExceptionInfo}=@ExceptionInfo");
            set.Add($"{_t.AttemptId}=CASE WHEN @KeepLease = {_t.True} THEN {_t.AttemptId} ELSE NULL END");
            set.Add(
                $"{_t.TerminalAt}=CASE WHEN {_t.IsInboxRecord} = {_t.True} AND @IsTerminal = {_t.True} THEN {SqlDialectTokens.Now} ELSE {_t.TerminalAt} END"
            );
            set.Add(
                $"{_t.EffectiveExpiresAt}=CASE WHEN {_t.IsInboxRecord} = {_t.True} AND @IsTerminal = {_t.True} THEN {_dialect.ShiftBySeconds(SqlDialectTokens.Now, _t.InboxRetentionSeconds)} ELSE {_t.EffectiveExpiresAt} END"
            );
            condition += $" AND {_InboxAttemptGuard(matchStorageId: false)}";
        }

        var assignments = string.Join(", ", set);

        // A write that holds no lease generation ends whatever attempt holds the row unless it keeps the lease: it is
        // rewriting the row's outcome, so the attempt is over.
        var unfenced = _RenderTransition(
            table,
            condition,
            assignments + ", " + _RevokeAnyLeaseAssignments(unless: $"@KeepLease = {_t.True}"),
            [_t.Id]
        );

        return new ChangeStateSql(assignments, condition, unfenced);
    }

    private async ValueTask<bool> _ChangeStateAsync(
        string operation,
        string table,
        MediumMessage message,
        StatusName state,
        MessageContentWrite contentWrite,
        DbTransaction? transaction,
        RetryDelay? retryDelay,
        bool keepLease,
        int? originalRetries,
        int? originalInlineAttempts,
        CancellationToken cancellationToken
    )
    {
        var received = _IsReceived(table);
        var refreshContent = contentWrite is MessageContentWrite.Refresh;
        var content = refreshContent ? _RefreshContent(message) : null;
        var isTerminal = state is StatusName.Succeeded or StatusName.Failed && retryDelay is null;
        var sql = _changeStateSql[(received ? 2 : 0) | (refreshContent ? 1 : 0)];

        void bind(DbCommand command)
        {
            if (refreshContent)
            {
                _dialect.AddParameter(command, "Content", _ContentType, content);
            }

            _dialect.AddParameter(command, "Retries", SqlColumnType.Int32, message.Retries);
            _dialect.AddParameter(command, "InlineAttempts", SqlColumnType.Int32, message.InlineAttempts);
            _dialect.AddParameter(command, "ExpiresAt", SqlColumnType.Timestamp, message.ExpiresAt);
            _BindRetryDelay(command, retryDelay);
            _dialect.AddParameter(command, "KeepLease", SqlColumnType.Boolean, keepLease);
            _dialect.AddParameter(command, "OriginalRetries", SqlColumnType.Int32, originalRetries);
            _dialect.AddParameter(command, "OriginalInlineAttempts", SqlColumnType.Int32, originalInlineAttempts);
            _dialect.AddParameter(command, "StatusName", _StatusType, state.ToString("G"));

            if (received)
            {
                _dialect.AddParameter(command, "ExceptionInfo", _ContentType, message.ExceptionInfo);
                _dialect.AddParameter(command, "IsTerminal", SqlColumnType.Boolean, isTerminal);
                _BindInboxAttempt(command, message.InboxAttemptFence, matchStorageId: false);
            }
        }

        if (originalInlineAttempts is null)
        {
            return await _RunTransitionAsync(
                    operation,
                    transaction,
                    sql.Unfenced,
                    command =>
                    {
                        _dialect.AddParameter(command, "Id", SqlColumnType.Guid, message.StorageId);
                        bind(command);
                    },
                    _ReadAppliedAsync,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        // An inline retry passes the attempt counter it read, and with it the lease generation it holds: its transition
        // applies only while that generation is still current by the database clock, so an attempt whose lease another
        // node took over writes nothing. A terminal outcome settles the lease, a retry schedule releases it so pickup can
        // take the row again, and an in-flight inline retry keeps it.
        var leaseTable = _LeaseTable(table);
        var row = _LeaseRow(leaseTable, message);
        var write = _Write(sql.Set, sql.Condition, _t.LeaseColumnNames, bind);

        var written = await _RunAsync(
                operation,
                transaction,
                async (connection, tx, ct) =>
                {
                    if (keepLease)
                    {
                        return await FencedRows
                            .WriteAsync(leaseTable, connection, tx, row, write, _ReadReturnedLease, ct)
                            .ConfigureAwait(false);
                    }

                    return isTerminal
                        ? await FencedRows
                            .SettleAsync(leaseTable, connection, tx, row, write, _ReadReturnedLease, ct)
                            .ConfigureAwait(false)
                        : await FencedRows
                            .ReleaseAsync(leaseTable, connection, tx, row, write, _ReadReturnedLease, ct)
                            .ConfigureAwait(false);
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        if (!written.IsAccepted)
        {
            return false;
        }

        message.Lease = written.Match(static (lease, _) => lease, static _ => default);
        return true;
    }

    public ValueTask<bool> ReservePublishAttemptAsync(
        MediumMessage message,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
    )
    {
        return _ReserveAttemptAsync(
            "messaging.reserve_publish_attempt",
            _publishedTable,
            message,
            originalInlineAttempts,
            cancellationToken
        );
    }

    public ValueTask<bool> ReserveReceiveAttemptAsync(
        MediumMessage message,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
    )
    {
        return _ReserveAttemptAsync(
            "messaging.reserve_receive_attempt",
            _receivedTable,
            message,
            originalInlineAttempts,
            cancellationToken
        );
    }

    /// <summary>Renders the condition an attempt reservation adds to the lease fence in the received or published table.</summary>
    private string _RenderReserveAttemptCondition(bool received)
    {
        return received
            ? $"{_terminalGuardWithRetries} AND {_InboxAttemptGuard(matchStorageId: true)}"
            : _terminalGuardWithRetries;
    }

    private async ValueTask<bool> _ReserveAttemptAsync(
        string operation,
        string table,
        MediumMessage message,
        int originalInlineAttempts,
        CancellationToken cancellationToken
    )
    {
        // Durable attempt reservation: the counter is a compare-and-set on the attempt the caller read, and only the
        // current lease generation may move it. The claim leased a whole batch at once and its rows run one after
        // another, so the reservation also renews the lease from the database clock: a row reserved late in the batch
        // starts its attempt with a full lease instead of whatever the batch lease has left. The counter write and the
        // renewal are one fenced statement, so a refused reservation leaves both the counter and the lease untouched.
        if (message.Lease.State != RowLeaseState.Active)
        {
            return false;
        }

        var received = _IsReceived(table);
        var leaseTable = _LeaseTable(table);
        var row = _LeaseRow(leaseTable, message);
        var write = _Write(
            set: $"{_t.InlineAttempts}=@InlineAttempts",
            condition: _reserveAttemptCondition[received ? 1 : 0],
            returning: _t.LeaseColumnNames,
            bind: command =>
            {
                _dialect.AddParameter(command, "InlineAttempts", SqlColumnType.Int32, message.InlineAttempts);
                _dialect.AddParameter(command, "OriginalRetries", SqlColumnType.Int32, message.Retries);
                _dialect.AddParameter(command, "OriginalInlineAttempts", SqlColumnType.Int32, originalInlineAttempts);

                if (received)
                {
                    _BindInboxAttempt(command, message.InboxAttemptFence, matchStorageId: true);
                }
            }
        );
        var dispatchTimeout = Options.RetryPolicy.DispatchTimeout;

        var written = await _RunAsync(
                operation,
                transaction: null,
                async (connection, tx, ct) =>
                    await FencedRows
                        .WriteAndRenewAsync(
                            leaseTable,
                            connection,
                            tx,
                            row,
                            write,
                            dispatchTimeout,
                            _ReadReturnedLease,
                            ct
                        )
                        .ConfigureAwait(false),
                cancellationToken
            )
            .ConfigureAwait(false);

        if (written.Match(static (stored, _) => (RowLeaseSnapshot?)stored, static _ => null) is not { } lease)
        {
            return false;
        }

        message.Lease = lease;
        return true;
    }

    public ValueTask<bool> RenewPublishLeaseAsync(
        MediumMessage message,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken
    )
    {
        return _RenewLeaseAsync(
            "messaging.renew_publish_lease",
            _publishedTable,
            message,
            leaseDuration,
            cancellationToken
        );
    }

    public ValueTask<bool> RenewReceiveLeaseAsync(
        MediumMessage message,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken
    )
    {
        return _RenewLeaseAsync(
            "messaging.renew_receive_lease",
            _receivedTable,
            message,
            leaseDuration,
            cancellationToken
        );
    }

    /// <summary>
    /// Slides a held lease forward while its attempt runs. Fenced on the exact lease generation the caller holds and on
    /// that generation still being live, and stamped from the same database-clock reading, so a renewal can never revive
    /// a lease that lapsed or take one another node now holds.
    /// </summary>
    private async ValueTask<bool> _RenewLeaseAsync(
        string operation,
        string table,
        MediumMessage message,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken
    )
    {
        if (message.Lease.State != RowLeaseState.Active)
        {
            return false;
        }

        var leaseTable = _LeaseTable(table);
        var row = _LeaseRow(leaseTable, message);

        var renewed = await _RunAsync(
                operation,
                transaction: null,
                (connection, tx, ct) =>
                    FencedRows.RenewAsync(leaseTable, connection, tx, row, leaseDuration, ct).AsTask(),
                cancellationToken
            )
            .ConfigureAwait(false);

        if (renewed.Status != LeaseRenewalStatus.Renewed)
        {
            return false;
        }

        message.Lease = message.Lease with { ExpiresAt = renewed.ExpiresAt };
        return true;
    }

    /// <summary>
    /// Takes a dispatch lease on a published message: a new lease generation, held by this node.
    /// Only succeeds if no live attempt holds the row.
    /// </summary>
    /// <returns><see langword="true"/> if the lease was acquired; <see langword="false"/> if another node already holds it.</returns>
    public ValueTask<bool> LeasePublishAsync(
        MediumMessage message,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default
    )
    {
        return _LeaseAsync(
            "messaging.lease_publish",
            _publishedTable,
            message,
            leaseDuration,
            reserve: null,
            cancellationToken
        );
    }

    public ValueTask<bool> LeasePublishAndReserveAttemptAsync(
        MediumMessage message,
        TimeSpan leaseDuration,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
    )
    {
        return _LeaseAsync(
            "messaging.lease_publish_and_reserve_attempt",
            _publishedTable,
            message,
            leaseDuration,
            originalInlineAttempts,
            cancellationToken
        );
    }

    /// <summary>
    /// Takes a dispatch lease on a received message: a new lease generation, held by this node.
    /// Only succeeds if no live attempt holds the row.
    /// </summary>
    /// <returns><see langword="true"/> if the lease was acquired; <see langword="false"/> if another node already holds it.</returns>
    public ValueTask<bool> LeaseReceiveAsync(
        MediumMessage message,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default
    )
    {
        return _LeaseAsync(
            "messaging.lease_receive",
            _receivedTable,
            message,
            leaseDuration,
            reserve: null,
            cancellationToken
        );
    }

    public ValueTask<bool> LeaseReceiveAndReserveAttemptAsync(
        MediumMessage message,
        TimeSpan leaseDuration,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
    )
    {
        return _LeaseAsync(
            "messaging.lease_receive_and_reserve_attempt",
            _receivedTable,
            message,
            leaseDuration,
            originalInlineAttempts,
            cancellationToken
        );
    }

    /// <summary>
    /// Grants a new lease generation when no live attempt holds the row and, given <paramref name="reserve"/>, reserves
    /// the next inline attempt in the same statement (the fresh-dispatch fast path). No holder match is required: this
    /// path is taking the lease, not holding it.
    /// </summary>
    /// <remarks>
    /// Ownership time is the database's: one clock reading decides whether the old lease expired and sets the new
    /// expiry, and the granted lease is read back so the caller's fence matches durable state exactly. The take waits
    /// out another writer of the row and reads the clock after that wait, like every other one-row transition.
    /// </remarks>
    private async ValueTask<bool> _LeaseAsync(
        string operation,
        string table,
        MediumMessage message,
        TimeSpan leaseDuration,
        int? reserve,
        CancellationToken cancellationToken
    )
    {
        var received = _IsReceived(table);
        // A received row starts a new inbox attempt with each lease it takes, so a stale attempt is fenced out.
        var newAttempt = reserve is not null && received;
        var scope = FencedRows.GrantScope(_LeaseTable(table), _TakePrefix);
        var holder = LeaseHolder;
        var sql = _leaseSql[(received ? 2 : 0) | (reserve is null ? 0 : 1)];

        var taken = await _RunAsync(
                operation,
                transaction: null,
                (connection, tx, ct) =>
                    RelationalCommand.ExecuteReaderAsync(
                        connection,
                        tx,
                        sql,
                        CommandTimeoutSeconds,
                        command =>
                        {
                            _dialect.AddParameter(command, "Id", SqlColumnType.Guid, message.StorageId);
                            scope.Bind(command, leaseDuration, holder);

                            if (reserve is not null)
                            {
                                _dialect.AddParameter(
                                    command,
                                    "InlineAttempts",
                                    SqlColumnType.Int32,
                                    message.InlineAttempts
                                );
                                _dialect.AddParameter(command, "OriginalRetries", SqlColumnType.Int32, message.Retries);
                                _dialect.AddParameter(command, "OriginalInlineAttempts", SqlColumnType.Int32, reserve);
                            }
                        },
                        async (reader, token) =>
                        {
                            // The take renders through _RenderTransition, whose locking read returns its own result first.
                            await RelationalCommand.SkipResultsAsync(reader, 1, token).ConfigureAwait(false);

                            return await _ReadTakenLeaseAsync(reader, newAttempt, holder, token).ConfigureAwait(false);
                        },
                        ct
                    ),
                cancellationToken
            )
            .ConfigureAwait(false);

        if (taken is not { } stored)
        {
            return false;
        }

        // Mirror the lease the server granted, not a locally recomputed one.
        message.Lease = stored.Lease;
        if (stored.AttemptId is { } attemptId && message.InboxGeneration is { } generation)
        {
            message.InboxAttemptFence = new InboxAttemptFence(
                message.StorageId,
                message.Lane,
                generation.Number,
                generation.IncarnationId,
                attemptId,
                stored.Lease.Generation
            );
        }

        return true;
    }

    /// <summary>
    /// Renders the lease take of a row in the received or published table, with or without reserving the next inline
    /// attempt; a received reservation also starts a new inbox attempt.
    /// </summary>
    private string _RenderLease(bool received, bool reserve)
    {
        var newAttempt = reserve && received;
        var scope = FencedRows.GrantScope(received ? _t.ReceivedLease : _t.PublishedLease, _TakePrefix);
        var set = new List<string>(capacity: 3) { scope.Assignments() };
        if (reserve)
        {
            set.Add($"{_t.InlineAttempts}=@InlineAttempts");
        }

        if (newAttempt)
        {
            set.Add($"{_t.AttemptId}=CASE WHEN {_t.IsInboxRecord} = {_t.True} THEN {_dialect.NewGuid()} ELSE NULL END");
        }

        string[] returning = newAttempt
            ? [.. scope.Returning, _t.LeaseTakeoverCount, _t.AttemptId]
            : [.. scope.Returning, _t.LeaseTakeoverCount];

        // A one-row take, unlike the batch claim, waits on the row's lock instead of skipping the row, so a take racing
        // another writer decides on the row that writer left. The grant is allowed only when no live attempt holds it.
        var fence =
            $"NOT ({_t.LeaseState} = {_t.ActiveLease} AND {_t.LeaseExpiresAt} > {SqlDialectTokens.Now}) AND {(reserve ? _terminalGuardWithRetries : _terminalGuard)}";

        return _RenderTransition(received ? _receivedTable : _publishedTable, fence, string.Join(", ", set), returning);
    }

    /// <summary>
    /// Reads a lease take's one row: whether the fence applied, then the key, the granted generation and expiry, the
    /// takeover count, and the new inbox attempt when the take started one.
    /// </summary>
    private static async Task<(RowLeaseSnapshot Lease, Guid? AttemptId)?> _ReadTakenLeaseAsync(
        DbDataReader reader,
        bool readsAttempt,
        string? holder,
        CancellationToken cancellationToken
    )
    {
        // A fenced transition always returns one row; a refused take returns it with the applied flag false.
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false) || !reader.GetBoolean(0))
        {
            return null;
        }

        var generation = Convert.ToInt64(reader.GetValue(2), CultureInfo.InvariantCulture);
        var expiresAt = await reader.GetFieldValueAsync<DateTimeOffset>(3, cancellationToken).ConfigureAwait(false);
        var takeovers = Convert.ToInt32(reader.GetValue(4), CultureInfo.InvariantCulture);
        Guid? attemptId =
            readsAttempt && !await reader.IsDBNullAsync(5, cancellationToken).ConfigureAwait(false)
                ? reader.GetGuid(5)
                : null;

        return (new RowLeaseSnapshot(generation, RowLeaseState.Active, expiresAt, holder, takeovers), attemptId);
    }

    public ValueTask<bool> ReleasePublishedLeaseAsync(
        MessageLeaseIdentity identity,
        CancellationToken cancellationToken = default
    )
    {
        return _ReleaseLeaseAsync(_publishedTable, identity, cancellationToken);
    }

    public ValueTask<bool> ReleaseReceivedLeaseAsync(
        MessageLeaseIdentity identity,
        CancellationToken cancellationToken = default
    )
    {
        return _ReleaseLeaseAsync(_receivedTable, identity, cancellationToken);
    }

    public ValueTask<int> ReleasePublishedLeasesAsync(
        IReadOnlyCollection<MessageLeaseIdentity> identities,
        CancellationToken cancellationToken = default
    )
    {
        return _ReleaseLeasesAsync(_publishedTable, identities, cancellationToken);
    }

    public ValueTask<int> ReleaseReceivedLeasesAsync(
        IReadOnlyCollection<MessageLeaseIdentity> identities,
        CancellationToken cancellationToken = default
    )
    {
        return _ReleaseLeasesAsync(_receivedTable, identities, cancellationToken);
    }

    /// <summary>
    /// Renders the condition a graceful release adds to the lease fence: the lane the identity names and, on the
    /// received table, the inbox attempt it carries.
    /// </summary>
    private string _RenderReleaseLeaseCondition(bool received)
    {
        var condition = $"{_t.IntentType}=@IntentType AND {_terminalGuard}";

        return received ? $"{condition} AND {_InboxAttemptGuard(matchStorageId: true)}" : condition;
    }

    private async ValueTask<bool> _ReleaseLeaseAsync(
        string table,
        MessageLeaseIdentity identity,
        CancellationToken cancellationToken
    )
    {
        return await _ReleaseLeasesAsync(table, [identity], cancellationToken).ConfigureAwait(false) == 1;
    }

    /// <summary>
    /// Releases each identity's lease generation, so a release never ends a lease another node has taken since. Each
    /// row is its own fenced transition in one transaction, locked in id order so two releases cannot deadlock.
    /// </summary>
    private async ValueTask<int> _ReleaseLeasesAsync(
        string table,
        IReadOnlyCollection<MessageLeaseIdentity> identities,
        CancellationToken cancellationToken
    )
    {
        if (identities.Count == 0)
        {
            return 0;
        }

        var received = _IsReceived(table);
        var leaseTable = _LeaseTable(table);
        var condition = _releaseLeaseCondition[received ? 1 : 0];
        var ordered = identities.OrderBy(static identity => identity.StorageId).ToArray();

        return await _RunAsync(
                "messaging.release_leases",
                transaction: null,
                async (connection, tx, ct) =>
                {
                    var released = 0;
                    foreach (var identity in ordered)
                    {
                        var write = _Write(
                            condition: condition,
                            bind: command =>
                            {
                                _BindLane(command, identity.Lane);
                                if (received)
                                {
                                    _BindInboxAttempt(command, identity.InboxAttemptFence, matchStorageId: true);
                                }
                            }
                        );
                        var result = await FencedRows
                            .ReleaseAsync(
                                leaseTable,
                                connection,
                                tx,
                                new FencedRow<Guid>(leaseTable.Name, identity.StorageId, identity.Generation),
                                write,
                                _ReadNothing,
                                ct
                            )
                            .ConfigureAwait(false);

                        if (result.IsAccepted)
                        {
                            released++;
                        }
                    }

                    return released;
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private void _BindLane(DbCommand command, MessageLane lane)
    {
        _dialect.AddParameter(
            command,
            "IntentType",
            SqlColumnType.Int16,
            MessageLaneCompatibility.ToPersistedValue(lane)
        );
    }

    public async ValueTask<bool> DeferReceivedRetryAsync(
        CircuitRetryDeferral deferral,
        CancellationToken cancellationToken = default
    )
    {
        var identity = deferral.Identity;
        var leaseTable = _t.ReceivedLease;
        var write = _Write(
            set: _deferReceivedRetrySet,
            condition: _releaseLeaseCondition[1],
            bind: command =>
            {
                _BindLane(command, identity.Lane);
                _dialect.AddDuration(command, "RetryDelay", deferral.Delay);
                _BindInboxAttempt(command, identity.InboxAttemptFence, matchStorageId: true);
            }
        );

        // The deferral releases the held generation with the new due time, so the row waits out the circuit instead of
        // its lease.
        var deferred = await _RunAsync(
                "messaging.defer_received_retry",
                transaction: null,
                (connection, tx, ct) =>
                    FencedRows
                        .ReleaseAsync(
                            leaseTable,
                            connection,
                            tx,
                            new FencedRow<Guid>(leaseTable.Name, identity.StorageId, identity.Generation),
                            write,
                            _ReadNothing,
                            ct
                        )
                        .AsTask(),
                cancellationToken
            )
            .ConfigureAwait(false);

        return deferred.IsAccepted;
    }

    public ValueTask<bool> DeferReceivedInboxOrphanAsync(
        MediumMessage message,
        CancellationToken cancellationToken = default
    ) => _SetInboxRoutabilityAsync("messaging.defer_received_inbox_orphan", message, orphaned: true, cancellationToken);

    public ValueTask<bool> ConfirmReceivedInboxRoutableAsync(
        MediumMessage message,
        CancellationToken cancellationToken = default
    ) =>
        _SetInboxRoutabilityAsync(
            "messaging.confirm_received_inbox_routable",
            message,
            orphaned: false,
            cancellationToken
        );

    // Orphaning defers the probe and releases the lease; confirming keeps the live lease so the caller can dispatch.
    // Both accept an already-matching orphan flag because the fence, not a state change, authorizes the caller.
    private async ValueTask<bool> _SetInboxRoutabilityAsync(
        string operation,
        MediumMessage message,
        bool orphaned,
        CancellationToken cancellationToken = default
    )
    {
        if (message.InboxAttemptFence is not { } fence || message.InboxGeneration is null)
        {
            return false;
        }

        var leaseTable = _t.ReceivedLease;
        var row = new FencedRow<Guid>(leaseTable.Name, fence.StorageId, fence.LeaseGeneration);
        var write = _Write(
            set: _inboxRoutabilitySet[orphaned ? 1 : 0],
            condition: _inboxRoutabilityCondition,
            returning: _inboxRoutabilityReturning,
            bind: command =>
            {
                _dialect.AddDuration(command, "ProbeInterval", Options.OrphanProbeInterval);
                _BindLane(command, fence.Lane);
                _dialect.AddParameter(command, "Generation", SqlColumnType.Int64, fence.Generation);
                _dialect.AddParameter(
                    command,
                    "GenerationIncarnationId",
                    SqlColumnType.Guid,
                    fence.GenerationIncarnationId
                );
                _dialect.AddParameter(command, "AttemptId", SqlColumnType.Guid, fence.AttemptId);
            }
        );

        var written = await _RunAsync(
                operation,
                transaction: null,
                async (connection, tx, ct) =>
                    orphaned
                        ? await FencedRows
                            .ReleaseAsync(leaseTable, connection, tx, row, write, _ReadRoutabilityAsync, ct)
                            .ConfigureAwait(false)
                        : await FencedRows
                            .WriteAsync(leaseTable, connection, tx, row, write, _ReadRoutabilityAsync, ct)
                            .ConfigureAwait(false),
                cancellationToken
            )
            .ConfigureAwait(false);

        if (!written.IsAccepted)
        {
            return false;
        }

        var (lease, nextRetryAt) = written.Match(static (stored, _) => stored, static _ => default);
        message.IsInboxOrphaned = orphaned;
        message.Lease = lease;
        if (orphaned)
        {
            message.NextRetryAt = nextRetryAt;
        }

        return true;
    }

    /// <summary>Reads the lease and the next retry time a routability write returns.</summary>
    private static async ValueTask<(RowLeaseSnapshot Lease, DateTimeOffset? NextRetryAt)> _ReadRoutabilityAsync(
        DbDataReader reader,
        CancellationToken cancellationToken
    )
    {
        var lease = await RowLeaseReader.ReadAsync(reader, 1, cancellationToken).ConfigureAwait(false);
        var nextRetryAt = await _ReadInstantAsync(reader, 1 + RowLeaseReader.ColumnCount, cancellationToken)
            .ConfigureAwait(false);

        return (lease, nextRetryAt);
    }

    /// <summary>Renders the assignments that mark a held inbox row orphaned (deferring its probe) or routable.</summary>
    private string _RenderInboxRoutabilitySet(bool orphaned)
    {
        // The next orphan probe falls due on the database clock, the clock the retry pickup compares it against.
        return orphaned
            ? $"{_t.IsInboxOrphaned}={_t.True}, {_t.NextRetryAt}={_dialect.ShiftByDuration(SqlDialectTokens.Now, "ProbeInterval")}"
            : $"{_t.IsInboxOrphaned}={_t.False}";
    }
}
