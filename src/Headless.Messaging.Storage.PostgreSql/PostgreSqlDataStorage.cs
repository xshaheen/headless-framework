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
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Headless.Messaging.Storage.PostgreSql;

/// <summary>
/// PostgreSQL implementation of <see cref="IDataStorage"/> for outbox pattern message persistence.
/// Handles storage, retrieval, and state management of published and received messages.
/// </summary>
public sealed class PostgreSqlDataStorage(
    IOptions<PostgreSqlOptions> postgreSqlOptions,
    IOptions<MessagingOptions> messagingOptions,
    IStorageInitializer initializer,
    ISerializer serializer,
    // PostgreSQL stores message ids as native uuid (big-endian byte sort) -> Version7 keeps the PK sequential.
    [FromKeyedServices(SequentialGuidType.Version7)] IGuidGenerator guidGenerator,
    TimeProvider timeProvider,
    INodeMembership nodeMembership
) : IDataStorage
{
    /// <summary>
    /// Maximum messages to fetch in a single retry batch.
    /// Higher values process more but increase memory and lock contention.
    /// </summary>
    private const int _RetryBatchSize = 200;

    /// <summary>
    /// Reusable WHERE-clause fragment that refuses updates to rows already in a terminal state
    /// (<c>Succeeded</c> / <c>Failed</c>) with no scheduled retry, while still respecting an
    /// optional optimistic-concurrency token (<c>@OriginalRetries</c>). Used by Change*StateAsync
    /// paths that pass <c>@OriginalRetries</c>.
    /// </summary>
    private const string _TerminalRowGuardWithRetries =
        "NOT (\"StatusName\" IN ('Succeeded','Failed') AND \"NextRetryAt\" IS NULL) AND (@OriginalRetries IS NULL OR \"Retries\"=@OriginalRetries)";

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
    private readonly INodeMembership _nodeMembership = nodeMembership;

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

        var sql = $"UPDATE {_publishedTable} SET \"StatusName\"=@StatusName WHERE \"Id\" = ANY(@Ids);";

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
            cancellationToken
        );
    }

    /// <summary>
    /// Acquires a dispatch lease on a published message by setting <c>LockedUntil</c> and <c>Owner</c>.
    /// Only succeeds if the row is currently unleased or its existing lease has expired.
    /// </summary>
    /// <returns><see langword="true"/> if the lease was acquired; <see langword="false"/> if another node already holds it.</returns>
    public ValueTask<bool> LeasePublishAsync(
        MediumMessage message,
        DateTime lockedUntil,
        CancellationToken cancellationToken = default
    ) => _LeaseMessageAsync(_publishedTable, message, lockedUntil, cancellationToken);

    /// <summary>
    /// Updates the status of a received message, including writing <c>ExceptionInfo</c> when the
    /// message faulted. Respects the terminal-row guard — permanently completed rows are not mutated.
    /// </summary>
    /// <returns><see langword="true"/> if a row was updated; <see langword="false"/> if the guard blocked it.</returns>
    public async ValueTask<bool> ChangeReceiveStateAsync(
        MediumMessage message,
        StatusName state,
        DateTime? nextRetryAt = null,
        DateTime? lockedUntil = null,
        int? originalRetries = null,
        CancellationToken cancellationToken = default
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
            $"UPDATE {_receivedTable} SET \"Content\"=@Content,\"Retries\"=@Retries,\"ExpiresAt\"=@ExpiresAt,\"NextRetryAt\"=@NextRetryAt,\"LockedUntil\"=@LockedUntil,\"Owner\"=@Owner,\"StatusName\"=@StatusName,\"ExceptionInfo\"=@ExceptionInfo WHERE \"Id\"=@Id AND {_TerminalRowGuardWithRetries}";

        object[] sqlParams =
        [
            new NpgsqlParameter("@Id", message.StorageId),
            new NpgsqlParameter("@Content", serializer.Serialize(message.Origin)),
            new NpgsqlParameter("@Retries", message.Retries),
            new NpgsqlParameter("@ExpiresAt", message.ExpiresAt.HasValue ? message.ExpiresAt.Value : DBNull.Value),
            new NpgsqlParameter("@NextRetryAt", nextRetryAt.ToUtcParameterValue()),
            new NpgsqlParameter("@LockedUntil", lockedUntil.ToUtcParameterValue()),
            new NpgsqlParameter("@Owner", NpgsqlDbType.Varchar)
            {
                Value = _nodeMembership.GetOwnerParameterValue(lockedUntil),
            },
            new NpgsqlParameter("@OriginalRetries", NpgsqlDbType.Integer)
            {
                Value = originalRetries ?? (object)DBNull.Value,
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
        DateTime lockedUntil,
        CancellationToken cancellationToken = default
    ) => _LeaseMessageAsync(_receivedTable, message, lockedUntil, cancellationToken);

    /// <summary>
    /// Persists a published outbox message to the <c>published</c> table. When <paramref name="transaction"/>
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
            $"INSERT INTO {_publishedTable} (\"Id\",\"Version\",\"Name\",\"Content\",\"IntentType\",\"Retries\",\"Added\",\"ExpiresAt\",\"NextRetryAt\",\"LockedUntil\",\"Owner\",\"StatusName\",\"MessageId\")"
            + $"VALUES(@Id,'{messagingOptions.Value.Version}',@Name,@Content,@IntentType,@Retries,@Added,@ExpiresAt,@NextRetryAt,@LockedUntil,@Owner,@StatusName,@MessageId);";

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
        };

        object[] sqlParams =
        [
            new NpgsqlParameter("@Id", stored.StorageId),
            new NpgsqlParameter("@Name", name),
            new NpgsqlParameter("@Content", stored.Content),
            new NpgsqlParameter("@IntentType", (short)stored.IntentType),
            new NpgsqlParameter("@Retries", stored.Retries),
            new NpgsqlParameter("@Added", stored.Added),
            new NpgsqlParameter("@ExpiresAt", stored.ExpiresAt.HasValue ? stored.ExpiresAt.Value : DBNull.Value),
            new NpgsqlParameter("@NextRetryAt", stored.NextRetryAt.ToUtcParameterValue()),
            new NpgsqlParameter("@LockedUntil", stored.LockedUntil.ToUtcParameterValue()),
            new NpgsqlParameter("@Owner", NpgsqlDbType.Varchar) { Value = DBNull.Value },
            new NpgsqlParameter("@StatusName", nameof(StatusName.Scheduled)),
            new NpgsqlParameter("@MessageId", message.Origin.GetId()),
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

            await dbTrans
                .Connection!.ExecuteNonQueryAsync(
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
    /// Persists a published outbox message built from a raw <c>Message</c> payload to the <c>published</c> table.
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
            new NpgsqlParameter("@Id", guidGenerator.Create()),
            new NpgsqlParameter("@Name", name),
            new NpgsqlParameter("@Group", NpgsqlDbType.Varchar) { Value = (object?)group ?? DBNull.Value },
            new NpgsqlParameter(
                "@Content",
                string.IsNullOrEmpty(message.Content) ? serializer.Serialize(message.Origin) : message.Content
            ),
            new NpgsqlParameter("@IntentType", (short)message.IntentType),
            new NpgsqlParameter("@Retries", messagingOptions.Value.RetryPolicy.MaxPersistedRetries),
            new NpgsqlParameter("@Added", timeProvider.GetUtcNow().UtcDateTime),
            new NpgsqlParameter(
                "@ExpiresAt",
                timeProvider.GetUtcNow().UtcDateTime.AddSeconds(messagingOptions.Value.FailedMessageExpiredAfter)
            ),
            new NpgsqlParameter("@NextRetryAt", DBNull.Value),
            new NpgsqlParameter("@LockedUntil", DBNull.Value),
            new NpgsqlParameter("@Owner", NpgsqlDbType.Varchar) { Value = DBNull.Value },
            new NpgsqlParameter("@StatusName", nameof(StatusName.Failed)),
            new NpgsqlParameter("@MessageId", message.Origin.GetId()),
            new NpgsqlParameter("@ExceptionInfo", exceptionInfo ?? (object)DBNull.Value),
        ];

        return await _StoreReceivedMessage(sqlParams, cancellationToken).ConfigureAwait(false);
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
        };

        object[] sqlParams =
        [
            new NpgsqlParameter("@Id", mediumMessage.StorageId),
            new NpgsqlParameter("@Name", name),
            new NpgsqlParameter("@Group", NpgsqlDbType.Varchar) { Value = (object?)group ?? DBNull.Value },
            new NpgsqlParameter("@Content", mediumMessage.Content),
            new NpgsqlParameter("@IntentType", (short)mediumMessage.IntentType),
            new NpgsqlParameter("@Retries", mediumMessage.Retries),
            new NpgsqlParameter("@Added", mediumMessage.Added),
            new NpgsqlParameter(
                "@ExpiresAt",
                mediumMessage.ExpiresAt.HasValue ? mediumMessage.ExpiresAt.Value : DBNull.Value
            ),
            new NpgsqlParameter("@NextRetryAt", mediumMessage.NextRetryAt.ToUtcParameterValue()),
            new NpgsqlParameter("@LockedUntil", mediumMessage.LockedUntil.ToUtcParameterValue()),
            new NpgsqlParameter("@Owner", NpgsqlDbType.Varchar) { Value = DBNull.Value },
            new NpgsqlParameter("@StatusName", nameof(StatusName.Scheduled)),
            new NpgsqlParameter("@MessageId", message.Origin.GetId()),
            new NpgsqlParameter("@ExceptionInfo", DBNull.Value),
        ];

        await _StoreReceivedMessage(sqlParams, cancellationToken).ConfigureAwait(false);

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

    /// <summary>
    /// Atomically selects delayed and stale-queued messages within a database transaction and
    /// invokes <paramref name="scheduleTask"/> to re-enqueue them. The SELECT uses
    /// <c>FOR UPDATE SKIP LOCKED</c> so concurrent replicas skip rows another node is scheduling.
    /// The transaction is committed after <paramref name="scheduleTask"/> completes.
    /// </summary>
    public async ValueTask ScheduleMessagesOfDelayedAsync(
        Func<object?, IEnumerable<MediumMessage>, ValueTask> scheduleTask,
        CancellationToken cancellationToken = default
    )
    {
        var sql =
            $"SELECT \"Id\",\"Content\",\"IntentType\",\"Retries\",\"Added\",\"ExpiresAt\" FROM {_publishedTable} WHERE \"Version\"=@Version "
            + $"AND ((\"ExpiresAt\"< @TwoMinutesLater AND \"StatusName\" = '{nameof(StatusName.Delayed)}') OR (\"ExpiresAt\"< @OneMinutesAgo AND \"StatusName\" = '{nameof(StatusName.Queued)}')) FOR UPDATE SKIP LOCKED LIMIT @BatchSize;";

        var sqlParams = new object[]
        {
            new NpgsqlParameter("@Version", messagingOptions.Value.Version),
            new NpgsqlParameter("@TwoMinutesLater", timeProvider.GetUtcNow().UtcDateTime.Add(_DelayedMessageLookahead)),
            new NpgsqlParameter(
                "@OneMinutesAgo",
                timeProvider.GetUtcNow().UtcDateTime.Subtract(_QueuedMessageLookback)
            ),
            new NpgsqlParameter("@BatchSize", messagingOptions.Value.SchedulerBatchSize),
        };

        await using var connection = postgreSqlOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var messageList = await connection
            .ExecuteReaderAsync(
                sql,
                async (reader, token) =>
                {
                    var messages = new List<MediumMessage>();
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        var content = reader.GetString(1);

                        var mediumMessage = new MediumMessage
                        {
                            StorageId = reader.GetGuid(0),
                            Origin = serializer.Deserialize(content)!,
                            Content = content,
                            IntentType = (IntentType)reader.GetInt16(2),
                            Retries = reader.GetInt32(3),
                            Added = reader.GetDateTime(4),
                            ExpiresAt = reader.GetDateTime(5),
                        };

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

        await scheduleTask(transaction, messageList);

        await transaction.CommitAsync(cancellationToken);
    }

    // NOTE: ChangeReceiveStateAsync does not call this helper because the receive path additionally
    // writes ExceptionInfo, a column absent from the published table schema. Keep these two methods
    // in sync when adding columns.
    private async ValueTask<bool> _ChangeMessageStateAsync(
        string tableName,
        MediumMessage message,
        StatusName state,
        object? transaction,
        DateTime? nextRetryAt,
        DateTime? lockedUntil,
        int? originalRetries,
        CancellationToken cancellationToken
    )
    {
        var sql =
            $"UPDATE {tableName} SET \"Content\"=@Content,\"Retries\"=@Retries,\"ExpiresAt\"=@ExpiresAt,\"NextRetryAt\"=@NextRetryAt,\"LockedUntil\"=@LockedUntil,\"Owner\"=@Owner,\"StatusName\"=@StatusName WHERE \"Id\"=@Id AND {_TerminalRowGuardWithRetries}";

        object[] sqlParams =
        [
            new NpgsqlParameter("@Id", message.StorageId),
            new NpgsqlParameter("@Content", serializer.Serialize(message.Origin)),
            new NpgsqlParameter("@Retries", message.Retries),
            new NpgsqlParameter("@ExpiresAt", message.ExpiresAt.HasValue ? message.ExpiresAt.Value : DBNull.Value),
            new NpgsqlParameter("@NextRetryAt", nextRetryAt.ToUtcParameterValue()),
            new NpgsqlParameter("@LockedUntil", lockedUntil.ToUtcParameterValue()),
            new NpgsqlParameter("@Owner", NpgsqlDbType.Varchar)
            {
                Value = _nodeMembership.GetOwnerParameterValue(lockedUntil),
            },
            new NpgsqlParameter("@OriginalRetries", NpgsqlDbType.Integer)
            {
                Value = originalRetries ?? (object)DBNull.Value,
            },
            new NpgsqlParameter("@StatusName", state.ToString("G")),
        ];

        int affectedRows;
        if (transaction is DbTransaction dbTransaction)
        {
            var connection = dbTransaction.Connection!;
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
            var connection = dbTrans.Connection!;
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

    private async ValueTask<bool> _StoreReceivedMessage(
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
        // RETURNING xmax gives us a cheap way to discriminate insert vs update vs no-op:
        //   xmax = 0           → fresh INSERT (no previous tuple version)
        //   xmax = current txn → UPDATE branch matched and the WHERE filter let it run
        //   no row             → ON CONFLICT matched but the WHERE filter blocked the update
        //                        (terminal-row guard hit). We treat as "not modified" → return false.
        // Use the inference form `ON CONFLICT (col, expression)` so the planner matches the
        // unique expression index by structure rather than
        // requiring a named constraint (a `CREATE UNIQUE INDEX` is an index, not a constraint;
        // `ON CONFLICT ON CONSTRAINT` would error out against it).
        // #3 — additionally skip rows whose lease is still active (LockedUntil in the future). A
        // redelivered message that arrives while the row is being dispatched would otherwise
        // overwrite LockedUntil = NULL and Retries = 0, releasing the active pickup lease
        // mid-attempt and letting the retry processor re-pick the row while the inline retry loop
        // is still in flight. Mirrors the matching guard in SqlServerDataStorage._StoreReceivedMessage.
        var sql = $"""
            INSERT INTO {_receivedTable}("Id","Version","Name","Group","Content","IntentType","Retries","Added","ExpiresAt","NextRetryAt","LockedUntil","Owner","StatusName","MessageId","ExceptionInfo")
            VALUES(@Id,'{messagingOptions.Value.Version}',@Name,@Group,@Content,@IntentType,@Retries,@Added,@ExpiresAt,@NextRetryAt,@LockedUntil,@Owner,@StatusName,@MessageId,@ExceptionInfo)
            ON CONFLICT ("Version", "MessageId", (COALESCE("Group", '')), "IntentType") DO UPDATE SET
                "StatusName"=EXCLUDED."StatusName",
                "Retries"=EXCLUDED."Retries",
                "ExpiresAt"=EXCLUDED."ExpiresAt",
                "NextRetryAt"=EXCLUDED."NextRetryAt",
                "LockedUntil"=EXCLUDED."LockedUntil",
                "Owner"=EXCLUDED."Owner",
                "Content"=EXCLUDED."Content",
                "ExceptionInfo"=EXCLUDED."ExceptionInfo"
            WHERE NOT ({_receivedTable}."StatusName" IN ('{nameof(StatusName.Succeeded)}','{nameof(
                StatusName.Failed
            )}') AND {_receivedTable}."NextRetryAt" IS NULL)
              AND ({_receivedTable}."LockedUntil" IS NULL OR {_receivedTable}."LockedUntil" <= now())
            """;

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

    private async ValueTask<bool> _LeaseMessageAsync(
        string tableName,
        MediumMessage message,
        DateTime lockedUntil,
        CancellationToken cancellationToken = default
    )
    {
        // #15 — explicit lease-contention predicate: only acquire the lease when the row is unleased
        // OR its existing lease has expired. Mirrors the matching guard in SqlServerDataStorage._LeaseMessageAsync.
        var sql =
            $"UPDATE {tableName} SET \"LockedUntil\"=@LockedUntil,\"Owner\"=@Owner WHERE \"Id\"=@Id "
            + "AND (\"LockedUntil\" IS NULL OR \"LockedUntil\" <= @Now) "
            + $"AND {_TerminalRowGuardSimple}";

        var owner = _nodeMembership.GetOwnerTag();
        object[] sqlParams =
        [
            new NpgsqlParameter("@Id", message.StorageId),
            new NpgsqlParameter("@LockedUntil", ((DateTime?)lockedUntil).ToUtcParameterValue()),
            new NpgsqlParameter("@Owner", NpgsqlDbType.Varchar) { Value = owner ?? (object)DBNull.Value },
            new NpgsqlParameter("@Now", timeProvider.GetUtcNow().UtcDateTime),
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

        if (affectedRows > 0)
        {
            message.LockedUntil = ((DateTime?)lockedUntil).ToUtcOrSelf();
            message.Owner = owner;
        }

        return affectedRows > 0;
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
        // is mid-claim on" behaviour. The UPDATE then assigns @NewLease = now + DispatchTimeout
        // so subsequent pickup polls (anywhere) see the row as leased until the dispatch
        // attempt completes (or the lease expires).
        //
        // Use the injected TimeProvider rather than the DB server clock (now()) so InMemory and
        // SQL providers share identical pickup semantics — keeps tests with a fake clock honest
        // and avoids subtle drift between application time and DB time.
        var sql = $"""
            UPDATE {tableName} SET "LockedUntil" = @NewLease, "Owner" = @Owner
            WHERE "Id" IN (
                SELECT "Id" FROM {tableName}
                WHERE "Retries" <= @Retries
                  AND "Version" = @Version
                  AND "NextRetryAt" IS NOT NULL AND "NextRetryAt" <= @Now
                  AND ("LockedUntil" IS NULL OR "LockedUntil" <= @Now)
                  AND {_TerminalRowGuardSimple}
                ORDER BY "NextRetryAt"
                LIMIT {_RetryBatchSize}
                FOR UPDATE SKIP LOCKED
            )
            RETURNING "Id","Content","IntentType","Retries","Added","NextRetryAt","LockedUntil","Owner";
            """;

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var newLease = now.Add(messagingOptions.Value.RetryPolicy.DispatchTimeout);

        object[] sqlParams =
        [
            new NpgsqlParameter("@Retries", messagingOptions.Value.RetryPolicy.MaxPersistedRetries),
            new NpgsqlParameter("@Version", messagingOptions.Value.Version),
            new NpgsqlParameter("@Now", now),
            new NpgsqlParameter("@NewLease", newLease),
            new NpgsqlParameter("@Owner", NpgsqlDbType.Varchar)
            {
                Value = _nodeMembership.GetOwnerTag() ?? (object)DBNull.Value,
            },
        ];

        await using var connection = postgreSqlOptions.Value.CreateConnection();

        var result = await connection
            .ExecuteReaderAsync(
                sql,
                async (reader, token) =>
                {
                    var messages = new List<MediumMessage>();
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        var content = reader.GetString(1);

                        var mediumMessage = new MediumMessage
                        {
                            StorageId = reader.GetGuid(0),
                            Origin = serializer.Deserialize(content)!,
                            Content = content,
                            IntentType = (IntentType)reader.GetInt16(2),
                            Retries = reader.GetInt32(3),
                            Added = reader.GetDateTime(4),
                            NextRetryAt = await reader.IsDBNullAsync(5, token).ConfigureAwait(false)
                                ? null
                                : reader.GetDateTime(5),
                            LockedUntil = await reader.IsDBNullAsync(6, token).ConfigureAwait(false)
                                ? null
                                : reader.GetDateTime(6),
                            Owner = await reader.IsDBNullAsync(7, token).ConfigureAwait(false)
                                ? null
                                : reader.GetString(7),
                        };

                        messages.Add(mediumMessage);
                    }

                    return messages;
                },
                commandTimeout: messagingOptions.Value.CommandTimeout,
                sqlParams: sqlParams,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        return result;
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

        var now = timeProvider.GetUtcNow().UtcDateTime;
        // Intentionally version-agnostic: reclaim only shortens leases on rows owned by dead
        // node incarnations, then the normal version-filtered pickup path decides what this
        // service version is allowed to dispatch.
        var sql = $"""
            UPDATE {tableName}
            SET "LockedUntil" = @Now
            WHERE "Owner" IS NOT NULL
              AND "Owner" = ANY(@DeadOwners)
              AND "LockedUntil" > @Now
              AND {_TerminalRowGuardSimple};
            """;

        await using var connection = postgreSqlOptions.Value.CreateConnection();
        return await connection
            .ExecuteNonQueryAsync(
                sql,
                commandTimeout: messagingOptions.Value.CommandTimeout,
                sqlParams:
                [
                    new NpgsqlParameter("@Now", now),
                    new NpgsqlParameter("@DeadOwners", NpgsqlDbType.Array | NpgsqlDbType.Varchar)
                    {
                        Value = deadOwners.ToArray(),
                    },
                ],
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
    }
}
