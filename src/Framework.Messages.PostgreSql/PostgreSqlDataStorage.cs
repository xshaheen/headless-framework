// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Framework.Abstractions;
using Framework.Messages.Configuration;
using Framework.Messages.Internal;
using Framework.Messages.Messages;
using Framework.Messages.Monitoring;
using Framework.Messages.Persistence;
using Framework.Messages.Serialization;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Framework.Messages;

public sealed class PostgreSqlDataStorage(
    IOptions<PostgreSqlOptions> postgreSqlOptions,
    IOptions<MessagingOptions> messagingOptions,
    IStorageInitializer initializer,
    ISerializer serializer,
    ILongIdGenerator longIdGenerator,
    TimeProvider timeProvider
) : IDataStorage
{
    private readonly string _lockName = initializer.GetLockTableName();
    private readonly string _pubName = initializer.GetPublishedTableName();
    private readonly string _recName = initializer.GetReceivedTableName();

    public async Task<bool> AcquireLockAsync(
        string key,
        TimeSpan ttl,
        string instance,
        CancellationToken token = default
    )
    {
        var sql =
            $"UPDATE {_lockName} SET \"Instance\"=@Instance,\"LastLockTime\"=@LastLockTime WHERE \"Key\"=@Key AND \"LastLockTime\" < @TTL;";
        var connection = postgreSqlOptions.Value.CreateConnection();
        await using var _ = connection;
        object[] sqlParams =
        [
            new NpgsqlParameter("@Instance", instance),
            new NpgsqlParameter("@LastLockTime", timeProvider.GetUtcNow().UtcDateTime),
            new NpgsqlParameter("@Key", key),
            new NpgsqlParameter("@TTL", timeProvider.GetUtcNow().UtcDateTime.Subtract(ttl)),
        ];
        var opResult = await connection.ExecuteNonQueryAsync(sql, sqlParams: sqlParams).AnyContext();
        return opResult > 0;
    }

    public async Task ReleaseLockAsync(string key, string instance, CancellationToken token = default)
    {
        var sql =
            $"UPDATE {_lockName} SET \"Instance\"='',\"LastLockTime\"=@LastLockTime WHERE \"Key\"=@Key AND \"Instance\"=@Instance;";
        var connection = postgreSqlOptions.Value.CreateConnection();
        await using var _ = connection;
        object[] sqlParams =
        [
            new NpgsqlParameter("@Instance", instance),
            new NpgsqlParameter("@LastLockTime", DateTime.MinValue),
            new NpgsqlParameter("@Key", key),
        ];
        await connection.ExecuteNonQueryAsync(sql, sqlParams: sqlParams).AnyContext();
    }

    public async Task RenewLockAsync(string key, TimeSpan ttl, string instance, CancellationToken token = default)
    {
        var sql =
            $"UPDATE {_lockName} SET \"LastLockTime\"=\"LastLockTime\"+interval '{ttl.TotalSeconds}' second WHERE \"Key\"=@Key AND \"Instance\"=@Instance;";
        var connection = postgreSqlOptions.Value.CreateConnection();
        await using var _ = connection;
        object[] sqlParams = [new NpgsqlParameter("@Instance", instance), new NpgsqlParameter("@Key", key)];
        await connection.ExecuteNonQueryAsync(sql, sqlParams: sqlParams).AnyContext();
    }

    public async Task ChangePublishStateToDelayedAsync(string[] ids)
    {
        var sql =
            $"UPDATE {_pubName} SET \"StatusName\"='{StatusName.Delayed}' WHERE \"Id\" IN ({string.Join(',', ids)});";
        var connection = postgreSqlOptions.Value.CreateConnection();
        await using var _ = connection;
        await connection.ExecuteNonQueryAsync(sql).AnyContext();
    }

    public async Task ChangePublishStateAsync(MediumMessage message, StatusName state, object? transaction = null)
    {
        await _ChangeMessageStateAsync(_pubName, message, state, transaction).AnyContext();
    }

    public async Task ChangeReceiveStateAsync(MediumMessage message, StatusName state)
    {
        await _ChangeMessageStateAsync(_recName, message, state).AnyContext();
    }

    public async Task<MediumMessage> StoreMessageAsync(string name, Message content, object? transaction = null)
    {
        var sql =
            $"INSERT INTO {_pubName} (\"Id\",\"Version\",\"Name\",\"Content\",\"Retries\",\"Added\",\"ExpiresAt\",\"StatusName\")"
            + $"VALUES(@Id,'{postgreSqlOptions.Value.Version}',@Name,@Content,@Retries,@Added,@ExpiresAt,@StatusName);";

        var message = new MediumMessage
        {
            DbId = content.GetId(),
            Origin = content,
            Content = serializer.Serialize(content),
            Added = timeProvider.GetUtcNow().UtcDateTime,
            ExpiresAt = null,
            Retries = 0,
        };

        object[] sqlParams =
        [
            new NpgsqlParameter("@Id", long.Parse(message.DbId, CultureInfo.InvariantCulture)),
            new NpgsqlParameter("@Name", name),
            new NpgsqlParameter("@Content", message.Content),
            new NpgsqlParameter("@Retries", message.Retries),
            new NpgsqlParameter("@Added", message.Added),
            new NpgsqlParameter("@ExpiresAt", message.ExpiresAt.HasValue ? message.ExpiresAt.Value : DBNull.Value),
            new NpgsqlParameter("@StatusName", nameof(StatusName.Scheduled)),
        ];

        if (transaction == null)
        {
            var connection = postgreSqlOptions.Value.CreateConnection();
            await using var _ = connection;
            await connection.ExecuteNonQueryAsync(sql, sqlParams: sqlParams).AnyContext();
        }
        else
        {
            var dbTrans = transaction as DbTransaction;
            if (dbTrans == null && transaction is IDbContextTransaction dbContextTrans)
            {
                dbTrans = dbContextTrans.GetDbTransaction();
            }

            var conn = dbTrans?.Connection!;
            await conn.ExecuteNonQueryAsync(sql, dbTrans, sqlParams).AnyContext();
        }

        return message;
    }

    public async Task StoreReceivedExceptionMessageAsync(string name, string group, string content)
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
        ];

        await _StoreReceivedMessage(sqlParams).AnyContext();
    }

    public async Task<MediumMessage> StoreReceivedMessageAsync(string name, string group, Message message)
    {
        var mediumMessage = new MediumMessage
        {
            DbId = longIdGenerator.Create().ToString(CultureInfo.InvariantCulture),
            Origin = message,
            Content = serializer.Serialize(message),
            Added = timeProvider.GetUtcNow().UtcDateTime,
            ExpiresAt = null,
            Retries = 0,
        };

        object[] sqlParams =
        [
            new NpgsqlParameter("@Id", long.Parse(mediumMessage.DbId, CultureInfo.InvariantCulture)),
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
        ];

        await _StoreReceivedMessage(sqlParams).AnyContext();

        return mediumMessage;
    }

    public async Task<int> DeleteExpiresAsync(
        string table,
        DateTime timeout,
        int batchCount = 1000,
        CancellationToken token = default
    )
    {
        var connection = postgreSqlOptions.Value.CreateConnection();
        await using var _ = connection;

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
                null,
                new NpgsqlParameter("@timeout", timeout),
                new NpgsqlParameter("@batchCount", batchCount)
            )
            .AnyContext();
    }

    public async Task<IEnumerable<MediumMessage>> GetPublishedMessagesOfNeedRetry(TimeSpan lookbackSeconds)
    {
        return await _GetMessagesOfNeedRetryAsync(_pubName, lookbackSeconds).AnyContext();
    }

    public async Task<IEnumerable<MediumMessage>> GetReceivedMessagesOfNeedRetry(TimeSpan lookbackSeconds)
    {
        return await _GetMessagesOfNeedRetryAsync(_recName, lookbackSeconds).AnyContext();
    }

    public async Task<int> DeleteReceivedMessageAsync(long id)
    {
        var sql = $"""DELETE FROM {_recName} WHERE "Id"={id.ToString(CultureInfo.InvariantCulture)}""";

        var connection = postgreSqlOptions.Value.CreateConnection();
        await using var _ = connection;
        var result = await connection.ExecuteNonQueryAsync(sql);
        return result;
    }

    public async Task<int> DeletePublishedMessageAsync(long id)
    {
        var sql = $"""DELETE FROM {_pubName} WHERE "Id"={id.ToString(CultureInfo.InvariantCulture)}""";

        var connection = postgreSqlOptions.Value.CreateConnection();
        await using var _ = connection;
        var result = await connection.ExecuteNonQueryAsync(sql);
        return result;
    }

    public async Task ScheduleMessagesOfDelayedAsync(
        Func<object, IEnumerable<MediumMessage>, Task> scheduleTask,
        CancellationToken token = default
    )
    {
        var sql =
            $"SELECT \"Id\",\"Content\",\"Retries\",\"Added\",\"ExpiresAt\" FROM {_pubName} WHERE \"Version\"=@Version "
            + $"AND ((\"ExpiresAt\"< @TwoMinutesLater AND \"StatusName\" = '{StatusName.Delayed}') OR (\"ExpiresAt\"< @OneMinutesAgo AND \"StatusName\" = '{StatusName.Queued}')) FOR UPDATE SKIP LOCKED LIMIT @BatchSize;";

        var sqlParams = new object[]
        {
            new NpgsqlParameter("@Version", messagingOptions.Value.Version),
            new NpgsqlParameter("@TwoMinutesLater", timeProvider.GetUtcNow().UtcDateTime.AddMinutes(2)),
            new NpgsqlParameter("@OneMinutesAgo", _QueuedMessageFetchTime()),
            new NpgsqlParameter("@BatchSize", messagingOptions.Value.SchedulerBatchSize),
        };

        await using var connection = postgreSqlOptions.Value.CreateConnection();
        await connection.OpenAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);

        var messageList = await connection
            .ExecuteReaderAsync(
                sql,
                async reader =>
                {
                    var messages = new List<MediumMessage>();
                    while (await reader.ReadAsync(token).AnyContext())
                    {
                        var content = reader.GetString(1);

                        messages.Add(
                            new MediumMessage
                            {
                                DbId = reader.GetInt64(0).ToString(CultureInfo.InvariantCulture),
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
                transaction,
                sqlParams
            )
            .AnyContext();

        await scheduleTask(transaction, messageList);

        await transaction.CommitAsync(token);
    }

    public IMonitoringApi GetMonitoringApi()
    {
        return new PostgreSqlMonitoringApi(postgreSqlOptions, initializer, serializer, timeProvider);
    }

    private DateTime _QueuedMessageFetchTime()
    {
        return timeProvider.GetUtcNow().UtcDateTime.AddMinutes(-1);
    }

    private async Task _ChangeMessageStateAsync(
        string tableName,
        MediumMessage message,
        StatusName state,
        object? transaction = null
    )
    {
        var sql =
            $"UPDATE {tableName} SET \"Content\"=@Content,\"Retries\"=@Retries,\"ExpiresAt\"=@ExpiresAt,\"StatusName\"=@StatusName WHERE \"Id\"=@Id";

        object[] sqlParams =
        [
            new NpgsqlParameter("@Id", long.Parse(message.DbId)),
            new NpgsqlParameter("@Content", serializer.Serialize(message.Origin)),
            new NpgsqlParameter("@Retries", message.Retries),
            new NpgsqlParameter("@ExpiresAt", message.ExpiresAt.HasValue ? message.ExpiresAt.Value : DBNull.Value),
            new NpgsqlParameter("@StatusName", state.ToString("G")),
        ];

        if (transaction is DbTransaction dbTransaction)
        {
            var connection = (NpgsqlConnection)dbTransaction.Connection!;
            await connection.ExecuteNonQueryAsync(sql, dbTransaction, sqlParams).AnyContext();
        }
        else
        {
            await using var connection = postgreSqlOptions.Value.CreateConnection();
            await using var _ = connection;
            await connection.ExecuteNonQueryAsync(sql, sqlParams: sqlParams).AnyContext();
        }
    }

    private async Task _StoreReceivedMessage(object[] sqlParams)
    {
        var sql =
            $"INSERT INTO {_recName}(\"Id\",\"Version\",\"Name\",\"Group\",\"Content\",\"Retries\",\"Added\",\"ExpiresAt\",\"StatusName\")"
            + $"VALUES(@Id,'{messagingOptions.Value.Version}',@Name,@Group,@Content,@Retries,@Added,@ExpiresAt,@StatusName) RETURNING \"Id\";";

        var connection = postgreSqlOptions.Value.CreateConnection();
        await using var _ = connection;
        await connection.ExecuteNonQueryAsync(sql, sqlParams: sqlParams).AnyContext();
    }

    private async Task<IEnumerable<MediumMessage>> _GetMessagesOfNeedRetryAsync(
        string tableName,
        TimeSpan lookbackSeconds
    )
    {
        var fourMinAgo = timeProvider.GetUtcNow().UtcDateTime.Subtract(lookbackSeconds);
        var sql =
            $"SELECT \"Id\",\"Content\",\"Retries\",\"Added\" FROM {tableName} WHERE \"Retries\"<@Retries "
            + $"AND \"Version\"=@Version AND \"Added\"<@Added AND \"StatusName\" IN ('{StatusName.Failed}','{StatusName.Scheduled}') LIMIT 200;";

        object[] sqlParams =
        [
            new NpgsqlParameter("@Retries", messagingOptions.Value.FailedRetryCount),
            new NpgsqlParameter("@Version", messagingOptions.Value.Version),
            new NpgsqlParameter("@Added", fourMinAgo),
        ];

        var connection = postgreSqlOptions.Value.CreateConnection();
        await using var _ = connection;
        var result = await connection
            .ExecuteReaderAsync(
                sql,
                async reader =>
                {
                    var messages = new List<MediumMessage>();
                    while (await reader.ReadAsync().AnyContext())
                    {
                        var content = reader.GetString(1);

                        messages.Add(
                            new MediumMessage
                            {
                                DbId = reader.GetInt64(0).ToString(),
                                Origin = serializer.Deserialize(content)!,
                                Content = content,
                                Retries = reader.GetInt32(2),
                                Added = reader.GetDateTime(3),
                            }
                        );
                    }

                    return messages;
                },
                sqlParams: sqlParams
            )
            .AnyContext();

        return result;
    }
}
