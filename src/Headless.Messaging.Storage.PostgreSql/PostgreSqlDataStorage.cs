// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Abstractions;
using Headless.Coordination;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

#pragma warning disable RCS1084 // Use coalesce expression instead of conditional expression
namespace Headless.Messaging.Storage.PostgreSql;

/// <summary>
/// PostgreSQL implementation of <see cref="IDataStorage"/> for outbox pattern message persistence.
/// Handles storage, retrieval, and state management of published and received messages.
/// </summary>
internal sealed partial class PostgreSqlDataStorage(
    IOptions<PostgreSqlOptions> postgreSqlOptions,
    IOptions<MessagingOptions> messagingOptions,
    IStorageInitializer initializer,
    ISerializer serializer,
    // PostgreSQL stores message ids as native uuid (big-endian byte sort) -> Version7 keeps the PK sequential.
    [FromKeyedServices(SequentialGuidType.Version7)] IGuidGenerator guidGenerator,
    TimeProvider timeProvider,
    INodeMembership nodeMembership,
    ILogger<PostgreSqlDataStorage> logger
) : IDataStorage, IDelayedMessageClaimStorage
{
    /// <summary>
    /// Reusable WHERE-clause fragment that refuses updates to rows already in a terminal state
    /// (<c>Succeeded</c> / <c>Failed</c>) with no scheduled retry, while still respecting an
    /// optional optimistic-concurrency token (<c>@OriginalRetries</c>). Used by Change*StateAsync
    /// paths that pass <c>@OriginalRetries</c>.
    /// </summary>
    private const string _TerminalRowGuardWithRetries =
        "NOT (\"StatusName\" IN ('Succeeded','Failed') AND \"NextRetryAt\" IS NULL) AND (@OriginalRetries IS NULL OR \"Retries\"=@OriginalRetries) AND (@OriginalInlineAttempts IS NULL OR \"InlineAttempts\"=@OriginalInlineAttempts)";

    /// <summary>
    /// Reusable WHERE-clause fragment for paths that do not supply <c>@OriginalRetries</c>
    /// (lease / store-received MERGE/INSERT-UPDATE). Same terminal-row semantics as
    /// <see cref="_TerminalRowGuardWithRetries"/> without the concurrency-token clause.
    /// </summary>
    private const string _TerminalRowGuardSimple =
        "NOT (\"StatusName\" IN ('Succeeded','Failed') AND \"NextRetryAt\" IS NULL)";

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
    /// Returns the monitoring API for querying message statistics and dashboard data against this PostgreSQL storage.
    /// </summary>
    public IMonitoringApi GetMonitoringApi()
    {
        return new PostgreSqlMonitoringApi(postgreSqlOptions, messagingOptions, initializer, serializer, timeProvider);
    }

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
            $"UPDATE {_publishedTable} SET \"StatusName\"=@StatusName, \"LockedUntil\"=NULL, \"Owner\"=NULL WHERE \"Id\" = ANY(@Ids) AND {_TerminalRowGuardSimple};";

        object[] sqlParams =
        [
            new NpgsqlParameter("@Ids", storageIds),
            new NpgsqlParameter("@StatusName", nameof(StatusName.Delayed)),
        ];

        await using var connection = postgreSqlOptions.Value.CreateConnection();

        await connection
            .ExecuteNonQueryAsync(
                sql,
                commandTimeout: messagingOptions.Value.CommandTimeout,
                sqlParams: sqlParams,
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
        DbTransaction? transaction = null,
        DateTimeOffset? nextRetryAt = null,
        DateTimeOffset? lockedUntil = null,
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
        DateTimeOffset? nextRetryAt,
        DateTimeOffset? lockedUntil,
        int originalRetries,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
    )
    {
        return _ChangeMessageStateAsync(
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
    }

    public ValueTask<bool> ReservePublishAttemptAsync(
        MediumMessage message,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
    )
    {
        return _ReserveAttemptAsync(_publishedTable, message, originalInlineAttempts, cancellationToken);
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
        return _LeaseMessageAsync(_publishedTable, message, leaseDuration, cancellationToken);
    }

    public ValueTask<bool> LeasePublishAndReserveAttemptAsync(
        MediumMessage message,
        TimeSpan leaseDuration,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
    )
    {
        return _LeaseAndReserveAttemptAsync(
            _publishedTable,
            message,
            leaseDuration,
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
        DateTimeOffset? nextRetryAt = null,
        DateTimeOffset? lockedUntil = null,
        int? originalRetries = null,
        CancellationToken cancellationToken = default
    )
    {
        return _ChangeReceiveStateAsync(
            message,
            state,
            nextRetryAt,
            lockedUntil,
            originalRetries,
            originalInlineAttempts: null,
            cancellationToken
        );
    }

    public ValueTask<bool> ChangeReceiveRetryStateAsync(
        MediumMessage message,
        StatusName state,
        DateTimeOffset? nextRetryAt,
        DateTimeOffset? lockedUntil,
        int originalRetries,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
    )
    {
        return _ChangeReceiveStateAsync(
            message,
            state,
            nextRetryAt,
            lockedUntil,
            originalRetries,
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
        return _ReserveAttemptAsync(_receivedTable, message, originalInlineAttempts, cancellationToken);
    }

    private async ValueTask<bool> _ChangeReceiveStateAsync(
        MediumMessage message,
        StatusName state,
        DateTimeOffset? nextRetryAt,
        DateTimeOffset? lockedUntil,
        int? originalRetries,
        int? originalInlineAttempts,
        CancellationToken cancellationToken
    )
    {
        // NOTE: ChangeReceiveStateAsync does not call _ChangeMessageStateAsync because the receive
        // path additionally writes ExceptionInfo, a column absent from the published table schema.
        // Keep these two methods in sync when adding columns.
        //
        // X1 terminal-row guard: the WHERE clause refuses updates to rows that are already
        // terminal (Succeeded or Failed) AND have NextRetryAt cleared — the marker for a
        // permanently-completed row. The narrower form (instead of `StatusName NOT IN
        // ('Succeeded','Failed')` as the X1 plan suggested) is deliberate: a Failed row with a
        // non-null NextRetryAt is "persisted for retry" and MUST remain mutable so the retry
        // processor can rewrite it on the next pickup. The plan's stricter form would block
        // that, breaking the persisted-retry flow.
        var sql =
            $"UPDATE {_receivedTable} SET \"Content\"=@Content,\"Retries\"=@Retries,\"InlineAttempts\"=@InlineAttempts,\"ExpiresAt\"=@ExpiresAt,\"NextRetryAt\"=@NextRetryAt,\"LockedUntil\"=@LockedUntil,\"Owner\"=@Owner,\"StatusName\"=@StatusName,\"ExceptionInfo\"=@ExceptionInfo WHERE \"Id\"=@Id AND {_TerminalRowGuardWithRetries} AND (@OriginalInlineAttempts IS NULL OR (\"LockedUntil\" IS NOT DISTINCT FROM @OriginalLockedUntil AND \"Owner\" IS NOT DISTINCT FROM @OriginalOwner AND \"LockedUntil\">statement_timestamp()))";

        object[] sqlParams =
        [
            new NpgsqlParameter("@Id", message.StorageId),
            new NpgsqlParameter("@Content", serializer.Serialize(message.Origin)),
            new NpgsqlParameter("@Retries", message.Retries),
            new NpgsqlParameter("@InlineAttempts", message.InlineAttempts),
            new NpgsqlParameter("@ExpiresAt", message.ExpiresAt.ToUtcParameterValue()),
            new NpgsqlParameter("@NextRetryAt", nextRetryAt.ToUtcParameterValue()),
            new NpgsqlParameter("@LockedUntil", lockedUntil.ToUtcParameterValue()),
            new NpgsqlParameter("@Owner", NpgsqlDbType.Varchar)
            {
                Value = nodeMembership.GetOwnerParameterValue(lockedUntil),
            },
            new NpgsqlParameter("@OriginalRetries", NpgsqlDbType.Integer)
            {
                Value = originalRetries ?? (object)DBNull.Value,
            },
            new NpgsqlParameter("@OriginalInlineAttempts", NpgsqlDbType.Integer)
            {
                Value = originalInlineAttempts ?? (object)DBNull.Value,
            },
            new NpgsqlParameter("@OriginalLockedUntil", message.LockedUntil.ToUtcParameterValue()),
            new NpgsqlParameter("@OriginalOwner", NpgsqlDbType.Varchar)
            {
                Value = message.Owner ?? (object)DBNull.Value,
            },
            new NpgsqlParameter("@StatusName", state.ToString("G")),
            new NpgsqlParameter("@ExceptionInfo", message.ExceptionInfo ?? (object)DBNull.Value),
        ];

        await using var connection = postgreSqlOptions.Value.CreateConnection();
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
    )
    {
        return _LeaseMessageAsync(_receivedTable, message, leaseDuration, cancellationToken);
    }

    public ValueTask<bool> LeaseReceiveAndReserveAttemptAsync(
        MediumMessage message,
        TimeSpan leaseDuration,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
    )
    {
        return _LeaseAndReserveAttemptAsync(
            _receivedTable,
            message,
            leaseDuration,
            originalInlineAttempts,
            cancellationToken
        );
    }

    /// <summary>
    /// Persists a published outbox message to the <c>published</c> table. When <paramref name="transaction"/>
    /// is supplied the INSERT participates in the caller's database transaction (transactional outbox).
    /// </summary>
    /// <returns>The stored <c>MediumMessage</c> with its generated <c>StorageId</c> and timestamps populated.</returns>
    public async ValueTask<MediumMessage> StoreMessageAsync(
        string name,
        MediumMessage message,
        DbTransaction? transaction = null,
        CancellationToken cancellationToken = default
    )
    {
        var sql =
            $"INSERT INTO {_publishedTable} (\"Id\",\"Version\",\"Name\",\"Content\",\"IntentType\",\"Retries\",\"InlineAttempts\",\"Added\",\"ExpiresAt\",\"NextRetryAt\",\"LockedUntil\",\"Owner\",\"StatusName\",\"MessageId\")"
            + $"VALUES(@Id,'{messagingOptions.Value.Version}',@Name,@Content,@IntentType,@Retries,@InlineAttempts,@Added,@ExpiresAt,@NextRetryAt,@LockedUntil,@Owner,@StatusName,@MessageId);";

        var added = timeProvider.GetUtcNow();
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
            new NpgsqlParameter("@Id", stored.StorageId),
            new NpgsqlParameter("@Name", name),
            new NpgsqlParameter("@Content", stored.Content),
            new NpgsqlParameter("@IntentType", (short)stored.IntentType),
            new NpgsqlParameter("@Retries", stored.Retries),
            new NpgsqlParameter("@InlineAttempts", stored.InlineAttempts),
            new NpgsqlParameter("@Added", stored.Added),
            new NpgsqlParameter("@ExpiresAt", stored.ExpiresAt.ToUtcParameterValue()),
            new NpgsqlParameter("@NextRetryAt", stored.NextRetryAt.ToUtcParameterValue()),
            new NpgsqlParameter("@LockedUntil", stored.LockedUntil.ToUtcParameterValue()),
            new NpgsqlParameter("@Owner", NpgsqlDbType.Varchar) { Value = DBNull.Value },
            new NpgsqlParameter("@StatusName", nameof(StatusName.Scheduled)),
            new NpgsqlParameter("@MessageId", message.Origin.Id),
        ];

        if (transaction == null)
        {
            await using var connection = postgreSqlOptions.Value.CreateConnection();
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
            var connection =
                transaction.Connection
                ?? throw new InvalidOperationException(
                    "The supplied DbTransaction has no active Connection — it may have already been committed or rolled back."
                );

            await connection
                .ExecuteNonQueryAsync(
                    sql,
                    transaction,
                    commandTimeout: messagingOptions.Value.CommandTimeout,
                    sqlParams: sqlParams,
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);
        }

        return stored;
    }

    /// <summary>
    /// Persists a published outbox message built from a raw <c>Message</c> payload to the <c>published</c> table.
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
                IntentType = IntentType.Bus,
            },
            transaction,
            cancellationToken
        );
    }

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
            new NpgsqlParameter("@Id", guidGenerator.Create()),
            new NpgsqlParameter("@Name", name),
            new NpgsqlParameter("@Group", NpgsqlDbType.Varchar) { Value = (object?)group ?? DBNull.Value },
            new NpgsqlParameter(
                "@Content",
                string.IsNullOrEmpty(message.Content) ? serializer.Serialize(message.Origin) : message.Content
            ),
            new NpgsqlParameter("@IntentType", (short)message.IntentType),
            new NpgsqlParameter("@Retries", messagingOptions.Value.RetryPolicy.MaxPersistedRetries),
            new NpgsqlParameter("@InlineAttempts", message.InlineAttempts),
            new NpgsqlParameter("@Added", timeProvider.GetUtcNow()),
            new NpgsqlParameter(
                "@ExpiresAt",
                timeProvider.GetUtcNow().AddSeconds(messagingOptions.Value.FailedMessageExpiredAfter)
            ),
            new NpgsqlParameter("@NextRetryAt", DBNull.Value),
            new NpgsqlParameter("@LockedUntil", DBNull.Value),
            new NpgsqlParameter("@Owner", NpgsqlDbType.Varchar) { Value = DBNull.Value },
            new NpgsqlParameter("@StatusName", nameof(StatusName.Failed)),
            new NpgsqlParameter("@MessageId", message.Origin.Id),
            new NpgsqlParameter("@ExceptionInfo", exceptionInfo ?? (object)DBNull.Value),
        ];

        var rowId = await _StoreReceivedMessage(sqlParams, cancellationToken).ConfigureAwait(false);
        return rowId is not null;
    }

    /// <summary>
    /// Persists an inbound message to the <c>received</c> table using an atomic upsert. Concurrent
    /// broker redeliveries of the same message are collapsed to a single row via the unique index on
    /// <c>(Version, MessageId, COALESCE(Group, ''), IntentType)</c>; the terminal-row guard ensures
    /// already-succeeded rows are never overwritten.
    /// </summary>
    /// <returns>The stored <c>MediumMessage</c> with its generated <c>StorageId</c> and timestamps populated.</returns>
    public async ValueTask<MediumMessage> StoreReceivedMessageAsync(
        string name,
        string group,
        MediumMessage message,
        CancellationToken cancellationToken = default
    )
    {
        var added = timeProvider.GetUtcNow();
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
            new NpgsqlParameter("@Id", mediumMessage.StorageId),
            new NpgsqlParameter("@Name", name),
            new NpgsqlParameter("@Group", NpgsqlDbType.Varchar) { Value = (object?)group ?? DBNull.Value },
            new NpgsqlParameter("@Content", mediumMessage.Content),
            new NpgsqlParameter("@IntentType", (short)mediumMessage.IntentType),
            new NpgsqlParameter("@Retries", mediumMessage.Retries),
            new NpgsqlParameter("@InlineAttempts", mediumMessage.InlineAttempts),
            new NpgsqlParameter("@Added", mediumMessage.Added),
            new NpgsqlParameter("@ExpiresAt", mediumMessage.ExpiresAt.ToUtcParameterValue()),
            new NpgsqlParameter("@NextRetryAt", mediumMessage.NextRetryAt.ToUtcParameterValue()),
            new NpgsqlParameter("@LockedUntil", mediumMessage.LockedUntil.ToUtcParameterValue()),
            new NpgsqlParameter("@Owner", NpgsqlDbType.Varchar) { Value = DBNull.Value },
            new NpgsqlParameter("@StatusName", nameof(StatusName.Scheduled)),
            new NpgsqlParameter("@MessageId", message.Origin.Id),
            new NpgsqlParameter("@ExceptionInfo", DBNull.Value),
        ];

        // #5 — adopt the authoritative persisted row id. On a concurrent redelivery that takes the ON
        // CONFLICT UPDATE branch the row keeps its original "Id", so the freshly-generated StorageId would
        // be stale and the caller's later ChangeReceiveStateAsync (WHERE "Id"=@Id) would silently no-op.
        var rowId = await _StoreReceivedMessage(sqlParams, cancellationToken).ConfigureAwait(false);
        if (rowId is { } id)
        {
            mediumMessage.StorageId = id;
        }

        return mediumMessage;
    }

    /// <summary>
    /// Persists an inbound message built from a raw <c>Message</c> payload to the <c>received</c> table.
    /// Convenience overload that wraps <paramref name="message"/> in a <c>MediumMessage</c> before storing.
    /// </summary>
    /// <returns>The stored <c>MediumMessage</c> with its generated <c>StorageId</c> and timestamps populated.</returns>
    public ValueTask<MediumMessage> StoreReceivedMessageAsync(
        string name,
        string group,
        Message message,
        CancellationToken cancellationToken = default
    )
    {
        return StoreReceivedMessageAsync(
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
        await using var connection = postgreSqlOptions.Value.CreateConnection();

        object[] sqlParams = [new NpgsqlParameter("@timeout", timeout), new NpgsqlParameter("@batchCount", batchCount)];

        return await connection
            .ExecuteNonQueryAsync(
                $"""
                DELETE FROM {table}
                WHERE "Id" IN (
                    SELECT "Id"
                    FROM {table}
                    WHERE "ExpiresAt" < @timeout
                    AND "StatusName" IN ('{nameof(StatusName.Succeeded)}','{nameof(StatusName.Failed)}')
                    AND "NextRetryAt" IS NULL
                    LIMIT @batchCount
                )
                """,
                transaction: null,
                commandTimeout: messagingOptions.Value.CommandTimeout,
                sqlParams: sqlParams,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Fetches published messages eligible for retry dispatch. Uses an atomic UPDATE…RETURNING
    /// to lease and return rows in a single round-trip, preventing double-dispatch across replicas.
    /// </summary>
    public async ValueTask<IEnumerable<MediumMessage>> GetPublishedMessagesOfNeedRetryAsync(
        CancellationToken cancellationToken = default
    )
    {
        return await _GetMessagesOfNeedRetryAsync(_publishedTable, cancellationToken).ConfigureAwait(false);
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
    /// Fetches received messages eligible for retry dispatch. Uses an atomic UPDATE…RETURNING
    /// to lease and return rows in a single round-trip, preventing double-dispatch across replicas.
    /// </summary>
    public async ValueTask<IEnumerable<MediumMessage>> GetReceivedMessagesOfNeedRetryAsync(
        CancellationToken cancellationToken = default
    )
    {
        return await _GetMessagesOfNeedRetryAsync(_receivedTable, cancellationToken).ConfigureAwait(false);
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
        var sql = $"""DELETE FROM {_receivedTable} WHERE "Id"=@Id""";

        await using var connection = postgreSqlOptions.Value.CreateConnection();
        var result = await connection
            .ExecuteNonQueryAsync(
                sql,
                commandTimeout: messagingOptions.Value.CommandTimeout,
                sqlParams: [new NpgsqlParameter("@Id", id)],
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
        return result;
    }

    /// <summary>Deletes a single published message by its storage identifier.</summary>
    /// <returns>1 if the row was deleted; 0 if not found.</returns>
    public async ValueTask<int> DeletePublishedMessageAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var sql = $"""DELETE FROM {_publishedTable} WHERE "Id"=@Id""";

        await using var connection = postgreSqlOptions.Value.CreateConnection();
        var result = await connection
            .ExecuteNonQueryAsync(
                sql,
                commandTimeout: messagingOptions.Value.CommandTimeout,
                sqlParams: [new NpgsqlParameter("@Id", id)],
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
        return result;
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

        var sql = $"""DELETE FROM {_receivedTable} WHERE "Id" = ANY(@Ids)""";

        await using var connection = postgreSqlOptions.Value.CreateConnection();
        return await connection
            .ExecuteNonQueryAsync(
                sql,
                commandTimeout: messagingOptions.Value.CommandTimeout,
                sqlParams: [new NpgsqlParameter("@Ids", ids.ToArray())],
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

        var sql = $"""DELETE FROM {_publishedTable} WHERE "Id" = ANY(@Ids)""";

        await using var connection = postgreSqlOptions.Value.CreateConnection();
        return await connection
            .ExecuteNonQueryAsync(
                sql,
                commandTimeout: messagingOptions.Value.CommandTimeout,
                sqlParams: [new NpgsqlParameter("@Ids", ids.ToArray())],
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
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
            $"UPDATE {tableName} SET \"InlineAttempts\"=@InlineAttempts WHERE \"Id\"=@Id AND {_TerminalRowGuardWithRetries} AND \"LockedUntil\" IS NOT DISTINCT FROM @LockedUntil AND \"Owner\" IS NOT DISTINCT FROM @CurrentOwner AND \"LockedUntil\">statement_timestamp()";
        object[] sqlParams =
        [
            new NpgsqlParameter("@Id", message.StorageId),
            new NpgsqlParameter("@InlineAttempts", message.InlineAttempts),
            new NpgsqlParameter("@OriginalRetries", message.Retries),
            new NpgsqlParameter("@OriginalInlineAttempts", originalInlineAttempts),
            new NpgsqlParameter("@LockedUntil", message.LockedUntil.ToUtcParameterValue()),
            new NpgsqlParameter("@CurrentOwner", NpgsqlDbType.Varchar)
            {
                Value = message.Owner ?? (object)DBNull.Value,
            },
        ];
        await using var connection = postgreSqlOptions.Value.CreateConnection();
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
        DbTransaction? transaction,
        DateTimeOffset? nextRetryAt,
        DateTimeOffset? lockedUntil,
        int? originalRetries,
        int? originalInlineAttempts,
        CancellationToken cancellationToken
    )
    {
        var sql =
            $"UPDATE {tableName} SET \"Content\"=@Content,\"Retries\"=@Retries,\"InlineAttempts\"=@InlineAttempts,\"ExpiresAt\"=@ExpiresAt,\"NextRetryAt\"=@NextRetryAt,\"LockedUntil\"=@LockedUntil,\"Owner\"=@Owner,\"StatusName\"=@StatusName WHERE \"Id\"=@Id AND {_TerminalRowGuardWithRetries} AND (@OriginalInlineAttempts IS NULL OR (\"LockedUntil\" IS NOT DISTINCT FROM @OriginalLockedUntil AND \"Owner\" IS NOT DISTINCT FROM @OriginalOwner AND \"LockedUntil\">statement_timestamp()))";

        object[] sqlParams =
        [
            new NpgsqlParameter("@Id", message.StorageId),
            new NpgsqlParameter("@Content", serializer.Serialize(message.Origin)),
            new NpgsqlParameter("@Retries", message.Retries),
            new NpgsqlParameter("@InlineAttempts", message.InlineAttempts),
            new NpgsqlParameter("@ExpiresAt", message.ExpiresAt.ToUtcParameterValue()),
            new NpgsqlParameter("@NextRetryAt", nextRetryAt.ToUtcParameterValue()),
            new NpgsqlParameter("@LockedUntil", lockedUntil.ToUtcParameterValue()),
            new NpgsqlParameter("@Owner", NpgsqlDbType.Varchar)
            {
                Value = nodeMembership.GetOwnerParameterValue(lockedUntil),
            },
            new NpgsqlParameter("@OriginalRetries", NpgsqlDbType.Integer)
            {
                Value = originalRetries ?? (object)DBNull.Value,
            },
            new NpgsqlParameter("@OriginalInlineAttempts", NpgsqlDbType.Integer)
            {
                Value = originalInlineAttempts ?? (object)DBNull.Value,
            },
            new NpgsqlParameter("@OriginalLockedUntil", message.LockedUntil.ToUtcParameterValue()),
            new NpgsqlParameter("@OriginalOwner", NpgsqlDbType.Varchar)
            {
                Value = message.Owner ?? (object)DBNull.Value,
            },
            new NpgsqlParameter("@StatusName", state.ToString("G")),
        ];

        int affectedRows;

        if (transaction is not null)
        {
            var connection =
                transaction.Connection
                ?? throw new InvalidOperationException(
                    "The supplied DbTransaction has no active Connection — it may have already been committed or rolled back."
                );
            affectedRows = await connection
                .ExecuteNonQueryAsync(
                    sql,
                    transaction: transaction,
                    commandTimeout: messagingOptions.Value.CommandTimeout,
                    sqlParams: sqlParams,
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);
        }
        else
        {
            await using var connection = postgreSqlOptions.Value.CreateConnection();

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
        // Atomic upsert via INSERT ... ON CONFLICT ON CONSTRAINT against the partial unique index
        // (MessageId, COALESCE("Group", '')) created by PostgreSqlStorageInitializer. The COALESCE
        // expression collapses NULL groups to the empty string so NULL-Group rows participate in
        // the uniqueness check (a plain ("MessageId","Group") unique constraint treats NULLs as
        // distinct, which would let two concurrent broker redeliveries of a no-group message both
        // insert and produce duplicate rows).
        //
        // The terminal-row guard moves into the ON CONFLICT DO UPDATE WHERE clause: a row that is
        // already Succeeded/Failed with no scheduled retry is left untouched so a redelivered
        // already-exhausted message cannot overwrite a previously-Succeeded row's status back to
        // Failed (which would re-fire OnExhausted spuriously).
        //
        // RETURNING "Id" surfaces the authoritative persisted row id and discriminates written vs no-op:
        //   one row returned → a fresh INSERT, OR an ON CONFLICT DO UPDATE whose WHERE guard passed; its
        //                      "Id" is the canonical row id. On the UPDATE branch the existing row keeps
        //                      its original "Id" (EXCLUDED."Id" is intentionally not in the SET list), which
        //                      differs from the freshly-generated @Id — so callers must adopt this value.
        //   no row returned  → ON CONFLICT matched but the DO UPDATE WHERE guard blocked the update
        //                      (terminal-row guard or active-lease guard) → returns null ("not modified").
        // Use the inference form `ON CONFLICT (col, expression)` so the planner matches the
        // unique expression index by structure rather than
        // requiring a named constraint (a `CREATE UNIQUE INDEX` is an index, not a constraint;
        // `ON CONFLICT ON CONSTRAINT` would error out against it).
        // #3 — additionally skip rows whose lease is still active (LockedUntil in the future). A
        // redelivered message that arrives while the row is being dispatched would otherwise
        // overwrite LockedUntil = NULL, releasing the active pickup lease mid-attempt and letting
        // the retry processor re-pick the row while the inline retry burst is still in flight.
        // Ownership time is the DATABASE's: the guard compares against statement_timestamp(), the same
        // clock that wrote "LockedUntil" in _LeaseAndReserveAttemptAsync. Sampling the app clock here
        // instead would let a node running ahead of the server see a live lease as expired and CLEAR it.
        // The DO UPDATE SET list deliberately excludes "Retries" and "InlineAttempts" so a benign
        // redelivery collapse never resets the durable retry counters.
        // Mirrors the matching guard in SqlServerDataStorage._StoreReceivedMessage.
        var sql = $"""
            INSERT INTO {_receivedTable}("Id","Version","Name","Group","Content","IntentType","Retries","InlineAttempts","Added","ExpiresAt","NextRetryAt","LockedUntil","Owner","StatusName","MessageId","ExceptionInfo")
            VALUES(@Id,'{messagingOptions.Value.Version}',@Name,@Group,@Content,@IntentType,@Retries,@InlineAttempts,@Added,@ExpiresAt,@NextRetryAt,@LockedUntil,@Owner,@StatusName,@MessageId,@ExceptionInfo)
            ON CONFLICT ("Version", "MessageId", (COALESCE("Group", '')), "IntentType") DO UPDATE SET
                "StatusName"=EXCLUDED."StatusName",
                "ExpiresAt"=EXCLUDED."ExpiresAt",
                "NextRetryAt"=EXCLUDED."NextRetryAt",
                "LockedUntil"=EXCLUDED."LockedUntil",
                "Owner"=EXCLUDED."Owner",
                "Content"=EXCLUDED."Content",
                "ExceptionInfo"=EXCLUDED."ExceptionInfo"
            WHERE NOT ({_receivedTable}."StatusName" IN ('{nameof(StatusName.Succeeded)}','{nameof(
                StatusName.Failed
            )}') AND {_receivedTable}."NextRetryAt" IS NULL)
              AND ({_receivedTable}."LockedUntil" IS NULL OR {_receivedTable}."LockedUntil" <= statement_timestamp())
            RETURNING "Id"
            """;

        await using var connection = postgreSqlOptions.Value.CreateConnection();

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
        // Ownership time is the database's: one statement-stable snapshot supplies both the expiry
        // comparison and the new deadline. Returning the stored identity keeps the caller's fence
        // byte-for-byte aligned with durable state.
        var sql = $"""
            WITH clock AS (SELECT statement_timestamp() AS now)
            UPDATE {tableName} AS message
            SET "LockedUntil"=clock.now + (@LeaseSeconds * INTERVAL '1 second'),
                "Owner"=@Owner,
                "InlineAttempts"=@InlineAttempts
            FROM clock
            WHERE message."Id"=@Id
              AND (message."LockedUntil" IS NULL OR message."LockedUntil" <= clock.now)
              AND {_TerminalRowGuardWithRetries}
            RETURNING message."LockedUntil",message."Owner"
            """;

        var owner = nodeMembership.GetOwnerTag();
        object[] sqlParams =
        [
            new NpgsqlParameter("@Id", message.StorageId),
            new NpgsqlParameter("@LeaseSeconds", leaseDuration.TotalSeconds),
            new NpgsqlParameter("@Owner", NpgsqlDbType.Varchar) { Value = owner ?? (object)DBNull.Value },
            new NpgsqlParameter("@InlineAttempts", message.InlineAttempts),
            new NpgsqlParameter("@OriginalRetries", message.Retries),
            new NpgsqlParameter("@OriginalInlineAttempts", originalInlineAttempts),
        ];

        await using var connection = postgreSqlOptions.Value.CreateConnection();
        var storedLease = await connection
            .ExecuteReaderAsync(
                sql,
                LeaseDeadlineReader.ReadAsync,
                commandTimeout: messagingOptions.Value.CommandTimeout,
                sqlParams: sqlParams,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        return _ApplyStoredLease(message, storedLease);
    }

    private async ValueTask<bool> _LeaseMessageAsync(
        string tableName,
        MediumMessage message,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default
    )
    {
        // #15 — explicit lease-contention predicate: only acquire the lease when the row is unleased
        // OR its existing lease has expired. Mirrors the matching guard in SqlServerDataStorage._LeaseMessageAsync.
        // Ownership time is the database's — see _LeaseAndReserveAttemptAsync.
        var sql = $"""
            WITH clock AS (SELECT statement_timestamp() AS now)
            UPDATE {tableName} AS message
            SET "LockedUntil"=clock.now + (@LeaseSeconds * INTERVAL '1 second'),
                "Owner"=@Owner
            FROM clock
            WHERE message."Id"=@Id
              AND (message."LockedUntil" IS NULL OR message."LockedUntil" <= clock.now)
              AND {_TerminalRowGuardSimple}
            RETURNING message."LockedUntil",message."Owner"
            """;

        var owner = nodeMembership.GetOwnerTag();
        object[] sqlParams =
        [
            new NpgsqlParameter("@Id", message.StorageId),
            new NpgsqlParameter("@LeaseSeconds", leaseDuration.TotalSeconds),
            new NpgsqlParameter("@Owner", NpgsqlDbType.Varchar) { Value = owner ?? (object)DBNull.Value },
        ];

        await using var connection = postgreSqlOptions.Value.CreateConnection();
        var storedLease = await connection
            .ExecuteReaderAsync(
                sql,
                LeaseDeadlineReader.ReadAsync,
                commandTimeout: messagingOptions.Value.CommandTimeout,
                sqlParams: sqlParams,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        return _ApplyStoredLease(message, storedLease);
    }

    private static bool _ApplyStoredLease(
        MediumMessage message,
        (DateTimeOffset LockedUntil, string? Owner)? storedLease
    )
    {
        if (storedLease is not { } lease)
        {
            return false;
        }

        message.LockedUntil = lease.LockedUntil;
        message.Owner = lease.Owner;
        return true;
    }

    private async ValueTask<IEnumerable<MediumMessage>> _GetMessagesOfNeedRetryAsync(
        string tableName,
        CancellationToken cancellationToken = default
    )
    {
        // Atomic claim-and-return: a single UPDATE statement leases the rows (sets LockedUntil)
        // and RETURNS the selected columns in one round-trip. Replaces the previous two-step
        // SELECT-FOR-UPDATE-then-Lease pattern, which committed the SELECT transaction before
        // _LeaseAsync wrote LockedUntil. In between, a concurrent replica could pass the same
        // "LockedUntil IS NULL" filter and lease the same row — double-dispatch.
        //
        // FOR UPDATE SKIP LOCKED on the inner SELECT preserves the "skip rows another replica
        // is mid-claim on" behaviour. The UPDATE then assigns database time + DispatchTimeout
        // so subsequent pickup polls (anywhere) see the row as leased until the dispatch
        // attempt completes (or the lease expires).
        //
        // NextRetryAt is scheduling state written from the injected TimeProvider, so its due
        // predicate uses that same authority. Lease expiry and stamping remain on one statement-
        // time database snapshot, keeping every replica on one ownership authority without a
        // clock query.
        var sql = string.Create(
            CultureInfo.InvariantCulture,
            $"""
            WITH candidates AS (
                SELECT message."Id"
                FROM {tableName} AS message
                WHERE "Retries" <= @Retries
                  AND "Version" = @Version
                  AND "NextRetryAt" IS NOT NULL AND "NextRetryAt" <= @Now
                  AND ("LockedUntil" IS NULL OR "LockedUntil" <= statement_timestamp())
                  AND {_TerminalRowGuardSimple}
                ORDER BY "NextRetryAt"
                LIMIT {messagingOptions.Value.RetryBatchSize}
                FOR UPDATE SKIP LOCKED
            )
            UPDATE {tableName} AS message
            SET "LockedUntil" = statement_timestamp() + (@LeaseSeconds * INTERVAL '1 second'),
                "Owner" = @Owner
            FROM candidates
            WHERE message."Id" = candidates."Id"
            RETURNING message."Id",message."Content",message."IntentType",message."Retries",message."InlineAttempts",message."Added",message."NextRetryAt",message."LockedUntil",message."Owner";
            """
        );

        object[] sqlParams =
        [
            new NpgsqlParameter("@Retries", messagingOptions.Value.RetryPolicy.MaxPersistedRetries),
            new NpgsqlParameter("@Version", messagingOptions.Value.Version),
            new NpgsqlParameter("@Now", timeProvider.GetUtcNow()),
            new NpgsqlParameter("@LeaseSeconds", messagingOptions.Value.RetryPolicy.DispatchTimeout.TotalSeconds),
            new NpgsqlParameter("@Owner", NpgsqlDbType.Varchar)
            {
                Value = nodeMembership.GetOwnerTag() ?? (object)DBNull.Value,
            },
        ];

        await using var connection = postgreSqlOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var poisonMessages = new List<PoisonMessage>();
        var result = await connection
            .ExecuteReaderAsync(
                sql,
                async (reader, token) =>
                {
                    var messages = new List<MediumMessage>();
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
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
                                Added = await reader.GetFieldValueAsync<DateTimeOffset>(5, token).ConfigureAwait(false),
                                NextRetryAt = await reader.IsDBNullAsync(6, token).ConfigureAwait(false)
                                    ? null
                                    : await reader.GetFieldValueAsync<DateTimeOffset>(6, token).ConfigureAwait(false),
                                LockedUntil = await reader.IsDBNullAsync(7, token).ConfigureAwait(false)
                                    ? null
                                    : await reader.GetFieldValueAsync<DateTimeOffset>(7, token).ConfigureAwait(false),
                                Owner = await reader.IsDBNullAsync(8, token).ConfigureAwait(false)
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
        var ids = poisonMessages.Select(message => message.StorageId).ToArray();
        var sql = isReceivedTable
            ? $"""
                UPDATE {tableName} AS target
                SET "StatusName"=@StatusName,"NextRetryAt"=NULL,"LockedUntil"=NULL,"Owner"=NULL,
                    "ExpiresAt"=@ExpiresAt,"ExceptionInfo"=poison."ExceptionInfo"
                FROM unnest(@Ids::uuid[], @ExceptionInfos::text[]) AS poison("Id", "ExceptionInfo")
                WHERE target."Id"=poison."Id" AND {_TerminalRowGuardSimple};
                """
            : $"""
                UPDATE {tableName}
                SET "StatusName"=@StatusName,"NextRetryAt"=NULL,"LockedUntil"=NULL,"Owner"=NULL,"ExpiresAt"=@ExpiresAt
                WHERE "Id"=ANY(@Ids) AND {_TerminalRowGuardSimple};
                """;
        var sqlParams = new List<object>
        {
            new NpgsqlParameter("@Ids", ids) { DataTypeName = "uuid[]" },
            new NpgsqlParameter("@StatusName", nameof(StatusName.Failed)),
            new NpgsqlParameter("@ExpiresAt", expiresAt),
        };
        if (isReceivedTable)
        {
            sqlParams.Add(
                new NpgsqlParameter(
                    "@ExceptionInfos",
                    poisonMessages.Select(message => message.ExceptionInfo).ToArray()
                )
                {
                    DataTypeName = "text[]",
                }
            );
        }

        // PostgreSQL aborts the transaction after any statement failure. One savepoint around the batched
        // terminal mark lets the healthy rows' claim commit while the failed poison batch remains leased.
        const string savepointName = "headless_poison_mark_batch";
        if (transaction is not null)
        {
            await connection
                .ExecuteNonQueryAsync(
                    $"SAVEPOINT {savepointName};",
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
                    sqlParams: [.. sqlParams],
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);

            if (transaction is not null)
            {
                await connection
                    .ExecuteNonQueryAsync(
                        $"RELEASE SAVEPOINT {savepointName};",
                        transaction: transaction,
                        commandTimeout: messagingOptions.Value.CommandTimeout,
                        cancellationToken: cancellationToken
                    )
                    .ConfigureAwait(false);
            }
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
                        $"ROLLBACK TO SAVEPOINT {savepointName};",
                        transaction: transaction,
                        commandTimeout: messagingOptions.Value.CommandTimeout,
                        cancellationToken: cancellationToken
                    )
                    .ConfigureAwait(false);
            }
        }
    }

    private async ValueTask<int> _ReclaimDeadOwnersAsync(
        string tableName,
        IReadOnlyCollection<string> deadOwners,
        CancellationToken cancellationToken
    )
    {
        // Empty deadOwners trivially matches zero rows (`x = ANY(ARRAY[]::varchar[])` is always FALSE),
        // so the early return is an optimization that skips the round-trip, not a safety guard.
        if (deadOwners.Count == 0)
        {
            return 0;
        }

        // Intentionally version-agnostic: reclaim only shortens leases on rows owned by dead
        // node incarnations, then the normal version-filtered pickup path decides what this
        // service version is allowed to dispatch.
        var sql = $"""
            UPDATE {tableName} AS message
            SET "LockedUntil" = statement_timestamp()
            WHERE "Owner" IS NOT NULL
              AND "Owner" = ANY(@DeadOwners)
              AND "LockedUntil" > statement_timestamp()
              AND {_TerminalRowGuardSimple};
            """;

        await using var connection = postgreSqlOptions.Value.CreateConnection();
        return await connection
            .ExecuteNonQueryAsync(
                sql,
                commandTimeout: messagingOptions.Value.CommandTimeout,
                sqlParams: [new NpgsqlParameter("@DeadOwners", deadOwners.ToArray()) { DataTypeName = "varchar[]" }],
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
    }

    private static PoisonMessage _CreatePoisonMessage(Guid storageId, Exception exception)
    {
        return new(storageId, $"{exception.GetType().FullName}: {exception.Message}");
    }

    private readonly record struct PoisonMessage(Guid StorageId, string ExceptionInfo);
}
