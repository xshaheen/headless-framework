// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Abstractions;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.Serialization;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Headless.Messaging.PostgreSql;

/// <summary>
/// PostgreSQL implementation of <see cref="IDataStorage"/> for outbox pattern message persistence.
/// Handles storage, retrieval, and state management of published and received messages.
/// </summary>
public sealed class PostgreSqlDataStorage(
    IOptions<PostgreSqlOptions> postgreSqlOptions,
    IOptions<MessagingOptions> messagingOptions,
    IStorageInitializer initializer,
    ISerializer serializer,
    ILongIdGenerator longIdGenerator,
    TimeProvider timeProvider
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

    private readonly string _lockTable = initializer.GetLockTableName();
    private readonly string _publishedTable = initializer.GetPublishedTableName();
    private readonly string _receivedTable = initializer.GetReceivedTableName();

    public IMonitoringApi GetMonitoringApi()
    {
        return new PostgreSqlMonitoringApi(postgreSqlOptions, messagingOptions, initializer, serializer, timeProvider);
    }

    public async ValueTask<bool> AcquireLockAsync(
        string key,
        TimeSpan ttl,
        string instance,
        CancellationToken cancellationToken = default
    )
    {
        var sql =
            $"UPDATE {_lockTable} SET \"Instance\"=@Instance,\"LastLockTime\"=@LastLockTime WHERE \"Key\"=@Key AND \"LastLockTime\" < @TTL;";
        await using var connection = postgreSqlOptions.Value.CreateConnection();

        object[] sqlParams =
        [
            new NpgsqlParameter("@Instance", instance),
            new NpgsqlParameter("@LastLockTime", timeProvider.GetUtcNow().UtcDateTime),
            new NpgsqlParameter("@Key", key),
            new NpgsqlParameter("@TTL", timeProvider.GetUtcNow().UtcDateTime.Subtract(ttl)),
        ];

        var opResult = await connection
            .ExecuteNonQueryAsync(
                sql,
                commandTimeout: messagingOptions.Value.CommandTimeout,
                sqlParams: sqlParams,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        return opResult > 0;
    }

    public async ValueTask ReleaseLockAsync(string key, string instance, CancellationToken cancellationToken = default)
    {
        var sql =
            $"UPDATE {_lockTable} SET \"Instance\"='',\"LastLockTime\"=@LastLockTime WHERE \"Key\"=@Key AND \"Instance\"=@Instance;";

        await using var connection = postgreSqlOptions.Value.CreateConnection();

        object[] sqlParams =
        [
            new NpgsqlParameter("@Instance", instance),
            new NpgsqlParameter("@LastLockTime", DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc)),
            new NpgsqlParameter("@Key", key),
        ];

        await connection
            .ExecuteNonQueryAsync(
                sql,
                commandTimeout: messagingOptions.Value.CommandTimeout,
                sqlParams: sqlParams,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
    }

    public async ValueTask RenewLockAsync(
        string key,
        TimeSpan ttl,
        string instance,
        CancellationToken cancellationToken = default
    )
    {
        var sql =
            $"UPDATE {_lockTable} SET \"LastLockTime\"=\"LastLockTime\"+(interval '1 second' * @TtlSeconds) WHERE \"Key\"=@Key AND \"Instance\"=@Instance;";

        await using var connection = postgreSqlOptions.Value.CreateConnection();

        object[] sqlParams =
        [
            new NpgsqlParameter("@Instance", instance),
            new NpgsqlParameter("@Key", key),
            new NpgsqlParameter("@TtlSeconds", ttl.TotalSeconds),
        ];

        await connection
            .ExecuteNonQueryAsync(
                sql,
                commandTimeout: messagingOptions.Value.CommandTimeout,
                sqlParams: sqlParams,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
    }

    public async ValueTask ChangePublishStateToDelayedAsync(
        long[] storageIds,
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

    public ValueTask<bool> LeasePublishAsync(
        MediumMessage message,
        DateTime lockedUntil,
        CancellationToken cancellationToken = default
    ) => _LeaseMessageAsync(_publishedTable, message, lockedUntil, cancellationToken);

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
            $"UPDATE {_receivedTable} SET \"Content\"=@Content,\"Retries\"=@Retries,\"ExpiresAt\"=@ExpiresAt,\"NextRetryAt\"=@NextRetryAt,\"LockedUntil\"=@LockedUntil,\"StatusName\"=@StatusName,\"ExceptionInfo\"=@ExceptionInfo WHERE \"Id\"=@Id AND {_TerminalRowGuardWithRetries}";

        object[] sqlParams =
        [
            new NpgsqlParameter("@Id", message.StorageId),
            new NpgsqlParameter("@Content", serializer.Serialize(message.Origin)),
            new NpgsqlParameter("@Retries", message.Retries),
            new NpgsqlParameter("@ExpiresAt", message.ExpiresAt.HasValue ? message.ExpiresAt.Value : DBNull.Value),
            new NpgsqlParameter("@NextRetryAt", nextRetryAt.ToUtcParameterValue()),
            new NpgsqlParameter("@LockedUntil", lockedUntil.ToUtcParameterValue()),
            new NpgsqlParameter("@OriginalRetries", originalRetries ?? (object)DBNull.Value),
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

    public ValueTask<bool> LeaseReceiveAsync(
        MediumMessage message,
        DateTime lockedUntil,
        CancellationToken cancellationToken = default
    ) => _LeaseMessageAsync(_receivedTable, message, lockedUntil, cancellationToken);

    public async ValueTask<MediumMessage> StoreMessageAsync(
        string name,
        Message content,
        object? transaction = null,
        CancellationToken cancellationToken = default
    )
    {
        var sql =
            $"INSERT INTO {_publishedTable} (\"Id\",\"Version\",\"Name\",\"Content\",\"Retries\",\"Added\",\"ExpiresAt\",\"NextRetryAt\",\"LockedUntil\",\"StatusName\",\"MessageId\")"
            + $"VALUES(@Id,'{postgreSqlOptions.Value.Version}',@Name,@Content,@Retries,@Added,@ExpiresAt,@NextRetryAt,@LockedUntil,@StatusName,@MessageId);";

        var added = timeProvider.GetUtcNow().UtcDateTime;
        var message = new MediumMessage
        {
            StorageId = longIdGenerator.Create(),
            Origin = content,
            Content = serializer.Serialize(content),
            Added = added,
            ExpiresAt = null,
            NextRetryAt = added.Add(messagingOptions.Value.RetryPolicy.InitialDispatchGrace),
            LockedUntil = null,
            Retries = 0,
        };

        object[] sqlParams =
        [
            new NpgsqlParameter("@Id", message.StorageId),
            new NpgsqlParameter("@Name", name),
            new NpgsqlParameter("@Content", message.Content),
            new NpgsqlParameter("@Retries", message.Retries),
            new NpgsqlParameter("@Added", message.Added),
            new NpgsqlParameter("@ExpiresAt", message.ExpiresAt.HasValue ? message.ExpiresAt.Value : DBNull.Value),
            new NpgsqlParameter("@NextRetryAt", message.NextRetryAt.ToUtcParameterValue()),
            new NpgsqlParameter("@LockedUntil", message.LockedUntil.ToUtcParameterValue()),
            new NpgsqlParameter("@StatusName", nameof(StatusName.Scheduled)),
            new NpgsqlParameter("@MessageId", content.GetId()),
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

        return message;
    }

    public async ValueTask<bool> StoreReceivedExceptionMessageAsync(
        string name,
        string group,
        string content,
        string? exceptionInfo = null,
        CancellationToken cancellationToken = default
    )
    {
        object[] sqlParams =
        [
            new NpgsqlParameter("@Id", longIdGenerator.Create()),
            new NpgsqlParameter("@Name", name),
            new NpgsqlParameter("@Group", group),
            new NpgsqlParameter("@Content", content),
            new NpgsqlParameter("@Retries", messagingOptions.Value.RetryPolicy.MaxPersistedRetries),
            new NpgsqlParameter("@Added", timeProvider.GetUtcNow().UtcDateTime),
            new NpgsqlParameter(
                "@ExpiresAt",
                timeProvider.GetUtcNow().UtcDateTime.AddSeconds(messagingOptions.Value.FailedMessageExpiredAfter)
            ),
            new NpgsqlParameter("@NextRetryAt", DBNull.Value),
            new NpgsqlParameter("@LockedUntil", DBNull.Value),
            new NpgsqlParameter("@StatusName", nameof(StatusName.Failed)),
            new NpgsqlParameter("@MessageId", serializer.Deserialize(content)!.GetId()),
            new NpgsqlParameter("@ExceptionInfo", exceptionInfo ?? (object)DBNull.Value),
        ];

        return await _StoreReceivedMessage(sqlParams, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<MediumMessage> StoreReceivedMessageAsync(
        string name,
        string group,
        Message message,
        CancellationToken cancellationToken = default
    )
    {
        var added = timeProvider.GetUtcNow().UtcDateTime;
        var mediumMessage = new MediumMessage
        {
            StorageId = longIdGenerator.Create(),
            Origin = message,
            Content = serializer.Serialize(message),
            Added = added,
            ExpiresAt = null,
            NextRetryAt = added.Add(messagingOptions.Value.RetryPolicy.InitialDispatchGrace),
            LockedUntil = null,
            Retries = 0,
        };

        object[] sqlParams =
        [
            new NpgsqlParameter("@Id", mediumMessage.StorageId),
            new NpgsqlParameter("@Name", name),
            new NpgsqlParameter("@Group", group),
            new NpgsqlParameter("@Content", mediumMessage.Content),
            new NpgsqlParameter("@Retries", mediumMessage.Retries),
            new NpgsqlParameter("@Added", mediumMessage.Added),
            new NpgsqlParameter(
                "@ExpiresAt",
                mediumMessage.ExpiresAt.HasValue ? mediumMessage.ExpiresAt.Value : DBNull.Value
            ),
            new NpgsqlParameter("@NextRetryAt", mediumMessage.NextRetryAt.ToUtcParameterValue()),
            new NpgsqlParameter("@LockedUntil", mediumMessage.LockedUntil.ToUtcParameterValue()),
            new NpgsqlParameter("@StatusName", nameof(StatusName.Scheduled)),
            new NpgsqlParameter("@MessageId", message.GetId()),
            new NpgsqlParameter("@ExceptionInfo", DBNull.Value),
        ];

        await _StoreReceivedMessage(sqlParams, cancellationToken).ConfigureAwait(false);

        return mediumMessage;
    }

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

    public async ValueTask<IEnumerable<MediumMessage>> GetPublishedMessagesOfNeedRetryAsync(
        CancellationToken cancellationToken = default
    )
    {
        return await _GetMessagesOfNeedRetryAsync(_publishedTable, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<IEnumerable<MediumMessage>> GetReceivedMessagesOfNeedRetryAsync(
        CancellationToken cancellationToken = default
    )
    {
        return await _GetMessagesOfNeedRetryAsync(_receivedTable, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<int> DeleteReceivedMessageAsync(long id, CancellationToken cancellationToken = default)
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

    public async ValueTask<int> DeletePublishedMessageAsync(long id, CancellationToken cancellationToken = default)
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

    public async ValueTask ScheduleMessagesOfDelayedAsync(
        Func<object?, IEnumerable<MediumMessage>, ValueTask> scheduleTask,
        CancellationToken cancellationToken = default
    )
    {
        var sql =
            $"SELECT \"Id\",\"Content\",\"Retries\",\"Added\",\"ExpiresAt\" FROM {_publishedTable} WHERE \"Version\"=@Version "
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
                            StorageId = reader.GetInt64(0),
                            Origin = serializer.Deserialize(content)!,
                            Content = content,
                            Retries = reader.GetInt32(2),
                            Added = reader.GetDateTime(3),
                            ExpiresAt = reader.GetDateTime(4),
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
            $"UPDATE {tableName} SET \"Content\"=@Content,\"Retries\"=@Retries,\"ExpiresAt\"=@ExpiresAt,\"NextRetryAt\"=@NextRetryAt,\"LockedUntil\"=@LockedUntil,\"StatusName\"=@StatusName WHERE \"Id\"=@Id AND {_TerminalRowGuardWithRetries}";

        object[] sqlParams =
        [
            new NpgsqlParameter("@Id", message.StorageId),
            new NpgsqlParameter("@Content", serializer.Serialize(message.Origin)),
            new NpgsqlParameter("@Retries", message.Retries),
            new NpgsqlParameter("@ExpiresAt", message.ExpiresAt.HasValue ? message.ExpiresAt.Value : DBNull.Value),
            new NpgsqlParameter("@NextRetryAt", nextRetryAt.ToUtcParameterValue()),
            new NpgsqlParameter("@LockedUntil", lockedUntil.ToUtcParameterValue()),
            new NpgsqlParameter("@OriginalRetries", originalRetries ?? (object)DBNull.Value),
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
        // partial unique index `uq_received_MessageId_GroupCoalesced` by structure rather than
        // requiring a named constraint (a `CREATE UNIQUE INDEX` is an index, not a constraint;
        // `ON CONFLICT ON CONSTRAINT` would error out against it).
        var sql = $"""
            INSERT INTO {_receivedTable}("Id","Version","Name","Group","Content","Retries","Added","ExpiresAt","NextRetryAt","LockedUntil","StatusName","MessageId","ExceptionInfo")
            VALUES(@Id,'{messagingOptions.Value.Version}',@Name,@Group,@Content,@Retries,@Added,@ExpiresAt,@NextRetryAt,@LockedUntil,@StatusName,@MessageId,@ExceptionInfo)
            ON CONFLICT ("MessageId", (COALESCE("Group", ''))) DO UPDATE SET
                "StatusName"=EXCLUDED."StatusName",
                "Retries"=EXCLUDED."Retries",
                "ExpiresAt"=EXCLUDED."ExpiresAt",
                "NextRetryAt"=EXCLUDED."NextRetryAt",
                "LockedUntil"=EXCLUDED."LockedUntil",
                "Content"=EXCLUDED."Content",
                "ExceptionInfo"=EXCLUDED."ExceptionInfo"
            WHERE NOT ({_receivedTable}."StatusName" IN ('{nameof(StatusName.Succeeded)}','{nameof(
                StatusName.Failed
            )}') AND {_receivedTable}."NextRetryAt" IS NULL)
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
        var sql = $"UPDATE {tableName} SET \"LockedUntil\"=@LockedUntil WHERE \"Id\"=@Id AND {_TerminalRowGuardSimple}";

        object[] sqlParams =
        [
            new NpgsqlParameter("@Id", message.StorageId),
            new NpgsqlParameter("@LockedUntil", ((DateTime?)lockedUntil).ToUtcParameterValue()),
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
            UPDATE {tableName} SET "LockedUntil" = @NewLease
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
            RETURNING "Id","Content","Retries","Added","NextRetryAt","LockedUntil";
            """;

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var newLease = now.Add(messagingOptions.Value.RetryPolicy.DispatchTimeout);

        object[] sqlParams =
        [
            new NpgsqlParameter("@Retries", messagingOptions.Value.RetryPolicy.MaxPersistedRetries),
            new NpgsqlParameter("@Version", messagingOptions.Value.Version),
            new NpgsqlParameter("@Now", now),
            new NpgsqlParameter("@NewLease", newLease),
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
                            StorageId = reader.GetInt64(0),
                            Origin = serializer.Deserialize(content)!,
                            Content = content,
                            Retries = reader.GetInt32(2),
                            Added = reader.GetDateTime(3),
                            NextRetryAt = await reader.IsDBNullAsync(4, token).ConfigureAwait(false)
                                ? null
                                : reader.GetDateTime(4),
                            LockedUntil = await reader.IsDBNullAsync(5, token).ConfigureAwait(false)
                                ? null
                                : reader.GetDateTime(5),
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
}
