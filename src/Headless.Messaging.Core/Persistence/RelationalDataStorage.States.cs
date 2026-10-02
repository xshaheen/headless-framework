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
    /// Bulk-transitions the specified published messages to <c>Delayed</c> status.
    /// No-op when <paramref name="storageIds"/> is empty.
    /// </summary>
    public async ValueTask ChangePublishStateToDelayedAsync(
        Guid[] storageIds,
        CancellationToken cancellationToken = default
    )
    {
        if (storageIds.Length == 0)
        {
            return;
        }

        // Clear the ownership lease alongside the status flip: the only caller is the graceful-shutdown flush,
        // which owns these rows via its own claim and is releasing them for immediate re-scheduling. Leaving a
        // stale LockedUntil/Owner would fence the row from re-claim until the lease expires (delayed message
        // delivered up to DispatchTimeout late after restart).
        var sql =
            $"UPDATE {_publishedTable} SET {_t.StatusName}=@StatusName, {_t.LockedUntil}=NULL, {_t.Owner}=NULL WHERE {_dialect.InList(_t.Id, "Ids", SqlColumnType.Guid)} AND {_terminalGuard};";

        await using var connection = _CreateConnection();
        await RelationalCommand
            .ExecuteNonQueryAsync(
                connection,
                transaction: null,
                sql,
                CommandTimeoutSeconds,
                command =>
                {
                    _dialect.AddListParameter(command, "Ids", SqlColumnType.Guid, storageIds);
                    _dialect.AddParameter(command, "StatusName", _StatusType, nameof(StatusName.Delayed));
                },
                cancellationToken
            )
            .ConfigureAwait(false);
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
        DateTimeOffset? lockedUntil = null,
        int? originalRetries = null,
        CancellationToken cancellationToken = default
    )
    {
        return _ChangeStateAsync(
            _publishedTable,
            message,
            state,
            contentWrite,
            transaction,
            retryDelay,
            lockedUntil,
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
        DateTimeOffset? lockedUntil,
        int originalRetries,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
    )
    {
        return _ChangeStateAsync(
            _publishedTable,
            message,
            state,
            contentWrite,
            transaction: null,
            retryDelay,
            lockedUntil,
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
        DateTimeOffset? lockedUntil = null,
        int? originalRetries = null,
        CancellationToken cancellationToken = default
    )
    {
        return _ChangeStateAsync(
            _receivedTable,
            message,
            state,
            contentWrite,
            transaction: null,
            retryDelay,
            lockedUntil,
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
        DateTimeOffset? lockedUntil,
        int originalRetries,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
    )
    {
        return _ChangeStateAsync(
            _receivedTable,
            message,
            state,
            contentWrite,
            transaction: null,
            retryDelay,
            lockedUntil,
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
            _receivedTable,
            message,
            StatusName.Succeeded,
            MessageContentWrite.Refresh,
            transaction,
            retryDelay: null,
            lockedUntil: null,
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

    private async ValueTask<bool> _ChangeStateAsync(
        string table,
        MediumMessage message,
        StatusName state,
        MessageContentWrite contentWrite,
        DbTransaction? transaction,
        RetryDelay? retryDelay,
        DateTimeOffset? lockedUntil,
        int? originalRetries,
        int? originalInlineAttempts,
        CancellationToken cancellationToken
    )
    {
        var received = _IsReceived(table);
        var refreshContent = contentWrite is MessageContentWrite.Refresh;
        var content = refreshContent ? _RefreshContent(message) : null;
        var isTerminal = state is StatusName.Succeeded or StatusName.Failed && retryDelay is null;

        var set = new List<string>(capacity: 12);
        if (refreshContent)
        {
            set.Add($"{_t.Content}=@Content");
        }

        set.Add($"{_t.Retries}=@Retries");
        set.Add($"{_t.InlineAttempts}=@InlineAttempts");
        set.Add($"{_t.ExpiresAt}=@ExpiresAt");
        set.Add($"{_t.NextRetryAt}={_nextRetryAtAssignment}");
        set.Add($"{_t.LockedUntil}=@LockedUntil");
        set.Add($"{_t.Owner}=@Owner");
        set.Add($"{_t.StatusName}=@StatusName");

        // An inline retry passes the generation it holds (@OriginalInlineAttempts set): its transition applies only
        // while the row is still leased to it, so an attempt whose lease another node took over writes nothing.
        var fence =
            $"{_terminalGuardWithRetries} AND (@OriginalInlineAttempts IS NULL OR ({_Same(_t.LockedUntil, "OriginalLockedUntil")} AND {_Same(_t.Owner, "OriginalOwner")} AND {_t.LockedUntil} > {SqlDialectTokens.Now}))";

        if (received)
        {
            // The received table also records the failure, ends the inbox attempt when the lease is released, and
            // stamps an inbox generation's terminal time and the retention it starts.
            set.Add($"{_t.ExceptionInfo}=@ExceptionInfo");
            set.Add($"{_t.AttemptId}=CASE WHEN @LockedUntil IS NULL THEN NULL ELSE {_t.AttemptId} END");
            set.Add(
                $"{_t.TerminalAt}=CASE WHEN {_t.IsInboxRecord} = {_t.True} AND @IsTerminal = {_t.True} THEN {SqlDialectTokens.Now} ELSE {_t.TerminalAt} END"
            );
            set.Add(
                $"{_t.EffectiveExpiresAt}=CASE WHEN {_t.IsInboxRecord} = {_t.True} AND @IsTerminal = {_t.True} THEN {_dialect.ShiftBySeconds(SqlDialectTokens.Now, _t.InboxRetentionSeconds)} ELSE {_t.EffectiveExpiresAt} END"
            );
            fence += $" AND {_InboxAttemptGuard(matchStorageId: false)}";
        }

        return await _TransitionAsync(
                transaction,
                table,
                fence,
                string.Join(", ", set),
                [_t.Id],
                command =>
                {
                    if (refreshContent)
                    {
                        _dialect.AddParameter(command, "Content", _ContentType, content);
                    }

                    _dialect.AddParameter(command, "Id", SqlColumnType.Guid, message.StorageId);
                    _dialect.AddParameter(command, "Retries", SqlColumnType.Int32, message.Retries);
                    _dialect.AddParameter(command, "InlineAttempts", SqlColumnType.Int32, message.InlineAttempts);
                    _dialect.AddParameter(command, "ExpiresAt", SqlColumnType.Timestamp, message.ExpiresAt);
                    _BindRetryDelay(command, retryDelay);
                    _dialect.AddParameter(command, "LockedUntil", SqlColumnType.Timestamp, lockedUntil);
                    _BindOwner(command, "Owner", hasLease: lockedUntil is not null);
                    _dialect.AddParameter(command, "OriginalRetries", SqlColumnType.Int32, originalRetries);
                    _dialect.AddParameter(
                        command,
                        "OriginalInlineAttempts",
                        SqlColumnType.Int32,
                        originalInlineAttempts
                    );
                    _dialect.AddParameter(command, "OriginalLockedUntil", SqlColumnType.Timestamp, message.LockedUntil);
                    _dialect.AddParameter(command, "OriginalOwner", _ownerType, message.Owner);
                    _dialect.AddParameter(command, "StatusName", _StatusType, state.ToString("G"));

                    if (received)
                    {
                        _dialect.AddParameter(command, "ExceptionInfo", _ContentType, message.ExceptionInfo);
                        _dialect.AddParameter(command, "IsTerminal", SqlColumnType.Boolean, isTerminal);
                        _BindInboxAttempt(command, message.InboxAttemptFence, matchStorageId: false);
                    }
                },
                _ReadAppliedAsync,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public ValueTask<bool> ReservePublishAttemptAsync(
        MediumMessage message,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
    )
    {
        return _ReserveAttemptAsync(_publishedTable, message, originalInlineAttempts, cancellationToken);
    }

    public ValueTask<bool> ReserveReceiveAttemptAsync(
        MediumMessage message,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
    )
    {
        return _ReserveAttemptAsync(_receivedTable, message, originalInlineAttempts, cancellationToken);
    }

    private async ValueTask<bool> _ReserveAttemptAsync(
        string table,
        MediumMessage message,
        int originalInlineAttempts,
        CancellationToken cancellationToken
    )
    {
        // Durable attempt reservation: the counter is a compare-and-set on the attempt the caller read, and only the
        // live lease holder may move it.
        var received = _IsReceived(table);
        var fence =
            $"{_terminalGuardWithRetries} AND {_Same(_t.LockedUntil, "LockedUntil")} AND {_Same(_t.Owner, "CurrentOwner")} AND {_t.LockedUntil} > {SqlDialectTokens.Now}";
        if (received)
        {
            fence += $" AND {_InboxAttemptGuard(matchStorageId: true)}";
        }

        return await _TransitionAsync(
                transaction: null,
                table,
                fence,
                $"{_t.InlineAttempts}=@InlineAttempts",
                [_t.Id],
                command =>
                {
                    _dialect.AddParameter(command, "Id", SqlColumnType.Guid, message.StorageId);
                    _dialect.AddParameter(command, "InlineAttempts", SqlColumnType.Int32, message.InlineAttempts);
                    _dialect.AddParameter(command, "OriginalRetries", SqlColumnType.Int32, message.Retries);
                    _dialect.AddParameter(
                        command,
                        "OriginalInlineAttempts",
                        SqlColumnType.Int32,
                        originalInlineAttempts
                    );
                    _dialect.AddParameter(command, "LockedUntil", SqlColumnType.Timestamp, message.LockedUntil);
                    _dialect.AddParameter(command, "CurrentOwner", _ownerType, message.Owner);

                    if (received)
                    {
                        _BindInboxAttempt(command, message.InboxAttemptFence, matchStorageId: true);
                    }
                },
                _ReadAppliedAsync,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Acquires a dispatch lease on a published message by setting <c>LockedUntil</c> and <c>Owner</c>.
    /// Only succeeds if the row is currently unleased or its existing lease has expired.
    /// </summary>
    /// <returns><see langword="true"/> if the lease was acquired; <see langword="false"/> if another node already holds it.</returns>
    public ValueTask<bool> LeasePublishAsync(
        MediumMessage message,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default
    )
    {
        return _LeaseAsync(_publishedTable, message, leaseDuration, reserve: null, cancellationToken);
    }

    public ValueTask<bool> LeasePublishAndReserveAttemptAsync(
        MediumMessage message,
        TimeSpan leaseDuration,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
    )
    {
        return _LeaseAsync(_publishedTable, message, leaseDuration, originalInlineAttempts, cancellationToken);
    }

    /// <summary>
    /// Acquires a dispatch lease on a received message by setting <c>LockedUntil</c> and <c>Owner</c>.
    /// Only succeeds if the row is currently unleased or its existing lease has expired.
    /// </summary>
    /// <returns><see langword="true"/> if the lease was acquired; <see langword="false"/> if another node already holds it.</returns>
    public ValueTask<bool> LeaseReceiveAsync(
        MediumMessage message,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default
    )
    {
        return _LeaseAsync(_receivedTable, message, leaseDuration, reserve: null, cancellationToken);
    }

    public ValueTask<bool> LeaseReceiveAndReserveAttemptAsync(
        MediumMessage message,
        TimeSpan leaseDuration,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
    )
    {
        return _LeaseAsync(_receivedTable, message, leaseDuration, originalInlineAttempts, cancellationToken);
    }

    /// <summary>
    /// Takes the dispatch lease when the row is unleased or its lease has expired, and, given
    /// <paramref name="reserve"/>, reserves the next inline attempt in the same transition (the fresh-dispatch fast
    /// path). No owner match is required: this path is taking the lease, not holding it.
    /// </summary>
    /// <remarks>
    /// Ownership time is the database's: one clock reading decides whether the old lease expired and sets the new
    /// deadline, and the stored deadline is read back so the caller's fence matches durable state exactly.
    /// </remarks>
    private async ValueTask<bool> _LeaseAsync(
        string table,
        MediumMessage message,
        TimeSpan leaseDuration,
        int? reserve,
        CancellationToken cancellationToken
    )
    {
        // A received row starts a new inbox attempt with each lease it takes, so a stale attempt is fenced out.
        var newAttempt = reserve is not null && _IsReceived(table);
        var set = $"{_t.LockedUntil}={_dialect.ShiftByDuration(SqlDialectTokens.Now, "Lease")}, {_t.Owner}=@Owner";
        if (reserve is not null)
        {
            set += $", {_t.InlineAttempts}=@InlineAttempts";
        }

        if (newAttempt)
        {
            set += $", {_t.AttemptId}=CASE WHEN {_t.IsInboxRecord} = {_t.True} THEN {_dialect.NewGuid()} ELSE NULL END";
        }

        var fence =
            $"({_t.LockedUntil} IS NULL OR {_t.LockedUntil} <= {SqlDialectTokens.Now}) AND {(reserve is null ? _terminalGuard : _terminalGuardWithRetries)}";
        IReadOnlyList<string> returning = newAttempt
            ? [_t.LockedUntil, _t.Owner, _t.AttemptId]
            : [_t.LockedUntil, _t.Owner];

        var lease = await _TransitionAsync(
                transaction: null,
                table,
                fence,
                set,
                returning,
                command =>
                {
                    _dialect.AddParameter(command, "Id", SqlColumnType.Guid, message.StorageId);
                    _dialect.AddDuration(command, "Lease", leaseDuration);
                    _BindOwner(command, "Owner", hasLease: true);

                    if (reserve is not null)
                    {
                        _dialect.AddParameter(command, "InlineAttempts", SqlColumnType.Int32, message.InlineAttempts);
                        _dialect.AddParameter(command, "OriginalRetries", SqlColumnType.Int32, message.Retries);
                        _dialect.AddParameter(command, "OriginalInlineAttempts", SqlColumnType.Int32, reserve);
                    }
                },
                async (reader, ct) => await _ReadLeaseAsync(reader, newAttempt, ct).ConfigureAwait(false),
                cancellationToken
            )
            .ConfigureAwait(false);

        if (lease is not { } stored)
        {
            return false;
        }

        // Mirror the durable deadline the server issued, not a locally recomputed one.
        message.LockedUntil = stored.LockedUntil;
        message.Owner = stored.Owner;
        if (stored.AttemptId is { } attemptId && message.InboxGeneration is { } generation)
        {
            message.InboxAttemptFence = new InboxAttemptFence(
                message.StorageId,
                message.Lane,
                generation.Number,
                generation.IncarnationId,
                attemptId,
                stored.Owner,
                stored.LockedUntil
            );
        }

        return true;
    }

    private static async Task<(DateTimeOffset LockedUntil, string? Owner, Guid? AttemptId)?> _ReadLeaseAsync(
        DbDataReader reader,
        bool readsAttempt,
        CancellationToken cancellationToken
    )
    {
        if (!await _ReadAppliedAsync(reader, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var lockedUntil =
            await _ReadInstantAsync(reader, 1, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("A taken lease has no durable deadline.");
        var owner = await _ReadStringAsync(reader, 2, cancellationToken).ConfigureAwait(false);
        Guid? attemptId =
            readsAttempt && !await reader.IsDBNullAsync(3, cancellationToken).ConfigureAwait(false)
                ? reader.GetGuid(3)
                : null;

        return (lockedUntil, owner, attemptId);
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
    /// The identity a graceful release matches: the exact lease generation the store returned to its holder, so a
    /// release never clears a lease another node has taken since.
    /// </summary>
    private string _LeaseIdentityPredicate(bool received, string suffix)
    {
        var inboxGuard = received
            ? $" AND ({_t.IsInboxRecord} = {_t.False} OR ({_t.Id}=@InboxStorageId{suffix} AND {_t.IntentType}=@InboxIntentType{suffix} AND {_t.Generation}=@InboxGeneration{suffix} AND {_t.GenerationIncarnationId}=@InboxGenerationIncarnationId{suffix} AND {_t.AttemptId}=@InboxAttemptId{suffix} AND {_Same(_t.Owner, "InboxOwner" + suffix)} AND {_t.LockedUntil}=@InboxLockedUntil{suffix}))"
            : "";

        return $"({_t.Id}=@Id{suffix} AND {_t.IntentType}=@IntentType{suffix} AND {_Same(_t.Owner, "Owner" + suffix)} AND {_t.LockedUntil}=@LockedUntil{suffix}{inboxGuard})";
    }

    private void _BindLeaseIdentity(DbCommand command, MessageLeaseIdentity identity, bool received, string suffix)
    {
        var fence = identity.InboxAttemptFence;
        _dialect.AddParameter(command, "Id" + suffix, SqlColumnType.Guid, identity.StorageId);
        _dialect.AddParameter(
            command,
            "IntentType" + suffix,
            SqlColumnType.Int16,
            MessageLaneCompatibility.ToPersistedValue(identity.Lane)
        );
        _dialect.AddParameter(command, "Owner" + suffix, _ownerType, identity.Owner);
        _dialect.AddParameter(command, "LockedUntil" + suffix, SqlColumnType.Timestamp, identity.LockedUntil);

        if (!received)
        {
            return;
        }

        _dialect.AddParameter(command, "InboxStorageId" + suffix, SqlColumnType.Guid, fence?.StorageId);
        _dialect.AddParameter(
            command,
            "InboxIntentType" + suffix,
            SqlColumnType.Int16,
            fence is null ? null : MessageLaneCompatibility.ToPersistedValue(fence.Lane)
        );
        _dialect.AddParameter(command, "InboxGeneration" + suffix, SqlColumnType.Int64, fence?.Generation);
        _dialect.AddParameter(
            command,
            "InboxGenerationIncarnationId" + suffix,
            SqlColumnType.Guid,
            fence?.GenerationIncarnationId
        );
        _dialect.AddParameter(command, "InboxAttemptId" + suffix, SqlColumnType.Guid, fence?.AttemptId);
        _dialect.AddParameter(command, "InboxOwner" + suffix, _ownerType, fence?.Owner);
        _dialect.AddParameter(command, "InboxLockedUntil" + suffix, SqlColumnType.Timestamp, fence?.LockedUntil);
    }

    private async ValueTask<bool> _ReleaseLeaseAsync(
        string table,
        MessageLeaseIdentity identity,
        CancellationToken cancellationToken
    )
    {
        var received = _IsReceived(table);
        var sql =
            $"UPDATE {table} SET {_t.Owner}=NULL, {_t.LockedUntil}=NULL WHERE {_LeaseIdentityPredicate(received, "")} AND {_terminalGuard};";

        await using var connection = _CreateConnection();
        var changed = await RelationalCommand
            .ExecuteNonQueryAsync(
                connection,
                transaction: null,
                sql,
                CommandTimeoutSeconds,
                command => _BindLeaseIdentity(command, identity, received, ""),
                cancellationToken
            )
            .ConfigureAwait(false);

        return changed == 1;
    }

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
        // A received identity binds eleven parameters, a published one four.
        var batchSize = Math.Min(_LeaseReleaseBatchSize, _MaxCommandParameters / (received ? 11 : 4));
        var released = 0;

        await using var connection = _CreateConnection();
        foreach (var batch in identities.Chunk(batchSize))
        {
            var predicates = new string[batch.Length];
            for (var index = 0; index < batch.Length; index++)
            {
                predicates[index] = _LeaseIdentityPredicate(received, index.ToString(CultureInfo.InvariantCulture));
            }

            var sql =
                $"UPDATE {table} SET {_t.Owner}=NULL, {_t.LockedUntil}=NULL WHERE {_terminalGuard} AND ({string.Join(" OR ", predicates)});";
            released += await RelationalCommand
                .ExecuteNonQueryAsync(
                    connection,
                    transaction: null,
                    sql,
                    CommandTimeoutSeconds,
                    command =>
                    {
                        for (var index = 0; index < batch.Length; index++)
                        {
                            _BindLeaseIdentity(
                                command,
                                batch[index],
                                received,
                                index.ToString(CultureInfo.InvariantCulture)
                            );
                        }
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        return released;
    }

    public async ValueTask<bool> DeferReceivedRetryAsync(
        CircuitRetryDeferral deferral,
        CancellationToken cancellationToken = default
    )
    {
        var identity = deferral.Identity;
        var fence =
            $"{_t.IntentType}=@IntentType AND {_Same(_t.Owner, "Owner")} AND {_t.LockedUntil}=@LockedUntil AND {_t.LockedUntil} > {SqlDialectTokens.Now} AND {_InboxAttemptGuard(matchStorageId: true)} AND {_terminalGuard}";

        return await _TransitionAsync(
                transaction: null,
                _receivedTable,
                fence,
                $"{_t.NextRetryAt}={_dialect.ShiftByDuration(SqlDialectTokens.Now, "RetryDelay")}, {_t.Owner}=NULL, {_t.LockedUntil}=NULL",
                [_t.Id],
                command =>
                {
                    _dialect.AddParameter(command, "Id", SqlColumnType.Guid, identity.StorageId);
                    _dialect.AddParameter(
                        command,
                        "IntentType",
                        SqlColumnType.Int16,
                        MessageLaneCompatibility.ToPersistedValue(identity.Lane)
                    );
                    _dialect.AddParameter(command, "Owner", _ownerType, identity.Owner);
                    _dialect.AddParameter(command, "LockedUntil", SqlColumnType.Timestamp, identity.LockedUntil);
                    _dialect.AddDuration(command, "RetryDelay", deferral.Delay);
                    _BindInboxAttempt(command, identity.InboxAttemptFence, matchStorageId: true);
                },
                _ReadAppliedAsync,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public ValueTask<bool> DeferReceivedInboxOrphanAsync(
        MediumMessage message,
        CancellationToken cancellationToken = default
    ) => _SetInboxRoutabilityAsync(message, orphaned: true, cancellationToken);

    public ValueTask<bool> ConfirmReceivedInboxRoutableAsync(
        MediumMessage message,
        CancellationToken cancellationToken = default
    ) => _SetInboxRoutabilityAsync(message, orphaned: false, cancellationToken);

    // Orphaning defers the probe and releases ownership; confirming keeps the live claim so the caller can dispatch.
    // Both accept an already-matching orphan flag because the fence, not a state change, authorizes the caller.
    private async ValueTask<bool> _SetInboxRoutabilityAsync(
        MediumMessage message,
        bool orphaned,
        CancellationToken cancellationToken = default
    )
    {
        if (message.InboxAttemptFence is not { } fence || message.InboxGeneration is null)
        {
            return false;
        }

        // The next orphan probe falls due on the database clock, the clock the retry pickup compares it against.
        var set = orphaned
            ? $"{_t.IsInboxOrphaned}={_t.True}, {_t.NextRetryAt}={_dialect.ShiftByDuration(SqlDialectTokens.Now, "ProbeInterval")}, {_t.Owner}=NULL, {_t.LockedUntil}=NULL"
            : $"{_t.IsInboxOrphaned}={_t.False}";
        var fencePredicate =
            $"{_t.IntentType}=@IntentType AND {_t.Generation}=@Generation AND {_t.GenerationIncarnationId}=@GenerationIncarnationId AND {_t.AttemptId}=@AttemptId AND {_Same(_t.Owner, "Owner")} AND {_t.LockedUntil}=@LockedUntil AND {_t.LockedUntil} > {SqlDialectTokens.Now}";

        var (changed, nextRetryAt) = await _TransitionAsync(
                transaction: null,
                _receivedTable,
                fencePredicate,
                set,
                [_t.NextRetryAt],
                command =>
                {
                    _dialect.AddParameter(command, "Id", SqlColumnType.Guid, fence.StorageId);
                    _dialect.AddDuration(command, "ProbeInterval", Options.OrphanProbeInterval);
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
                    _dialect.AddParameter(command, "AttemptId", SqlColumnType.Guid, fence.AttemptId);
                    _dialect.AddParameter(command, "Owner", _ownerType, fence.Owner);
                    _dialect.AddParameter(command, "LockedUntil", SqlColumnType.Timestamp, fence.LockedUntil);
                },
                static async (reader, ct) =>
                    await _ReadAppliedAsync(reader, ct).ConfigureAwait(false)
                        ? (true, await _ReadInstantAsync(reader, 1, ct).ConfigureAwait(false))
                        : (false, null),
                cancellationToken
            )
            .ConfigureAwait(false);

        if (changed)
        {
            message.IsInboxOrphaned = orphaned;
            if (orphaned)
            {
                message.NextRetryAt = nextRetryAt;
                message.Owner = null;
                message.LockedUntil = null;
            }
        }

        return changed;
    }
}
