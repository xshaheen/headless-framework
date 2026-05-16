// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.Abstractions;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.Serialization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.SqlServer;

/// <summary>
/// SQL Server implementation of <see cref="IDataStorage"/> for message persistence.
/// Handles storage, retrieval, and state transitions for published and received messages.
/// </summary>
public sealed class SqlServerDataStorage(
    IOptions<MessagingOptions> messagingOptions,
    IOptions<SqlServerOptions> options,
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

    public async ValueTask<bool> AcquireLockAsync(
        string key,
        TimeSpan ttl,
        string instance,
        CancellationToken cancellationToken = default
    )
    {
        var sql =
            $"UPDATE {_lockTable} SET [Instance]=@Instance,[LastLockTime]=@LastLockTime WHERE [Key]=@Key AND [LastLockTime] < @TTL;";
        await using var connection = new SqlConnection(options.Value.ConnectionString);

        object[] sqlParams =
        [
            new SqlParameter("@Instance", instance),
            new SqlParameter("@LastLockTime", timeProvider.GetUtcNow().UtcDateTime),
            new SqlParameter("@Key", key),
            new SqlParameter("@TTL", timeProvider.GetUtcNow().UtcDateTime.Subtract(ttl)),
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
            $"UPDATE {_lockTable} SET [Instance]='',[LastLockTime]=@LastLockTime WHERE [Key]=@Key AND [Instance]=@Instance;";

        await using var connection = new SqlConnection(options.Value.ConnectionString);

        object[] sqlParams =
        [
            new SqlParameter("@Instance", instance),
            new SqlParameter("@LastLockTime", new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc))
            {
                SqlDbType = SqlDbType.DateTime2,
            },
            new SqlParameter("@Key", key),
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
            $"UPDATE {_lockTable} SET [LastLockTime]=DATEADD(second,@TtlSeconds,[LastLockTime]) WHERE [Key]=@Key AND [Instance]=@Instance;";
        await using var connection = new SqlConnection(options.Value.ConnectionString);

        object[] sqlParams =
        [
            new SqlParameter("@Key", key),
            new SqlParameter("@Instance", instance),
            new SqlParameter("@TtlSeconds", (long)ttl.TotalSeconds),
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

        var parameters = new object[storageIds.Length + 1];
        var paramNames = new string[storageIds.Length];

        for (var i = 0; i < storageIds.Length; i++)
        {
            paramNames[i] = $"@Id{i}";
            parameters[i] = new SqlParameter($"@Id{i}", storageIds[i]);
        }

        parameters[^1] = new SqlParameter("@StatusName", nameof(StatusName.Delayed));

        var sql =
            $"UPDATE {_publishedTable} SET [StatusName]=@StatusName WHERE [Id] IN ({string.Join(',', paramNames)});";

        await using var connection = new SqlConnection(options.Value.ConnectionString);

        await connection
            .ExecuteNonQueryAsync(
                sql,
                commandTimeout: messagingOptions.Value.CommandTimeout,
                sqlParams: parameters,
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
            cancellationToken,
            lockedUntil,
            originalRetries
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
        var sql =
            // X1 terminal-row guard: refuses updates to rows that are already terminal AND
            // have NextRetryAt cleared. Failed rows with non-null NextRetryAt stay mutable so
            // the retry processor can rewrite them — see the matching note in PostgreSqlDataStorage.
            $"UPDATE {_receivedTable} SET Content=@Content, Retries=@Retries, ExpiresAt=@ExpiresAt, NextRetryAt=@NextRetryAt, LockedUntil=@LockedUntil, StatusName=@StatusName, ExceptionInfo=@ExceptionInfo WHERE Id=@Id AND NOT (StatusName IN ('{nameof(StatusName.Succeeded)}','{nameof(StatusName.Failed)}') AND NextRetryAt IS NULL) AND (@OriginalRetries IS NULL OR Retries=@OriginalRetries)";

        object[] sqlParams =
        [
            new SqlParameter("@Id", message.StorageId),
            new SqlParameter("@Content", serializer.Serialize(message.Origin)),
            new SqlParameter("@Retries", message.Retries),
            new SqlParameter("@ExpiresAt", SqlDbType.DateTime2)
            {
                Value = message.ExpiresAt.HasValue ? message.ExpiresAt.Value : DBNull.Value,
            },
            new SqlParameter("@NextRetryAt", SqlDbType.DateTime2) { Value = nextRetryAt.ToUtcParameterValue() },
            new SqlParameter("@LockedUntil", SqlDbType.DateTime2) { Value = lockedUntil.ToUtcParameterValue() },
            new SqlParameter("@OriginalRetries", originalRetries ?? (object)DBNull.Value),
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
            $"INSERT INTO {_publishedTable} ([Id],[Version],[Name],[Content],[Retries],[Added],[ExpiresAt],[NextRetryAt],[LockedUntil],[StatusName],[MessageId])"
            + $"VALUES(@Id,'{options.Value.Version}',@Name,@Content,@Retries,@Added,@ExpiresAt,@NextRetryAt,@LockedUntil,@StatusName,@MessageId);";

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
            new SqlParameter("@Id", message.StorageId),
            new SqlParameter("@Name", name),
            new SqlParameter("@Content", message.Content),
            new SqlParameter("@Retries", message.Retries),
            new SqlParameter("@Added", message.Added),
            new SqlParameter("@ExpiresAt", SqlDbType.DateTime2)
            {
                Value = message.ExpiresAt.HasValue ? message.ExpiresAt.Value : DBNull.Value,
            },
            new SqlParameter("@NextRetryAt", SqlDbType.DateTime2) { Value = message.NextRetryAt.ToUtcParameterValue() },
            new SqlParameter("@LockedUntil", SqlDbType.DateTime2) { Value = message.LockedUntil.ToUtcParameterValue() },
            new SqlParameter("@StatusName", nameof(StatusName.Scheduled)),
            new SqlParameter("@MessageId", content.GetId()),
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
            new SqlParameter("@Id", longIdGenerator.Create()),
            new SqlParameter("@Name", name),
            new SqlParameter("@Group", group),
            new SqlParameter("@Content", content),
            new SqlParameter("@Retries", messagingOptions.Value.RetryPolicy.MaxPersistedRetries),
            new SqlParameter("@Added", timeProvider.GetUtcNow().UtcDateTime),
            new SqlParameter("@ExpiresAt", SqlDbType.DateTime2)
            {
                Value = timeProvider
                    .GetUtcNow()
                    .UtcDateTime.AddSeconds(messagingOptions.Value.FailedMessageExpiredAfter),
            },
            new SqlParameter("@NextRetryAt", SqlDbType.DateTime2) { Value = DBNull.Value },
            new SqlParameter("@LockedUntil", SqlDbType.DateTime2) { Value = DBNull.Value },
            new SqlParameter("@StatusName", nameof(StatusName.Failed)),
            new SqlParameter("@MessageId", serializer.Deserialize(content)!.GetId()),
            new SqlParameter("@Version", messagingOptions.Value.Version),
            new SqlParameter("@ExceptionInfo", exceptionInfo ?? (object)DBNull.Value),
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
            new SqlParameter("@Id", mediumMessage.StorageId),
            new SqlParameter("@Name", name),
            new SqlParameter("@Group", group),
            new SqlParameter("@Content", mediumMessage.Content),
            new SqlParameter("@Retries", mediumMessage.Retries),
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
            new SqlParameter("@StatusName", nameof(StatusName.Scheduled)),
            new SqlParameter("@MessageId", message.GetId()),
            new SqlParameter("@Version", messagingOptions.Value.Version),
            new SqlParameter("@ExceptionInfo", DBNull.Value),
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
                 );
                """,
                transaction: null,
                commandTimeout: messagingOptions.Value.CommandTimeout,
                sqlParams: [new SqlParameter("@timeout", timeout), new SqlParameter("@batchCount", batchCount)],
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
    }

    public ValueTask<IEnumerable<MediumMessage>> GetPublishedMessagesOfNeedRetryAsync(
        CancellationToken cancellationToken = default
    )
    {
        return _GetMessagesOfNeedRetryAsync(_publishedTable, cancellationToken);
    }

    public ValueTask<IEnumerable<MediumMessage>> GetReceivedMessagesOfNeedRetryAsync(
        CancellationToken cancellationToken = default
    )
    {
        return _GetMessagesOfNeedRetryAsync(_receivedTable, cancellationToken);
    }

    public async ValueTask<int> DeleteReceivedMessageAsync(long id, CancellationToken cancellationToken = default)
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

    public async ValueTask<int> DeletePublishedMessageAsync(long id, CancellationToken cancellationToken = default)
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

    public async ValueTask ScheduleMessagesOfDelayedAsync(
        Func<object, IEnumerable<MediumMessage>, ValueTask> scheduleTask,
        CancellationToken cancellationToken = default
    )
    {
        var sql = $"""
            SELECT TOP (@BatchSize) Id, Content, Retries, Added, ExpiresAt FROM {_publishedTable} WITH (UPDLOCK, READPAST)
            WHERE Version = @Version AND StatusName = '{nameof(StatusName.Delayed)}' AND ExpiresAt < @TwoMinutesLater
            UNION ALL
            SELECT TOP (@BatchSize) Id, Content, Retries, Added, ExpiresAt FROM {_publishedTable} WITH (UPDLOCK, READPAST)
            WHERE Version = @Version AND StatusName = '{nameof(StatusName.Queued)}' AND ExpiresAt < @OneMinutesAgo;
            """;

        object[] sqlParams =
        [
            new SqlParameter("@Version", messagingOptions.Value.Version),
            new SqlParameter("@TwoMinutesLater", timeProvider.GetUtcNow().UtcDateTime.Add(_DelayedMessageLookahead)),
            new SqlParameter("@OneMinutesAgo", timeProvider.GetUtcNow().UtcDateTime.Subtract(_QueuedMessageLookback)),
            new SqlParameter("@BatchSize", messagingOptions.Value.SchedulerBatchSize),
        ];

        await using var connection = new SqlConnection(options.Value.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var messageList = await connection
            .ExecuteReaderAsync(
                sql,
                async (reader, ct) =>
                {
                    var messages = new List<MediumMessage>();
                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        var content = reader.GetString(1);

                        messages.Add(
                            new MediumMessage
                            {
                                StorageId = reader.GetInt64(0),
                                Origin = serializer.Deserialize(content)!,
                                Content = content,
                                Retries = reader.GetInt32(2),
                                Added = reader.GetDateTime(3),
                                ExpiresAt = reader.GetDateTime(4),
                            }
                        );
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

    public IMonitoringApi GetMonitoringApi()
    {
        return new SqlServerMonitoringApi(options, messagingOptions, initializer, serializer, timeProvider);
    }

    private async ValueTask<bool> _ChangeMessageStateAsync(
        string tableName,
        MediumMessage message,
        StatusName state,
        object? transaction = null,
        DateTime? nextRetryAt = null,
        CancellationToken cancellationToken = default,
        DateTime? lockedUntil = null,
        int? originalRetries = null
    )
    {
        var sql =
            $"UPDATE {tableName} SET Content=@Content, Retries=@Retries,ExpiresAt=@ExpiresAt,NextRetryAt=@NextRetryAt,LockedUntil=@LockedUntil,StatusName=@StatusName WHERE Id=@Id AND NOT (StatusName IN ('{nameof(StatusName.Succeeded)}','{nameof(StatusName.Failed)}') AND NextRetryAt IS NULL) AND (@OriginalRetries IS NULL OR Retries=@OriginalRetries)";

        object[] sqlParams =
        [
            new SqlParameter("@Id", message.StorageId),
            new SqlParameter("@Content", serializer.Serialize(message.Origin)),
            new SqlParameter("@Retries", message.Retries),
            new SqlParameter("@ExpiresAt", SqlDbType.DateTime2)
            {
                Value = message.ExpiresAt.HasValue ? message.ExpiresAt.Value : DBNull.Value,
            },
            new SqlParameter("@NextRetryAt", SqlDbType.DateTime2) { Value = nextRetryAt.ToUtcParameterValue() },
            new SqlParameter("@LockedUntil", SqlDbType.DateTime2) { Value = lockedUntil.ToUtcParameterValue() },
            new SqlParameter("@OriginalRetries", originalRetries ?? (object)DBNull.Value),
            new SqlParameter("@StatusName", state.ToString("G")),
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

    private async ValueTask<bool> _StoreReceivedMessage(
        object[] sqlParams,
        CancellationToken cancellationToken = default
    )
    {
        // The WHEN MATCHED predicate skips terminal Succeeded/Failed rows that have no scheduled
        // retry. Without the guard, a broker-redelivered message whose payload again fails to
        // deserialize would overwrite a previously-Succeeded row's status to Failed, firing
        // OnExhausted for a message that actually succeeded.
        var sql = $"""
            MERGE {_receivedTable} WITH (HOLDLOCK) AS target
            USING (SELECT @MessageId AS MessageId, @Group AS [Group]) AS source
            ON target.MessageId = source.MessageId AND (target.[Group] = source.[Group] OR (target.[Group] IS NULL AND source.[Group] IS NULL))
            WHEN MATCHED AND NOT (target.StatusName IN ('{nameof(StatusName.Succeeded)}','{nameof(
                StatusName.Failed
            )}') AND target.NextRetryAt IS NULL) THEN
                UPDATE SET StatusName = @StatusName, Retries = @Retries, ExpiresAt = @ExpiresAt, NextRetryAt = @NextRetryAt, LockedUntil = @LockedUntil, Content = @Content, ExceptionInfo = @ExceptionInfo
            WHEN NOT MATCHED THEN
                INSERT ([Id],[Version],[Name],[Group],[Content],[Retries],[Added],[ExpiresAt],[NextRetryAt],[LockedUntil],[StatusName],[MessageId],[ExceptionInfo])
                VALUES (@Id,@Version,@Name,@Group,@Content,@Retries,@Added,@ExpiresAt,@NextRetryAt,@LockedUntil,@StatusName,@MessageId,@ExceptionInfo);
            """;

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

    private async ValueTask<bool> _LeaseMessageAsync(
        string tableName,
        MediumMessage message,
        DateTime lockedUntil,
        CancellationToken cancellationToken = default
    )
    {
        var sql =
            $"UPDATE {tableName} SET LockedUntil=@LockedUntil WHERE Id=@Id AND NOT (StatusName IN ('{nameof(StatusName.Succeeded)}','{nameof(StatusName.Failed)}') AND NextRetryAt IS NULL)";

        object[] sqlParams =
        [
            new SqlParameter("@Id", message.StorageId),
            new SqlParameter("@LockedUntil", SqlDbType.DateTime2)
            {
                Value = ((DateTime?)lockedUntil).ToUtcParameterValue(),
            },
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
        // Use the injected TimeProvider rather than the DB server clock (GETUTCDATE()) so InMemory
        // and SQL providers share identical pickup semantics — keeps tests with a fake clock honest
        // and avoids subtle drift between application time and DB time.
        var sql =
            $"SELECT TOP ({_RetryBatchSize}) Id, Content, Retries, Added, NextRetryAt, LockedUntil FROM {tableName} WITH (UPDLOCK, READPAST) "
            + $"WHERE Retries <= @Retries AND Version = @Version AND NextRetryAt IS NOT NULL AND NextRetryAt <= @Now AND (LockedUntil IS NULL OR LockedUntil <= @Now);";

        object[] sqlParams =
        [
            new SqlParameter("@Retries", messagingOptions.Value.RetryPolicy.MaxPersistedRetries),
            new SqlParameter("@Version", messagingOptions.Value.Version),
            new SqlParameter("@Now", SqlDbType.DateTime2) { Value = timeProvider.GetUtcNow().UtcDateTime },
        ];

        await using var connection = new SqlConnection(options.Value.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var result = await connection
            .ExecuteReaderAsync(
                sql,
                async (reader, ct) =>
                {
                    var messages = new List<MediumMessage>();
                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        var content = reader.GetString(1);

                        messages.Add(
                            new MediumMessage
                            {
                                StorageId = reader.GetInt64(0),
                                Origin = serializer.Deserialize(content)!,
                                Content = content,
                                Retries = reader.GetInt32(2),
                                Added = reader.GetDateTime(3),
                                NextRetryAt = await reader.IsDBNullAsync(4, ct).ConfigureAwait(false)
                                    ? null
                                    : reader.GetDateTime(4),
                                LockedUntil = await reader.IsDBNullAsync(5, ct).ConfigureAwait(false)
                                    ? null
                                    : reader.GetDateTime(5),
                            }
                        );
                    }

                    return messages;
                },
                transaction: transaction,
                commandTimeout: messagingOptions.Value.CommandTimeout,
                sqlParams: sqlParams,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken);

        return result;
    }
}
