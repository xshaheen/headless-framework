// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.Abstractions;
using Headless.Coordination;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.Serialization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

#pragma warning disable RCS1084 // Use coalesce expression instead of conditional expression
namespace Headless.Messaging.Storage.SqlServer;

/// <summary>
/// SQL Server implementation of <see cref="IDataStorage"/> for message persistence.
/// Handles storage, retrieval, and state transitions for published and received messages.
/// </summary>
internal sealed class SqlServerDataStorage(
    IOptions<MessagingOptions> messagingOptions,
    IOptions<SqlServerOptions> options,
    IStorageInitializer initializer,
    ISerializer serializer,
    [FromKeyedServices(SequentialGuidType.SqlServer)] IGuidGenerator guidGenerator,
    TimeProvider timeProvider,
    INodeMembership nodeMembership,
    ILogger<SqlServerDataStorage> logger
) : IDataStorage
{
    /// <summary>
    /// Reusable WHERE-clause fragment that refuses updates to rows already in a terminal state
    /// (<c>Succeeded</c> / <c>Failed</c>) with no scheduled retry, while still respecting an
    /// optional optimistic-concurrency token (<c>@OriginalRetries</c>). Used by Change*StateAsync
    /// paths that pass <c>@OriginalRetries</c>.
    /// </summary>
    private const string _TerminalRowGuardWithRetries =
        "NOT (StatusName IN ('Succeeded','Failed') AND NextRetryAt IS NULL) AND (@OriginalRetries IS NULL OR Retries=@OriginalRetries) AND (@OriginalInlineAttempts IS NULL OR InlineAttempts=@OriginalInlineAttempts)";

    /// <summary>
    /// Reusable WHERE-clause fragment for paths that do not supply <c>@OriginalRetries</c>
    /// (lease / store-received MERGE). Same terminal-row semantics as
    /// <see cref="_TerminalRowGuardWithRetries"/> without the concurrency-token clause.
    /// </summary>
    private const string _TerminalRowGuardSimple = "NOT (StatusName IN ('Succeeded','Failed') AND NextRetryAt IS NULL)";

    /// <summary>
    /// Lookahead window for delayed messages.
    /// Messages expiring within this window are pre-fetched for scheduling.
    /// </summary>
    private static readonly TimeSpan _DelayedMessageLookahead = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Lookback window for queued messages that may have been lost.
    /// Messages queued longer than this are re-scheduled.
    /// </summary>
    private static readonly TimeSpan _QueuedMessageLookback = TimeSpan.FromMinutes(1);

    private readonly string _publishedTable = initializer.GetPublishedTableName();
    private readonly string _receivedTable = initializer.GetReceivedTableName();

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

        var tvpParam = _BuildIdListTvpParameter(storageIds);
        var statusParam = new SqlParameter("@StatusName", nameof(StatusName.Delayed));

        var sql =
            $"UPDATE {_publishedTable} SET [StatusName]=@StatusName WHERE [Id] IN (SELECT [Id] FROM @Ids) AND {_TerminalRowGuardSimple};";

        await using var connection = new SqlConnection(options.Value.ConnectionString);

        await connection
            .ExecuteNonQueryAsync(
                sql,
                commandTimeout: messagingOptions.Value.CommandTimeout,
                sqlParams: [tvpParam, statusParam],
                cancellationToken: cancellationToken
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
        object? transaction = null,
        DateTime? nextRetryAt = null,
        DateTime? lockedUntil = null,
        int? originalRetries = null,
        CancellationToken cancellationToken = default
    )
    {
        return _ChangeMessageStateAsync(
            _publishedTable,
            message,
            state,
            transaction,
            nextRetryAt,
            lockedUntil,
            originalRetries,
            originalInlineAttempts: null,
            cancellationToken
        );
    }

    public ValueTask<bool> ChangePublishRetryStateAsync(
        MediumMessage message,
        StatusName state,
        DateTime? nextRetryAt,
        DateTime? lockedUntil,
        int originalRetries,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
    ) =>
        _ChangeMessageStateAsync(
            _publishedTable,
            message,
            state,
            transaction: null,
            nextRetryAt,
            lockedUntil,
            originalRetries,
            originalInlineAttempts,
            cancellationToken
        );

    public ValueTask<bool> ReservePublishAttemptAsync(
        MediumMessage message,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
    ) => _ReserveAttemptAsync(_publishedTable, message, originalInlineAttempts, cancellationToken);

    /// <summary>
    /// Acquires a dispatch lease on a published message by setting <c>LockedUntil</c> and <c>Owner</c>.
    /// Only succeeds if the row is currently unleased or its existing lease has expired.
    /// </summary>
    /// <returns><see langword="true"/> if the lease was acquired; <see langword="false"/> if another node already holds it.</returns>
    public ValueTask<bool> LeasePublishAsync(
        MediumMessage message,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default
    ) => _LeaseMessageAsync(_publishedTable, message, leaseDuration, cancellationToken);

    public ValueTask<bool> LeasePublishAndReserveAttemptAsync(
        MediumMessage message,
        TimeSpan leaseDuration,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
    ) =>
        _LeaseAndReserveAttemptAsync(
            _publishedTable,
            message,
            leaseDuration,
            originalInlineAttempts,
            cancellationToken
        );

    /// <summary>
    /// Updates the status of a received message, including writing <c>ExceptionInfo</c> when the
    /// message faulted. Respects the terminal-row guard — permanently completed rows are not mutated.
    /// </summary>
    /// <returns><see langword="true"/> if a row was updated; <see langword="false"/> if the guard blocked it.</returns>
    public ValueTask<bool> ChangeReceiveStateAsync(
        MediumMessage message,
        StatusName state,
        DateTime? nextRetryAt = null,
        DateTime? lockedUntil = null,
        int? originalRetries = null,
        CancellationToken cancellationToken = default
    ) =>
        _ChangeReceiveStateAsync(
            message,
            state,
            nextRetryAt,
            lockedUntil,
            originalRetries,
            originalInlineAttempts: null,
            cancellationToken
        );

    public ValueTask<bool> ChangeReceiveRetryStateAsync(
        MediumMessage message,
        StatusName state,
        DateTime? nextRetryAt,
        DateTime? lockedUntil,
        int originalRetries,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
    ) =>
        _ChangeReceiveStateAsync(
            message,
            state,
            nextRetryAt,
            lockedUntil,
            originalRetries,
            originalInlineAttempts,
            cancellationToken
        );

    public ValueTask<bool> ReserveReceiveAttemptAsync(
        MediumMessage message,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
    ) => _ReserveAttemptAsync(_receivedTable, message, originalInlineAttempts, cancellationToken);

    private async ValueTask<bool> _ChangeReceiveStateAsync(
        MediumMessage message,
        StatusName state,
        DateTime? nextRetryAt,
        DateTime? lockedUntil,
        int? originalRetries,
        int? originalInlineAttempts,
        CancellationToken cancellationToken
    )
    {
        // NOTE: ChangeReceiveStateAsync does not call _ChangeMessageStateAsync because the receive
        // path additionally writes ExceptionInfo, a column absent from the published table schema.
        // Keep these two methods in sync when adding columns.
        var sql =
            // X1 terminal-row guard: refuses updates to rows that are already terminal AND
            // have NextRetryAt cleared. Failed rows with non-null NextRetryAt stay mutable so
            // the retry processor can rewrite them — see the matching note in PostgreSqlDataStorage.
            $"DECLARE @LeaseNow datetime2(7) = SYSUTCDATETIME(); UPDATE {_receivedTable} SET Content=@Content, Retries=@Retries, InlineAttempts=@InlineAttempts, ExpiresAt=@ExpiresAt, NextRetryAt=@NextRetryAt, LockedUntil=@LockedUntil, Owner=@Owner, StatusName=@StatusName, ExceptionInfo=@ExceptionInfo WHERE Id=@Id AND {_TerminalRowGuardWithRetries} AND (@OriginalInlineAttempts IS NULL OR (((LockedUntil IS NULL AND @OriginalLockedUntil IS NULL) OR LockedUntil=@OriginalLockedUntil) AND ((Owner IS NULL AND @OriginalOwner IS NULL) OR Owner=@OriginalOwner) AND LockedUntil>@LeaseNow))";

        object[] sqlParams =
        [
            new SqlParameter("@Id", message.StorageId),
            new SqlParameter("@Content", serializer.Serialize(message.Origin)),
            new SqlParameter("@Retries", message.Retries),
            new SqlParameter("@InlineAttempts", message.InlineAttempts),
            new SqlParameter("@ExpiresAt", SqlDbType.DateTime2)
            {
                Value = message.ExpiresAt.HasValue ? message.ExpiresAt.Value : DBNull.Value,
            },
            new SqlParameter("@NextRetryAt", SqlDbType.DateTime2) { Value = nextRetryAt.ToUtcParameterValue() },
            new SqlParameter("@LockedUntil", SqlDbType.DateTime2) { Value = lockedUntil.ToUtcParameterValue() },
            _OwnerParameter("@Owner", lockedUntil),
            new SqlParameter("@OriginalRetries", SqlDbType.Int) { Value = originalRetries ?? (object)DBNull.Value },
            new SqlParameter("@OriginalInlineAttempts", SqlDbType.Int)
            {
                Value = originalInlineAttempts ?? (object)DBNull.Value,
            },
            new SqlParameter("@OriginalLockedUntil", SqlDbType.DateTime2)
            {
                Value = message.LockedUntil.ToUtcParameterValue(),
            },
            new SqlParameter("@OriginalOwner", SqlDbType.NVarChar, options.Value.OwnerColumnMaxLength)
            {
                Value = message.Owner ?? (object)DBNull.Value,
            },
            new SqlParameter("@StatusName", state.ToString("G")),
            new SqlParameter("@ExceptionInfo", message.ExceptionInfo ?? (object)DBNull.Value),
        ];

        await using var connection = new SqlConnection(options.Value.ConnectionString);

        var affectedRows = await connection
            .ExecuteNonQueryAsync(
                sql,
                commandTimeout: messagingOptions.Value.CommandTimeout,
                sqlParams: sqlParams,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        return affectedRows > 0;
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
    ) => _LeaseMessageAsync(_receivedTable, message, leaseDuration, cancellationToken);

    public ValueTask<bool> LeaseReceiveAndReserveAttemptAsync(
        MediumMessage message,
        TimeSpan leaseDuration,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
    ) =>
        _LeaseAndReserveAttemptAsync(_receivedTable, message, leaseDuration, originalInlineAttempts, cancellationToken);

    /// <summary>
    /// Persists a published outbox message to the <c>Published</c> table. When <paramref name="transaction"/>
    /// is supplied the INSERT participates in the caller's database transaction (transactional outbox).
    /// </summary>
    /// <returns>The stored <c>MediumMessage</c> with its generated <c>StorageId</c> and timestamps populated.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="transaction"/> is a non-null type that is neither
    /// <c>DbTransaction</c> nor <c>IDbContextTransaction</c>.
    /// </exception>
    public async ValueTask<MediumMessage> StoreMessageAsync(
        string name,
        MediumMessage message,
        object? transaction = null,
        CancellationToken cancellationToken = default
    )
    {
        var sql =
            $"INSERT INTO {_publishedTable} ([Id],[Version],[Name],[Content],[IntentType],[Retries],[InlineAttempts],[Added],[ExpiresAt],[NextRetryAt],[LockedUntil],[Owner],[StatusName],[MessageId])"
            + $"VALUES(@Id,'{messagingOptions.Value.Version}',@Name,@Content,@IntentType,@Retries,@InlineAttempts,@Added,@ExpiresAt,@NextRetryAt,@LockedUntil,@Owner,@StatusName,@MessageId);";

        var added = timeProvider.GetUtcNow().UtcDateTime;
        var stored = new MediumMessage
        {
            StorageId = guidGenerator.Create(),
            Origin = message.Origin,
            Content = serializer.Serialize(message.Origin),
            IntentType = message.IntentType,
            Added = added,
            ExpiresAt = null,
            NextRetryAt = added.Add(messagingOptions.Value.RetryPolicy.InitialDispatchGrace),
            LockedUntil = null,
            Owner = null,
            Retries = 0,
            InlineAttempts = 0,
        };

        object[] sqlParams =
        [
            new SqlParameter("@Id", stored.StorageId),
            new SqlParameter("@Name", name),
            new SqlParameter("@Content", stored.Content),
            new SqlParameter("@IntentType", SqlDbType.SmallInt) { Value = (short)stored.IntentType },
            new SqlParameter("@Retries", stored.Retries),
            new SqlParameter("@InlineAttempts", stored.InlineAttempts),
            new SqlParameter("@Added", stored.Added),
            new SqlParameter("@ExpiresAt", SqlDbType.DateTime2)
            {
                Value = stored.ExpiresAt.HasValue ? stored.ExpiresAt.Value : DBNull.Value,
            },
            new SqlParameter("@NextRetryAt", SqlDbType.DateTime2) { Value = stored.NextRetryAt.ToUtcParameterValue() },
            new SqlParameter("@LockedUntil", SqlDbType.DateTime2) { Value = stored.LockedUntil.ToUtcParameterValue() },
            _OwnerParameter("@Owner", stored.LockedUntil),
            new SqlParameter("@StatusName", nameof(StatusName.Scheduled)),
            new SqlParameter("@MessageId", message.Origin.Id),
        ];

        if (transaction == null)
        {
            await using var connection = new SqlConnection(options.Value.ConnectionString);
            await connection
                .ExecuteNonQueryAsync(
                    sql,
                    commandTimeout: messagingOptions.Value.CommandTimeout,
                    sqlParams: sqlParams,
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);
        }
        else
        {
            var dbTrans = transaction as DbTransaction;
            if (dbTrans == null && transaction is IDbContextTransaction dbContextTrans)
            {
                dbTrans = dbContextTrans.GetDbTransaction();
            }

            if (dbTrans is null)
            {
                throw new InvalidOperationException(
                    $"Unsupported transaction type '{transaction.GetType().FullName}'. Expected DbTransaction or IDbContextTransaction."
                );
            }

            var connection =
                dbTrans.Connection
                ?? throw new InvalidOperationException(
                    "The supplied DbTransaction has no active Connection — it may have already been committed or rolled back."
                );

            await connection
                .ExecuteNonQueryAsync(
                    sql,
                    transaction: dbTrans,
                    commandTimeout: messagingOptions.Value.CommandTimeout,
                    sqlParams: sqlParams,
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);
        }

        return stored;
    }

    /// <summary>
    /// Persists a published outbox message built from a raw <c>Message</c> payload to the <c>Published</c> table.
    /// Convenience overload that wraps <paramref name="content"/> in a <c>MediumMessage</c> before storing.
    /// </summary>
    /// <returns>The stored <c>MediumMessage</c> with its generated <c>StorageId</c> and timestamps populated.</returns>
    public ValueTask<MediumMessage> StoreMessageAsync(
        string name,
        Message content,
        object? transaction = null,
        CancellationToken cancellationToken = default
    ) =>
        StoreMessageAsync(
            name,
            new MediumMessage
            {
                StorageId = Guid.Empty,
                Origin = content,
                Content = string.Empty,
                IntentType = IntentType.Bus,
            },
            transaction,
            cancellationToken
        );

    /// <summary>
    /// Stores a received message that failed before it could be dispatched, using its raw serialized
    /// <paramref name="content"/> string. The row is written directly into the <c>Failed</c> state with
    /// the maximum retry count so it will not be re-picked up by the normal retry path.
    /// </summary>
    /// <returns><see langword="true"/> if a new row was inserted or an existing non-terminal row was updated.</returns>
    public async ValueTask<bool> StoreReceivedExceptionMessageAsync(
        string name,
        string group,
        string content,
        string? exceptionInfo = null,
        CancellationToken cancellationToken = default
    )
    {
        var origin = serializer.Deserialize(content)!;
        return await StoreReceivedExceptionMessageAsync(
                name,
                group,
                new MediumMessage
                {
                    StorageId = Guid.Empty,
                    Origin = origin,
                    Content = content,
                    IntentType = IntentType.Bus,
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
        string group,
        MediumMessage message,
        string? exceptionInfo = null,
        CancellationToken cancellationToken = default
    )
    {
        object[] sqlParams =
        [
            new SqlParameter("@Id", guidGenerator.Create()),
            new SqlParameter("@Name", name),
            new SqlParameter("@Group", SqlDbType.NVarChar, 200) { Value = (object?)group ?? DBNull.Value },
            new SqlParameter(
                "@Content",
                string.IsNullOrEmpty(message.Content) ? serializer.Serialize(message.Origin) : message.Content
            ),
            new SqlParameter("@IntentType", SqlDbType.SmallInt) { Value = (short)message.IntentType },
            new SqlParameter("@Retries", messagingOptions.Value.RetryPolicy.MaxPersistedRetries),
            new SqlParameter("@InlineAttempts", message.InlineAttempts),
            new SqlParameter("@Added", timeProvider.GetUtcNow().UtcDateTime),
            new SqlParameter("@ExpiresAt", SqlDbType.DateTime2)
            {
                Value = timeProvider
                    .GetUtcNow()
                    .UtcDateTime.AddSeconds(messagingOptions.Value.FailedMessageExpiredAfter),
            },
            new SqlParameter("@NextRetryAt", SqlDbType.DateTime2) { Value = DBNull.Value },
            new SqlParameter("@LockedUntil", SqlDbType.DateTime2) { Value = DBNull.Value },
            _OwnerParameter("@Owner", lockedUntil: null),
            new SqlParameter("@StatusName", nameof(StatusName.Failed)),
            new SqlParameter("@MessageId", message.Origin.Id),
            new SqlParameter("@Version", messagingOptions.Value.Version),
            new SqlParameter("@ExceptionInfo", exceptionInfo ?? (object)DBNull.Value),
        ];

        var rowId = await _StoreReceivedMessage(sqlParams, cancellationToken).ConfigureAwait(false);
        return rowId is not null;
    }

    /// <summary>
    /// Persists an inbound message to the <c>Received</c> table using an atomic MERGE statement.
    /// Concurrent broker redeliveries of the same message are collapsed to a single row; the
    /// terminal-row guard ensures already-succeeded rows are never overwritten.
    /// </summary>
    /// <returns>The stored <c>MediumMessage</c> with its generated <c>StorageId</c> and timestamps populated.</returns>
    public async ValueTask<MediumMessage> StoreReceivedMessageAsync(
        string name,
        string group,
        MediumMessage message,
        CancellationToken cancellationToken = default
    )
    {
        var added = timeProvider.GetUtcNow().UtcDateTime;
        var mediumMessage = new MediumMessage
        {
            StorageId = guidGenerator.Create(),
            Origin = message.Origin,
            Content = serializer.Serialize(message.Origin),
            IntentType = message.IntentType,
            Added = added,
            ExpiresAt = null,
            NextRetryAt = added.Add(messagingOptions.Value.RetryPolicy.InitialDispatchGrace),
            LockedUntil = null,
            Owner = null,
            Retries = 0,
            InlineAttempts = 0,
        };

        object[] sqlParams =
        [
            new SqlParameter("@Id", mediumMessage.StorageId),
            new SqlParameter("@Name", name),
            new SqlParameter("@Group", SqlDbType.NVarChar, 200) { Value = (object?)group ?? DBNull.Value },
            new SqlParameter("@Content", mediumMessage.Content),
            new SqlParameter("@IntentType", SqlDbType.SmallInt) { Value = (short)mediumMessage.IntentType },
            new SqlParameter("@Retries", mediumMessage.Retries),
            new SqlParameter("@InlineAttempts", mediumMessage.InlineAttempts),
            new SqlParameter("@Added", mediumMessage.Added),
            new SqlParameter("@ExpiresAt", SqlDbType.DateTime2)
            {
                Value = mediumMessage.ExpiresAt.HasValue ? mediumMessage.ExpiresAt.Value : DBNull.Value,
            },
            new SqlParameter("@NextRetryAt", SqlDbType.DateTime2)
            {
                Value = mediumMessage.NextRetryAt.ToUtcParameterValue(),
            },
            new SqlParameter("@LockedUntil", SqlDbType.DateTime2)
            {
                Value = mediumMessage.LockedUntil.ToUtcParameterValue(),
            },
            _OwnerParameter("@Owner", mediumMessage.LockedUntil),
            new SqlParameter("@StatusName", nameof(StatusName.Scheduled)),
            new SqlParameter("@MessageId", message.Origin.Id),
            new SqlParameter("@Version", messagingOptions.Value.Version),
            new SqlParameter("@ExceptionInfo", DBNull.Value),
        ];

        // #5 — adopt the authoritative persisted row id (see _StoreReceivedMessage); on the MERGE UPDATE
        // branch the row keeps its original [Id], so the freshly-generated StorageId would be stale and the
        // caller's later ChangeReceiveStateAsync (WHERE Id=@Id) would silently no-op.
        var rowId = await _StoreReceivedMessage(sqlParams, cancellationToken).ConfigureAwait(false);
        if (rowId is { } id)
        {
            mediumMessage.StorageId = id;
        }

        return mediumMessage;
    }

    /// <summary>
    /// Persists an inbound message built from a raw <c>Message</c> payload to the <c>Received</c> table.
    /// Convenience overload that wraps <paramref name="message"/> in a <c>MediumMessage</c> before storing.
    /// </summary>
    /// <returns>The stored <c>MediumMessage</c> with its generated <c>StorageId</c> and timestamps populated.</returns>
    public ValueTask<MediumMessage> StoreReceivedMessageAsync(
        string name,
        string group,
        Message message,
        CancellationToken cancellationToken = default
    ) =>
        StoreReceivedMessageAsync(
            name,
            group,
            new MediumMessage
            {
                StorageId = Guid.Empty,
                Origin = message,
                Content = string.Empty,
                IntentType = IntentType.Bus,
            },
            cancellationToken
        );

    /// <summary>
    /// Deletes expired terminal messages from the specified table in batches.
    /// Only rows in <c>Succeeded</c> or <c>Failed</c> state with <c>ExpiresAt</c> before
    /// <paramref name="timeout"/> and no pending retry (<c>NextRetryAt IS NULL</c>) are removed.
    /// </summary>
    /// <returns>The number of rows deleted.</returns>
    public async ValueTask<int> DeleteExpiresAsync(
        string table,
        DateTime timeout,
        int batchCount = 1000,
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = new SqlConnection(options.Value.ConnectionString);

        return await connection
            .ExecuteNonQueryAsync(
                $"""
                DELETE FROM {table}
                 WHERE Id IN (
                     SELECT TOP (@batchCount) Id
                     FROM {table} WITH (READPAST)
                     WHERE ExpiresAt < @timeout
                     AND StatusName IN('{nameof(StatusName.Succeeded)}','{nameof(StatusName.Failed)}')
                     AND NextRetryAt IS NULL
                 );
                """,
                transaction: null,
                commandTimeout: messagingOptions.Value.CommandTimeout,
                sqlParams: [new SqlParameter("@timeout", timeout), new SqlParameter("@batchCount", batchCount)],
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Fetches published messages eligible for retry dispatch. Uses an atomic UPDATE with
    /// <c>OUTPUT INSERTED</c> to lease and return rows in a single round-trip, preventing
    /// double-dispatch across replicas.
    /// </summary>
    public ValueTask<IEnumerable<MediumMessage>> GetPublishedMessagesOfNeedRetryAsync(
        CancellationToken cancellationToken = default
    )
    {
        return _GetMessagesOfNeedRetryAsync(_publishedTable, cancellationToken);
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
    /// Fetches received messages eligible for retry dispatch. Uses an atomic UPDATE with
    /// <c>OUTPUT INSERTED</c> to lease and return rows in a single round-trip, preventing
    /// double-dispatch across replicas.
    /// </summary>
    public ValueTask<IEnumerable<MediumMessage>> GetReceivedMessagesOfNeedRetryAsync(
        CancellationToken cancellationToken = default
    )
    {
        return _GetMessagesOfNeedRetryAsync(_receivedTable, cancellationToken);
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

    /// <summary>Deletes a single received message by its storage identifier.</summary>
    /// <returns>1 if the row was deleted; 0 if not found.</returns>
    public async ValueTask<int> DeleteReceivedMessageAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var sql = $"DELETE FROM {_receivedTable} WHERE Id=@Id";

        await using var connection = new SqlConnection(options.Value.ConnectionString);

        var affectedRowCount = await connection
            .ExecuteNonQueryAsync(
                sql,
                commandTimeout: messagingOptions.Value.CommandTimeout,
                sqlParams: [new SqlParameter("@Id", id)],
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        return affectedRowCount;
    }

    /// <summary>Deletes a single published message by its storage identifier.</summary>
    /// <returns>1 if the row was deleted; 0 if not found.</returns>
    public async ValueTask<int> DeletePublishedMessageAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var sql = $"DELETE FROM {_publishedTable} WHERE Id=@Id";

        await using var connection = new SqlConnection(options.Value.ConnectionString);

        var affectedRowCount = await connection
            .ExecuteNonQueryAsync(
                sql,
                commandTimeout: messagingOptions.Value.CommandTimeout,
                sqlParams: [new SqlParameter("@Id", id)],
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        return affectedRowCount;
    }

    /// <summary>Deletes a batch of received messages by their storage identifiers.</summary>
    /// <returns>The number of rows deleted.</returns>
    public async ValueTask<int> DeleteReceivedMessagesAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken = default
    )
    {
        if (ids.Count == 0)
        {
            return 0;
        }

        var sqlParams = new object[] { _BuildIdListTvpParameter(ids) };

        var sql = $"DELETE FROM {_receivedTable} WHERE Id IN (SELECT Id FROM @Ids)";

        await using var connection = new SqlConnection(options.Value.ConnectionString);
        return await connection
            .ExecuteNonQueryAsync(
                sql,
                commandTimeout: messagingOptions.Value.CommandTimeout,
                sqlParams: sqlParams,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>Deletes a batch of published messages by their storage identifiers.</summary>
    /// <returns>The number of rows deleted.</returns>
    public async ValueTask<int> DeletePublishedMessagesAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken = default
    )
    {
        if (ids.Count == 0)
        {
            return 0;
        }

        var sqlParams = new object[] { _BuildIdListTvpParameter(ids) };

        var sql = $"DELETE FROM {_publishedTable} WHERE Id IN (SELECT Id FROM @Ids)";

        await using var connection = new SqlConnection(options.Value.ConnectionString);
        return await connection
            .ExecuteNonQueryAsync(
                sql,
                commandTimeout: messagingOptions.Value.CommandTimeout,
                sqlParams: sqlParams,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the <c>@Ids</c> table-valued parameter backed by the <c>HeadlessMessagingIdList</c> type
    /// (provisioned by the storage initializer). Using a TVP keeps the SQL text and parameter shape
    /// constant regardless of id count, so SQL Server reuses a single cached query plan — and it stays
    /// portable to older engines (table types need no OPENJSON / compatibility level 130).
    /// </summary>
    private SqlParameter _BuildIdListTvpParameter(IReadOnlyList<Guid> ids)
    {
        var idsTable = new DataTable();
        idsTable.Columns.Add("Id", typeof(Guid));
        foreach (var id in ids)
        {
            idsTable.Rows.Add(id);
        }

        return new SqlParameter("@Ids", SqlDbType.Structured)
        {
            TypeName = $"[{options.Value.Schema}].[HeadlessMessagingIdList]",
            Value = idsTable,
        };
    }

    /// <summary>
    /// Atomically selects delayed and stale-queued messages within a database transaction and
    /// invokes <paramref name="scheduleTask"/> to re-enqueue them. Uses branch-bounded ordered
    /// <c>TOP</c> reads with <c>UPDLOCK, READPAST</c> so concurrent replicas skip rows another
    /// node is scheduling without locking an unbounded candidate set.
    /// The transaction is committed after <paramref name="scheduleTask"/> completes.
    /// </summary>
    public async ValueTask ScheduleMessagesOfDelayedAsync(
        Func<object?, IEnumerable<MediumMessage>, ValueTask> scheduleTask,
        CancellationToken cancellationToken = default
    )
    {
        var sql = $"""
            WITH DelayedCandidates AS (
                SELECT TOP (@BatchSize) Id, Content, IntentType, Retries, InlineAttempts, Added, ExpiresAt
                FROM {_publishedTable} WITH (UPDLOCK, READPAST)
                WHERE Version = @Version AND StatusName = @DelayedStatusName AND ExpiresAt < @TwoMinutesLater
                ORDER BY ExpiresAt, Id
            ),
            QueuedCandidates AS (
                SELECT TOP (@BatchSize) Id, Content, IntentType, Retries, InlineAttempts, Added, ExpiresAt
                FROM {_publishedTable} WITH (UPDLOCK, READPAST)
                WHERE Version = @Version AND StatusName = @QueuedStatusName AND ExpiresAt < @OneMinutesAgo
                ORDER BY ExpiresAt, Id
            ),
            Candidates AS (
                SELECT Id, Content, IntentType, Retries, InlineAttempts, Added, ExpiresAt FROM DelayedCandidates
                UNION ALL
                SELECT Id, Content, IntentType, Retries, InlineAttempts, Added, ExpiresAt FROM QueuedCandidates
            )
            SELECT TOP (@BatchSize) Id, Content, IntentType, Retries, InlineAttempts, Added, ExpiresAt
            FROM Candidates
            ORDER BY ExpiresAt, Id;
            """;

        object[] sqlParams =
        [
            new SqlParameter("@Version", messagingOptions.Value.Version),
            new SqlParameter("@DelayedStatusName", nameof(StatusName.Delayed)),
            new SqlParameter("@QueuedStatusName", nameof(StatusName.Queued)),
            new SqlParameter("@TwoMinutesLater", timeProvider.GetUtcNow().UtcDateTime.Add(_DelayedMessageLookahead)),
            new SqlParameter("@OneMinutesAgo", timeProvider.GetUtcNow().UtcDateTime.Subtract(_QueuedMessageLookback)),
            new SqlParameter("@BatchSize", messagingOptions.Value.SchedulerBatchSize),
        ];

        await using var connection = new SqlConnection(options.Value.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var poisonMessages = new List<PoisonMessage>();
        var messageList = await connection
            .ExecuteReaderAsync(
                sql,
                async (reader, ct) =>
                {
                    var messages = new List<MediumMessage>();
                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        var storageId = reader.GetGuid(0);
                        var content = reader.GetString(1);

                        MediumMessage mediumMessage;
                        try
                        {
                            mediumMessage = new MediumMessage
                            {
                                StorageId = storageId,
                                Origin = serializer.Deserialize(content)!,
                                Content = content,
                                IntentType = (IntentType)reader.GetInt16(2),
                                Retries = reader.GetInt32(3),
                                InlineAttempts = reader.GetInt32(4),
                                Added = reader.GetDateTime(5),
                                ExpiresAt = reader.GetDateTime(6),
                            };
                        }
#pragma warning disable CA1031 // deliberately broad: one un-deserializable row must not abort the schedule batch (#3)
                        catch (Exception ex)
#pragma warning restore CA1031
                        {
                            logger.LogPoisonMessageSkipped(storageId, _publishedTable, ex);
                            poisonMessages.Add(_CreatePoisonMessage(storageId, ex));
                            continue;
                        }

                        messages.Add(mediumMessage);
                    }

                    return messages;
                },
                transaction: transaction,
                commandTimeout: messagingOptions.Value.CommandTimeout,
                sqlParams: sqlParams,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        logger.LogSchedulerBatchFetched(messageList.Count, _publishedTable);

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

    /// <summary>
    /// Returns the monitoring API for querying message statistics and dashboard data against this SQL Server storage.
    /// </summary>
    public IMonitoringApi GetMonitoringApi()
    {
        return new SqlServerMonitoringApi(options, messagingOptions, initializer, serializer, timeProvider);
    }

    // NOTE: ChangeReceiveStateAsync does not call this helper because the receive path additionally
    // writes ExceptionInfo, a column absent from the published table schema. Keep these two methods
    // in sync when adding columns.
    private async ValueTask<bool> _ReserveAttemptAsync(
        string tableName,
        MediumMessage message,
        int originalInlineAttempts,
        CancellationToken cancellationToken
    )
    {
        var sql =
            $"DECLARE @LeaseNow datetime2(7) = SYSUTCDATETIME(); UPDATE {tableName} SET InlineAttempts=@InlineAttempts WHERE Id=@Id AND {_TerminalRowGuardWithRetries} AND ((LockedUntil IS NULL AND @LockedUntil IS NULL) OR LockedUntil=@LockedUntil) AND ((Owner IS NULL AND @CurrentOwner IS NULL) OR Owner=@CurrentOwner) AND LockedUntil>@LeaseNow";
        object[] sqlParams =
        [
            new SqlParameter("@Id", message.StorageId),
            new SqlParameter("@InlineAttempts", message.InlineAttempts),
            new SqlParameter("@OriginalRetries", message.Retries),
            new SqlParameter("@OriginalInlineAttempts", originalInlineAttempts),
            new SqlParameter("@LockedUntil", SqlDbType.DateTime2) { Value = message.LockedUntil.ToUtcParameterValue() },
            new SqlParameter("@CurrentOwner", SqlDbType.NVarChar, options.Value.OwnerColumnMaxLength)
            {
                Value = message.Owner ?? (object)DBNull.Value,
            },
        ];
        await using var connection = new SqlConnection(options.Value.ConnectionString);
        var affected = await connection
            .ExecuteNonQueryAsync(
                sql,
                commandTimeout: messagingOptions.Value.CommandTimeout,
                sqlParams: sqlParams,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
        return affected > 0;
    }

    private async ValueTask<bool> _ChangeMessageStateAsync(
        string tableName,
        MediumMessage message,
        StatusName state,
        object? transaction,
        DateTime? nextRetryAt,
        DateTime? lockedUntil,
        int? originalRetries,
        int? originalInlineAttempts,
        CancellationToken cancellationToken
    )
    {
        var sql =
            $"DECLARE @LeaseNow datetime2(7) = SYSUTCDATETIME(); UPDATE {tableName} SET Content=@Content, Retries=@Retries,InlineAttempts=@InlineAttempts,ExpiresAt=@ExpiresAt,NextRetryAt=@NextRetryAt,LockedUntil=@LockedUntil,Owner=@Owner,StatusName=@StatusName WHERE Id=@Id AND {_TerminalRowGuardWithRetries} AND (@OriginalInlineAttempts IS NULL OR (((LockedUntil IS NULL AND @OriginalLockedUntil IS NULL) OR LockedUntil=@OriginalLockedUntil) AND ((Owner IS NULL AND @OriginalOwner IS NULL) OR Owner=@OriginalOwner) AND LockedUntil>@LeaseNow))";

        object[] sqlParams =
        [
            new SqlParameter("@Id", message.StorageId),
            new SqlParameter("@Content", serializer.Serialize(message.Origin)),
            new SqlParameter("@Retries", message.Retries),
            new SqlParameter("@InlineAttempts", message.InlineAttempts),
            new SqlParameter("@ExpiresAt", SqlDbType.DateTime2)
            {
                Value = message.ExpiresAt.HasValue ? message.ExpiresAt.Value : DBNull.Value,
            },
            new SqlParameter("@NextRetryAt", SqlDbType.DateTime2) { Value = nextRetryAt.ToUtcParameterValue() },
            new SqlParameter("@LockedUntil", SqlDbType.DateTime2) { Value = lockedUntil.ToUtcParameterValue() },
            _OwnerParameter("@Owner", lockedUntil),
            new SqlParameter("@OriginalRetries", SqlDbType.Int) { Value = originalRetries ?? (object)DBNull.Value },
            new SqlParameter("@OriginalInlineAttempts", SqlDbType.Int)
            {
                Value = originalInlineAttempts ?? (object)DBNull.Value,
            },
            new SqlParameter("@OriginalLockedUntil", SqlDbType.DateTime2)
            {
                Value = message.LockedUntil.ToUtcParameterValue(),
            },
            new SqlParameter("@OriginalOwner", SqlDbType.NVarChar, options.Value.OwnerColumnMaxLength)
            {
                Value = message.Owner ?? (object)DBNull.Value,
            },
            new SqlParameter("@StatusName", state.ToString("G")),
        ];

        int affectedRows;
        if (transaction is DbTransaction dbTransaction)
        {
            var connection =
                dbTransaction.Connection
                ?? throw new InvalidOperationException(
                    "The supplied DbTransaction has no active Connection — it may have already been committed or rolled back."
                );
            affectedRows = await connection
                .ExecuteNonQueryAsync(
                    sql,
                    transaction: dbTransaction,
                    commandTimeout: messagingOptions.Value.CommandTimeout,
                    sqlParams: sqlParams,
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);
        }
        else if (transaction is IDbContextTransaction efTransaction)
        {
            var dbTrans = efTransaction.GetDbTransaction();
            var connection =
                dbTrans.Connection
                ?? throw new InvalidOperationException(
                    "The supplied DbTransaction has no active Connection — it may have already been committed or rolled back."
                );
            affectedRows = await connection
                .ExecuteNonQueryAsync(
                    sql,
                    transaction: dbTrans,
                    commandTimeout: messagingOptions.Value.CommandTimeout,
                    sqlParams: sqlParams,
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);
        }
        else
        {
            await using var connection = new SqlConnection(options.Value.ConnectionString);
            affectedRows = await connection
                .ExecuteNonQueryAsync(
                    sql,
                    commandTimeout: messagingOptions.Value.CommandTimeout,
                    sqlParams: sqlParams,
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);
        }

        return affectedRows > 0;
    }

    private async ValueTask<Guid?> _StoreReceivedMessage(
        object[] sqlParams,
        CancellationToken cancellationToken = default
    )
    {
        // The WHEN MATCHED predicate skips terminal Succeeded/Failed rows that have no scheduled
        // retry. Without the guard, a broker-redelivered message whose payload again fails to
        // deserialize would overwrite a previously-Succeeded row's status to Failed, firing
        // OnExhausted for a message that actually succeeded.
        //
        // #9 — narrow the WHEN MATCHED predicate to additionally skip rows whose lease is still
        // active (LockedUntil in the future). A redelivered message that arrives while the row
        // is being dispatched would otherwise overwrite LockedUntil = NULL, releasing the active
        // pickup lease mid-attempt and causing the retry processor to re-pick the row while the
        // inline retry burst is still in flight. The UPDATE SET list deliberately excludes
        // [Retries] and [InlineAttempts] so a benign redelivery collapse never resets the durable
        // retry counters.
        //
        // #5 — OUTPUT inserted.[Id] returns the authoritative persisted row id (insert or update branch).
        // On the UPDATE branch the existing row keeps its original [Id], which differs from the freshly
        // generated @Id, so the caller adopts the returned value; a guard-blocked no-op returns no row.
        var sql = $"""
            DECLARE @LeaseNow datetime2(7) = SYSUTCDATETIME();

            MERGE {_receivedTable} WITH (HOLDLOCK) AS target
            USING (SELECT @Version AS Version, @MessageId AS MessageId, @Group AS [Group], @IntentType AS IntentType) AS source
            ON target.Version = source.Version AND target.MessageId = source.MessageId AND (target.[Group] = source.[Group] OR (target.[Group] IS NULL AND source.[Group] IS NULL)) AND target.IntentType = source.IntentType
            WHEN MATCHED
                AND NOT (target.StatusName IN ('{nameof(StatusName.Succeeded)}','{nameof(
                    StatusName.Failed
                )}') AND target.NextRetryAt IS NULL)
                AND (target.LockedUntil IS NULL OR target.LockedUntil <= @LeaseNow)
            THEN
                UPDATE SET StatusName = @StatusName, ExpiresAt = @ExpiresAt, NextRetryAt = @NextRetryAt, LockedUntil = @LockedUntil, Owner = @Owner, Content = @Content, ExceptionInfo = @ExceptionInfo
            WHEN NOT MATCHED THEN
                INSERT ([Id],[Version],[Name],[Group],[Content],[IntentType],[Retries],[InlineAttempts],[Added],[ExpiresAt],[NextRetryAt],[LockedUntil],[Owner],[StatusName],[MessageId],[ExceptionInfo])
                VALUES (@Id,@Version,@Name,@Group,@Content,@IntentType,@Retries,@InlineAttempts,@Added,@ExpiresAt,@NextRetryAt,@LockedUntil,@Owner,@StatusName,@MessageId,@ExceptionInfo)
            OUTPUT inserted.[Id];
            """;

        await using var connection = new SqlConnection(options.Value.ConnectionString);

        return await connection
            .ExecuteReaderAsync<Guid?>(
                sql,
                static async (reader, token) =>
                    await reader.ReadAsync(token).ConfigureAwait(false) ? reader.GetGuid(0) : null,
                commandTimeout: messagingOptions.Value.CommandTimeout,
                sqlParams: sqlParams,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
    }

    private async ValueTask<bool> _LeaseAndReserveAttemptAsync(
        string tableName,
        MediumMessage message,
        TimeSpan leaseDuration,
        int originalInlineAttempts,
        CancellationToken cancellationToken
    )
    {
        // Fresh-dispatch fast path: acquire the lease AND reserve the next inline attempt in one
        // statement (one round trip instead of the _LeaseMessageAsync + _ReserveAttemptAsync pair).
        // Combines the lease-contention predicate (#15) with the durable-counter CAS from
        // _ReserveAttemptAsync; no owner match is required because this path is TAKING the lease.
        // Ownership time is the DATABASE's: one SYSUTCDATETIME() snapshot supplies BOTH the expiry
        // comparison and the new deadline, so the lease a remote replica later evaluates was written by the
        // same clock it compares against. OUTPUT returns the durable deadline so the in-memory model matches
        // the row without a second read.
        var sql = $"""
            DECLARE @ClaimNow datetime2(7) = SYSUTCDATETIME();
            UPDATE {tableName}
            SET LockedUntil = DATEADD(nanosecond, @LeaseNanoseconds, DATEADD(second, @LeaseWholeSeconds, @ClaimNow)),
                Owner = @Owner,
                InlineAttempts = @InlineAttempts
            OUTPUT inserted.LockedUntil, inserted.Owner
            WHERE Id = @Id
              AND (LockedUntil IS NULL OR LockedUntil <= @ClaimNow)
              AND {_TerminalRowGuardWithRetries};
            """;

        var owner = nodeMembership.GetOwnerTag();
        var (leaseWholeSeconds, leaseNanoseconds) = _SplitLeaseDuration(leaseDuration);
        object[] sqlParams =
        [
            new SqlParameter("@Id", message.StorageId),
            new SqlParameter("@LeaseWholeSeconds", SqlDbType.Int) { Value = leaseWholeSeconds },
            new SqlParameter("@LeaseNanoseconds", SqlDbType.Int) { Value = leaseNanoseconds },
            new SqlParameter("@Owner", SqlDbType.NVarChar, options.Value.OwnerColumnMaxLength)
            {
                Value = owner ?? (object)DBNull.Value,
            },
            new SqlParameter("@InlineAttempts", message.InlineAttempts),
            new SqlParameter("@OriginalRetries", message.Retries),
            new SqlParameter("@OriginalInlineAttempts", originalInlineAttempts),
        ];

        await using var connection = new SqlConnection(options.Value.ConnectionString);
        var persistedLease = await connection
            .ExecuteReaderAsync(
                sql,
                LeaseDeadlineReader.ReadAsync,
                commandTimeout: messagingOptions.Value.CommandTimeout,
                sqlParams: sqlParams,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        if (persistedLease is not { } lease)
        {
            return false;
        }

        // Mirror the DURABLE deadline the server issued, not a locally recomputed one.
        message.LockedUntil = lease.LockedUntil;
        message.Owner = lease.Owner;

        return true;
    }

    private async ValueTask<bool> _LeaseMessageAsync(
        string tableName,
        MediumMessage message,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default
    )
    {
        // #15 — explicit lease-contention predicate: only acquire the lease when the row is unleased
        // OR its existing lease has expired. Without this, two replicas racing on a fresh-from-broker
        // dispatch could both UPDATE LockedUntil and both believe they hold the lease (the SqlServer
        // and PostgreSql atomic-claim pickup paths already filter on LockedUntil, but the lease call
        // from the consume/publish path itself was unconditional). Returning false here surfaces the
        // contention to the inline retry loop, which skips dispatch.
        // Ownership time is the DATABASE's — see _LeaseAndReserveAttemptAsync for why.
        var sql = $"""
            DECLARE @ClaimNow datetime2(7) = SYSUTCDATETIME();
            UPDATE {tableName}
            SET LockedUntil = DATEADD(nanosecond, @LeaseNanoseconds, DATEADD(second, @LeaseWholeSeconds, @ClaimNow)),
                Owner = @Owner
            OUTPUT inserted.LockedUntil, inserted.Owner
            WHERE Id = @Id
              AND (LockedUntil IS NULL OR LockedUntil <= @ClaimNow)
              AND {_TerminalRowGuardSimple};
            """;

        var owner = nodeMembership.GetOwnerTag();
        var (leaseWholeSeconds, leaseNanoseconds) = _SplitLeaseDuration(leaseDuration);
        object[] sqlParams =
        [
            new SqlParameter("@Id", message.StorageId),
            new SqlParameter("@LeaseWholeSeconds", SqlDbType.Int) { Value = leaseWholeSeconds },
            new SqlParameter("@LeaseNanoseconds", SqlDbType.Int) { Value = leaseNanoseconds },
            new SqlParameter("@Owner", SqlDbType.NVarChar, options.Value.OwnerColumnMaxLength)
            {
                Value = owner ?? (object)DBNull.Value,
            },
        ];

        await using var connection = new SqlConnection(options.Value.ConnectionString);
        var persistedLease = await connection
            .ExecuteReaderAsync(
                sql,
                LeaseDeadlineReader.ReadAsync,
                commandTimeout: messagingOptions.Value.CommandTimeout,
                sqlParams: sqlParams,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        if (persistedLease is not { } lease)
        {
            return false;
        }

        // Mirror the DURABLE deadline the server issued, not a locally recomputed one.
        message.LockedUntil = lease.LockedUntil;
        message.Owner = lease.Owner;

        return true;
    }

    /// <summary>
    /// Splits a lease duration into the whole-seconds and nanoseconds pair required by <c>DATEADD</c>.
    /// A seconds-only call would lose sub-second precision, while a nanoseconds-only call overflows
    /// its integer argument for durations longer than roughly two seconds.
    /// </summary>
    private static (int WholeSeconds, int Nanoseconds) _SplitLeaseDuration(TimeSpan leaseDuration)
    {
        var wholeSeconds = checked((int)(leaseDuration.Ticks / TimeSpan.TicksPerSecond));
        var nanoseconds = checked((int)(leaseDuration.Ticks % TimeSpan.TicksPerSecond * 100));

        return (wholeSeconds, nanoseconds);
    }

    private async ValueTask<IEnumerable<MediumMessage>> _GetMessagesOfNeedRetryAsync(
        string tableName,
        CancellationToken cancellationToken = default
    )
    {
        // Atomic claim-and-return: UPDATE TOP (N) ... OUTPUT inserted.* leases the rows (sets
        // LockedUntil) and returns the selected columns in one round-trip. Replaces the previous
        // two-step SELECT-UPDLOCK-then-Lease pattern, which committed the SELECT transaction
        // before _LeaseAsync wrote LockedUntil. In between, a concurrent replica could pass the
        // same "LockedUntil IS NULL" filter and lease the same row — double-dispatch.
        //
        // WITH (UPDLOCK, READPAST, ROWLOCK) on the UPDATE preserves the "skip rows another
        // replica is mid-claim on" behaviour. The UPDATE assigns database time + DispatchTimeout
        // so subsequent pickup polls (anywhere) see the row as leased until the dispatch attempt
        // completes (or the lease expires).
        //
        // NextRetryAt is scheduling state written from the injected TimeProvider, so its due
        // predicate uses that same authority. Lease expiry and stamping remain on one command-
        // local database snapshot, keeping every replica on one ownership authority without a
        // clock query.
        var sql = $"""
            DECLARE @ClaimNow datetime2(7) = SYSUTCDATETIME();

            WITH Candidates AS (
                SELECT TOP (@BatchSize) Id
                FROM {tableName} WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE Retries <= @Retries
                  AND Version = @Version
                  AND NextRetryAt IS NOT NULL AND NextRetryAt <= @Now
                  AND (LockedUntil IS NULL OR LockedUntil <= @ClaimNow)
                  AND {_TerminalRowGuardSimple}
                ORDER BY NextRetryAt, Id
            )
            UPDATE target
            SET LockedUntil = DATEADD(nanosecond, @LeaseNanoseconds, DATEADD(second, @LeaseWholeSeconds, @ClaimNow)), Owner = @Owner
            OUTPUT inserted.Id, inserted.Content, inserted.IntentType, inserted.Retries, inserted.InlineAttempts, inserted.Added, inserted.NextRetryAt, inserted.LockedUntil, inserted.Owner
            FROM {tableName} AS target
            INNER JOIN Candidates ON target.Id = Candidates.Id;
            """;

        var (leaseWholeSeconds, leaseNanoseconds) = _SplitLeaseDuration(
            messagingOptions.Value.RetryPolicy.DispatchTimeout
        );

        object[] sqlParams =
        [
            new SqlParameter("@BatchSize", messagingOptions.Value.RetryBatchSize),
            new SqlParameter("@Retries", messagingOptions.Value.RetryPolicy.MaxPersistedRetries),
            new SqlParameter("@Version", messagingOptions.Value.Version),
            new SqlParameter("@Now", SqlDbType.DateTime2) { Value = timeProvider.GetUtcNow().UtcDateTime },
            new SqlParameter("@LeaseWholeSeconds", SqlDbType.Int) { Value = leaseWholeSeconds },
            new SqlParameter("@LeaseNanoseconds", SqlDbType.Int) { Value = leaseNanoseconds },
            _OwnerParameter("@Owner", hasLease: true),
        ];

        await using var connection = new SqlConnection(options.Value.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var poisonMessages = new List<PoisonMessage>();
        var result = await connection
            .ExecuteReaderAsync(
                sql,
                async (reader, ct) =>
                {
                    var messages = new List<MediumMessage>();
                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        var storageId = reader.GetGuid(0);
                        var content = reader.GetString(1);

                        MediumMessage mediumMessage;
                        try
                        {
                            mediumMessage = new MediumMessage
                            {
                                StorageId = storageId,
                                Origin = serializer.Deserialize(content)!,
                                Content = content,
                                IntentType = (IntentType)reader.GetInt16(2),
                                Retries = reader.GetInt32(3),
                                InlineAttempts = reader.GetInt32(4),
                                Added = reader.GetDateTime(5),
                                NextRetryAt = await reader.IsDBNullAsync(6, ct).ConfigureAwait(false)
                                    ? null
                                    : reader.GetDateTime(6),
                                LockedUntil = await reader.IsDBNullAsync(7, ct).ConfigureAwait(false)
                                    ? null
                                    : reader.GetDateTime(7),
                                Owner = await reader.IsDBNullAsync(8, ct).ConfigureAwait(false)
                                    ? null
                                    : reader.GetString(8),
                            };
                        }
#pragma warning disable CA1031 // deliberately broad: one un-deserializable row must not abort/starve the batch (#3)
                        catch (Exception ex)
#pragma warning restore CA1031
                        {
                            logger.LogPoisonMessageSkipped(storageId, tableName, ex);
                            poisonMessages.Add(_CreatePoisonMessage(storageId, ex));
                            continue;
                        }

                        messages.Add(mediumMessage);
                    }

                    return messages;
                },
                transaction: transaction,
                commandTimeout: messagingOptions.Value.CommandTimeout,
                sqlParams: sqlParams,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        await _MarkPoisonMessagesTerminalAsync(connection, transaction, tableName, poisonMessages, cancellationToken)
            .ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return result;
    }

    private async ValueTask _MarkPoisonMessagesTerminalAsync(
        DbConnection connection,
        DbTransaction? transaction,
        string tableName,
        IReadOnlyList<PoisonMessage> poisonMessages,
        CancellationToken cancellationToken
    )
    {
        if (poisonMessages.Count == 0)
        {
            return;
        }

        var expiresAt = timeProvider
            .GetUtcNow()
            .UtcDateTime.AddSeconds(messagingOptions.Value.FailedMessageExpiredAfter);
        var isReceivedTable = string.Equals(tableName, _receivedTable, StringComparison.Ordinal);
        var sql = isReceivedTable
            ? $"""
                UPDATE target
                SET StatusName=@StatusName, NextRetryAt=NULL, LockedUntil=NULL, Owner=NULL,
                    ExpiresAt=@ExpiresAt, ExceptionInfo=poison.ExceptionInfo
                FROM {tableName} AS target
                INNER JOIN @PoisonMessages AS poison ON target.Id=poison.Id
                WHERE {_TerminalRowGuardSimple};
                """
            : $"""
                UPDATE {tableName}
                SET StatusName=@StatusName, NextRetryAt=NULL, LockedUntil=NULL, Owner=NULL, ExpiresAt=@ExpiresAt
                WHERE Id IN (SELECT Id FROM @Ids) AND {_TerminalRowGuardSimple};
                """;
        object[] sqlParams =
        [
            new SqlParameter("@StatusName", nameof(StatusName.Failed)),
            new SqlParameter("@ExpiresAt", SqlDbType.DateTime2) { Value = expiresAt },
            isReceivedTable
                ? _BuildPoisonMessageListTvpParameter(poisonMessages)
                : _BuildIdListTvpParameter(poisonMessages.Select(message => message.StorageId).ToArray()),
        ];

        // One savepoint isolates the batched terminal mark from the shared claim transaction. If it fails,
        // poison marking is skipped but healthy-row claims still commit; failed poison rows remain leased.
        const string savepointName = "headless_poison_mark_batch";
        if (transaction is not null)
        {
            await connection
                .ExecuteNonQueryAsync(
                    $"SAVE TRANSACTION {savepointName};",
                    transaction: transaction,
                    commandTimeout: messagingOptions.Value.CommandTimeout,
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);
        }

        try
        {
            await connection
                .ExecuteNonQueryAsync(
                    sql,
                    transaction: transaction,
                    commandTimeout: messagingOptions.Value.CommandTimeout,
                    sqlParams: sqlParams,
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            foreach (var poisonMessage in poisonMessages)
            {
                logger.LogPoisonMessageTerminalMarkFailed(poisonMessage.StorageId, tableName, ex);
            }

            if (transaction is not null)
            {
                await connection
                    .ExecuteNonQueryAsync(
                        $"ROLLBACK TRANSACTION {savepointName};",
                        transaction: transaction,
                        commandTimeout: messagingOptions.Value.CommandTimeout,
                        cancellationToken: cancellationToken
                    )
                    .ConfigureAwait(false);
            }
        }
    }

    private SqlParameter _BuildPoisonMessageListTvpParameter(IReadOnlyList<PoisonMessage> poisonMessages)
    {
        var messagesTable = new DataTable();
        messagesTable.Columns.Add("Id", typeof(Guid));
        messagesTable.Columns.Add("ExceptionInfo", typeof(string));
        foreach (var poisonMessage in poisonMessages)
        {
            messagesTable.Rows.Add(poisonMessage.StorageId, poisonMessage.ExceptionInfo);
        }

        return new SqlParameter("@PoisonMessages", SqlDbType.Structured)
        {
            TypeName = $"[{options.Value.Schema}].[HeadlessMessagingPoisonMessageList]",
            Value = messagesTable,
        };
    }

    private async ValueTask<int> _ReclaimDeadOwnersAsync(
        string tableName,
        IReadOnlyCollection<string> deadOwners,
        CancellationToken cancellationToken
    )
    {
        // Empty deadOwners trivially matches zero rows (`Owner IN ()` is never satisfied), so the early
        // return is an optimization that skips the round-trip, not a safety guard.
        if (deadOwners.Count == 0)
        {
            return 0;
        }

        var sql = $"""
            DECLARE @ReclaimNow datetime2(7) = SYSUTCDATETIME();

            UPDATE target
            SET LockedUntil = @ReclaimNow
            FROM {tableName} AS target
            WHERE target.Owner IS NOT NULL
              AND target.Owner IN (SELECT [Owner] FROM @DeadOwners)
              AND target.LockedUntil > @ReclaimNow
              AND {_TerminalRowGuardSimple};
            """;

        // A TVP keeps the SQL text and parameter shape constant regardless of owner count, so SQL Server reuses
        // a single cached plan even when a mass-node-loss reconcile batches many dead owners into one UPDATE.
        var sqlParams = new object[] { _BuildOwnerListTvpParameter(deadOwners) };

        await using var connection = new SqlConnection(options.Value.ConnectionString);
        return await connection
            .ExecuteNonQueryAsync(
                sql,
                commandTimeout: messagingOptions.Value.CommandTimeout,
                sqlParams: sqlParams,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the <c>@DeadOwners</c> table-valued parameter backed by the <c>HeadlessMessagingOwnerList</c> type
    /// (provisioned by the storage initializer). The TVP keeps the reclaim plan stable across owner counts.
    /// </summary>
    private SqlParameter _BuildOwnerListTvpParameter(IReadOnlyCollection<string> deadOwners)
    {
        var ownersTable = new DataTable();
        ownersTable.Columns.Add("Owner", typeof(string));

        // Defensive de-dup: ReclaimDead* is a public IDataStorage contract method, so a direct caller may pass
        // duplicate owner tags (the bridge already de-dups its reclaimed set). The TVP's Owner column is the PK,
        // so duplicates would otherwise violate it.
        foreach (var owner in deadOwners.Distinct(StringComparer.Ordinal))
        {
            ownersTable.Rows.Add(owner);
        }

        return new SqlParameter("@DeadOwners", SqlDbType.Structured)
        {
            TypeName = $"[{options.Value.Schema}].[HeadlessMessagingOwnerList]",
            Value = ownersTable,
        };
    }

    private SqlParameter _OwnerParameter(string name, DateTime? lockedUntil) =>
        _OwnerParameter(name, lockedUntil is not null);

    private SqlParameter _OwnerParameter(string name, bool hasLease) =>
        new(name, SqlDbType.NVarChar, options.Value.OwnerColumnMaxLength)
        {
            Value = hasLease ? nodeMembership.GetOwnerTag() ?? (object)DBNull.Value : DBNull.Value,
        };

    private static PoisonMessage _CreatePoisonMessage(Guid storageId, Exception exception) =>
        new(storageId, $"{exception.GetType().FullName}: {exception.Message}");

    private readonly record struct PoisonMessage(Guid StorageId, string ExceptionInfo);
}
