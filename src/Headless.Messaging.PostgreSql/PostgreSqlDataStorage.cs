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
        return new PostgreSqlMonitoringApi(postgreSqlOptions, initializer, serializer, timeProvider);
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
            .ExecuteNonQueryAsync(sql, cancellationToken: cancellationToken, sqlParams: sqlParams)
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
            .ExecuteNonQueryAsync(sql, cancellationToken: cancellationToken, sqlParams: sqlParams)
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
            .ExecuteNonQueryAsync(sql, cancellationToken: cancellationToken, sqlParams: sqlParams)
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
            .ExecuteNonQueryAsync(sql, cancellationToken: cancellationToken, sqlParams: sqlParams)
            .ConfigureAwait(false);
    }

    public ValueTask ChangePublishStateAsync(
        MediumMessage message,
        StatusName state,
        object? transaction = null,
        CancellationToken cancellationToken = default
    )
    {
        return _ChangeMessageStateAsync(_publishedTable, message, state, transaction, cancellationToken);
    }

    public async ValueTask ChangeReceiveStateAsync(
        MediumMessage message,
        StatusName state,
        CancellationToken cancellationToken = default
    )
    {
        var sql =
            $"UPDATE {_receivedTable} SET \"Content\"=@Content,\"Retries\"=@Retries,\"ExpiresAt\"=@ExpiresAt,\"StatusName\"=@StatusName,\"ExceptionInfo\"=@ExceptionInfo WHERE \"Id\"=@Id";

        object[] sqlParams =
        [
            new NpgsqlParameter("@Id", message.StorageId),
            new NpgsqlParameter("@Content", serializer.Serialize(message.Origin)),
            new NpgsqlParameter("@Retries", message.Retries),
            new NpgsqlParameter(
                "@ExpiresAt",
                message.ExpiresAt.HasValue ? (object)message.ExpiresAt.Value : DBNull.Value
            ),
            new NpgsqlParameter("@StatusName", state.ToString("G")),
            new NpgsqlParameter("@ExceptionInfo", message.ExceptionInfo ?? (object)DBNull.Value),
        ];

        await using var connection = postgreSqlOptions.Value.CreateConnection();
        await connection
            .ExecuteNonQueryAsync(sql, cancellationToken: cancellationToken, sqlParams: sqlParams)
            .ConfigureAwait(false);
    }

    public async ValueTask<MediumMessage> StoreMessageAsync(
        string name,
        Message content,
        object? transaction = null,
        CancellationToken cancellationToken = default
    )
    {
        var sql =
            $"INSERT INTO {_publishedTable} (\"Id\",\"Version\",\"Name\",\"Content\",\"Retries\",\"Added\",\"ExpiresAt\",\"StatusName\",\"MessageId\")"
            + $"VALUES(@Id,'{postgreSqlOptions.Value.Version}',@Name,@Content,@Retries,@Added,@ExpiresAt,@StatusName,@MessageId);";

        var message = new MediumMessage
        {
            StorageId = longIdGenerator.Create(),
            Origin = content,
            Content = serializer.Serialize(content),
            Added = timeProvider.GetUtcNow().UtcDateTime,
            ExpiresAt = null,
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
            new NpgsqlParameter("@StatusName", nameof(StatusName.Scheduled)),
            new NpgsqlParameter("@MessageId", content.GetId()),
        ];

        if (transaction == null)
        {
            await using var connection = postgreSqlOptions.Value.CreateConnection();
            await connection
                .ExecuteNonQueryAsync(sql, cancellationToken: cancellationToken, sqlParams: sqlParams)
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
                .Connection!.ExecuteNonQueryAsync(sql, dbTrans, cancellationToken, sqlParams)
                .ConfigureAwait(false);
        }

        return message;
    }

    public async ValueTask StoreReceivedExceptionMessageAsync(
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
            new NpgsqlParameter("@Retries", messagingOptions.Value.FailedRetryCount),
            new NpgsqlParameter("@Added", timeProvider.GetUtcNow().UtcDateTime),
            new NpgsqlParameter(
                "@ExpiresAt",
                timeProvider.GetUtcNow().UtcDateTime.AddSeconds(messagingOptions.Value.FailedMessageExpiredAfter)
            ),
            new NpgsqlParameter("@StatusName", nameof(StatusName.Failed)),
            new NpgsqlParameter("@MessageId", serializer.Deserialize(content)!.GetId()),
            new NpgsqlParameter("@ExceptionInfo", exceptionInfo ?? (object)DBNull.Value),
        ];

        await _StoreReceivedMessage(sqlParams, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<MediumMessage> StoreReceivedMessageAsync(
        string name,
        string group,
        Message message,
        CancellationToken cancellationToken = default
    )
    {
        var mediumMessage = new MediumMessage
        {
            StorageId = longIdGenerator.Create(),
            Origin = message,
            Content = serializer.Serialize(message),
            Added = timeProvider.GetUtcNow().UtcDateTime,
            ExpiresAt = null,
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
                    LIMIT @batchCount
                )
                """,
                transaction: null,
                cancellationToken,
                sqlParams
            )
            .ConfigureAwait(false);
    }

    public async ValueTask<IEnumerable<MediumMessage>> GetPublishedMessagesOfNeedRetry(
        TimeSpan lookbackSeconds,
        CancellationToken cancellationToken = default
    )
    {
        return await _GetMessagesOfNeedRetryAsync(_publishedTable, lookbackSeconds, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<IEnumerable<MediumMessage>> GetReceivedMessagesOfNeedRetry(
        TimeSpan lookbackSeconds,
        CancellationToken cancellationToken = default
    )
    {
        return await _GetMessagesOfNeedRetryAsync(_receivedTable, lookbackSeconds, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<int> DeleteReceivedMessageAsync(long id, CancellationToken cancellationToken = default)
    {
        var sql = $"""DELETE FROM {_receivedTable} WHERE "Id"=@Id""";

        await using var connection = postgreSqlOptions.Value.CreateConnection();
        var result = await connection
            .ExecuteNonQueryAsync(sql, cancellationToken: cancellationToken, sqlParams: new NpgsqlParameter("@Id", id))
            .ConfigureAwait(false);
        return result;
    }

    public async ValueTask<int> DeletePublishedMessageAsync(long id, CancellationToken cancellationToken = default)
    {
        var sql = $"""DELETE FROM {_publishedTable} WHERE "Id"=@Id""";

        await using var connection = postgreSqlOptions.Value.CreateConnection();
        var result = await connection
            .ExecuteNonQueryAsync(sql, cancellationToken: cancellationToken, sqlParams: new NpgsqlParameter("@Id", id))
            .ConfigureAwait(false);
        return result;
    }

    public async ValueTask ScheduleMessagesOfDelayedAsync(
        Func<object, IEnumerable<MediumMessage>, ValueTask> scheduleTask,
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
                transaction,
                cancellationToken,
                sqlParams
            )
            .ConfigureAwait(false);

        await scheduleTask(transaction, messageList);

        await transaction.CommitAsync(cancellationToken);
    }

    private async ValueTask _ChangeMessageStateAsync(
        string tableName,
        MediumMessage message,
        StatusName state,
        object? transaction = null,
        CancellationToken cancellationToken = default
    )
    {
        var sql =
            $"UPDATE {tableName} SET \"Content\"=@Content,\"Retries\"=@Retries,\"ExpiresAt\"=@ExpiresAt,\"StatusName\"=@StatusName WHERE \"Id\"=@Id";

        object[] sqlParams =
        [
            new NpgsqlParameter("@Id", message.StorageId),
            new NpgsqlParameter("@Content", serializer.Serialize(message.Origin)),
            new NpgsqlParameter("@Retries", message.Retries),
            new NpgsqlParameter("@ExpiresAt", message.ExpiresAt.HasValue ? message.ExpiresAt.Value : DBNull.Value),
            new NpgsqlParameter("@StatusName", state.ToString("G")),
        ];

        if (transaction is DbTransaction dbTransaction)
        {
            var connection = dbTransaction.Connection!;
            await connection
                .ExecuteNonQueryAsync(sql, dbTransaction, cancellationToken, sqlParams)
                .ConfigureAwait(false);
        }
        else if (transaction is IDbContextTransaction efTransaction)
        {
            var dbTrans = efTransaction.GetDbTransaction();
            var connection = dbTrans.Connection!;
            await connection.ExecuteNonQueryAsync(sql, dbTrans, cancellationToken, sqlParams).ConfigureAwait(false);
        }
        else
        {
            await using var connection = postgreSqlOptions.Value.CreateConnection();

            await connection
                .ExecuteNonQueryAsync(sql, cancellationToken: cancellationToken, sqlParams: sqlParams)
                .ConfigureAwait(false);
        }
    }

    private async ValueTask _StoreReceivedMessage(object[] sqlParams, CancellationToken cancellationToken = default)
    {
        var sql = $"""
            INSERT INTO {_receivedTable}("Id","Version","Name","Group","Content","Retries","Added","ExpiresAt","StatusName","MessageId","ExceptionInfo")
            VALUES(@Id,'{messagingOptions.Value.Version}',@Name,@Group,@Content,@Retries,@Added,@ExpiresAt,@StatusName,@MessageId,@ExceptionInfo)
            ON CONFLICT ("MessageId","Group") DO UPDATE SET
                "StatusName"=EXCLUDED."StatusName",
                "Retries"=EXCLUDED."Retries",
                "ExpiresAt"=EXCLUDED."ExpiresAt",
                "Content"=EXCLUDED."Content",
                "ExceptionInfo"=EXCLUDED."ExceptionInfo";
            """;

        await using var connection = postgreSqlOptions.Value.CreateConnection();

        await connection
            .ExecuteNonQueryAsync(sql, cancellationToken: cancellationToken, sqlParams: sqlParams)
            .ConfigureAwait(false);
    }

    private async ValueTask<IEnumerable<MediumMessage>> _GetMessagesOfNeedRetryAsync(
        string tableName,
        TimeSpan lookbackSeconds,
        CancellationToken cancellationToken = default
    )
    {
        var cutoffTime = timeProvider.GetUtcNow().UtcDateTime.Subtract(lookbackSeconds);
        var sql =
            $"SELECT \"Id\",\"Content\",\"Retries\",\"Added\" FROM {tableName} WHERE \"Retries\"<@Retries "
            + $"AND \"Version\"=@Version AND \"Added\"<@Added AND \"StatusName\" IN ('{nameof(StatusName.Failed)}','{nameof(StatusName.Scheduled)}') LIMIT {_RetryBatchSize} FOR UPDATE SKIP LOCKED;";

        object[] sqlParams =
        [
            new NpgsqlParameter("@Retries", messagingOptions.Value.FailedRetryCount),
            new NpgsqlParameter("@Version", messagingOptions.Value.Version),
            new NpgsqlParameter("@Added", cutoffTime),
        ];

        await using var connection = postgreSqlOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

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
                        };

                        messages.Add(mediumMessage);
                    }

                    return messages;
                },
                transaction,
                cancellationToken,
                sqlParams
            )
            .ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken);

        return result;
    }
}
