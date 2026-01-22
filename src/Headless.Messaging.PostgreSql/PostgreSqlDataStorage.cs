// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Framework.Abstractions;
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
            $"UPDATE {_lockName} SET \"Instance\"=@Instance,\"LastLockTime\"=@LastLockTime WHERE \"Key\"=@Key AND \"LastLockTime\" < @TTL;";
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
            .AnyContext();

        return opResult > 0;
    }

    public async ValueTask ReleaseLockAsync(string key, string instance, CancellationToken cancellationToken = default)
    {
        var sql =
            $"UPDATE {_lockName} SET \"Instance\"='',\"LastLockTime\"=@LastLockTime WHERE \"Key\"=@Key AND \"Instance\"=@Instance;";

        await using var connection = postgreSqlOptions.Value.CreateConnection();

        object[] sqlParams =
        [
            new NpgsqlParameter("@Instance", instance),
            new NpgsqlParameter("@LastLockTime", DateTime.MinValue),
            new NpgsqlParameter("@Key", key),
        ];

        await connection
            .ExecuteNonQueryAsync(sql, cancellationToken: cancellationToken, sqlParams: sqlParams)
            .AnyContext();
    }

    public async ValueTask RenewLockAsync(
        string key,
        TimeSpan ttl,
        string instance,
        CancellationToken cancellationToken = default
    )
    {
        var sql =
            $"UPDATE {_lockName} SET \"LastLockTime\"=\"LastLockTime\"+interval '{ttl.TotalSeconds}' second WHERE \"Key\"=@Key AND \"Instance\"=@Instance;";

        await using var connection = postgreSqlOptions.Value.CreateConnection();

        object[] sqlParams = [new NpgsqlParameter("@Instance", instance), new NpgsqlParameter("@Key", key)];

        await connection
            .ExecuteNonQueryAsync(sql, cancellationToken: cancellationToken, sqlParams: sqlParams)
            .AnyContext();
    }

    public async ValueTask ChangePublishStateToDelayedAsync(string[] ids, CancellationToken cancellationToken = default)
    {
        if (ids.Length == 0)
        {
            return;
        }

        var parameters = new object[ids.Length + 1];
        var paramNames = new string[ids.Length];

        for (var i = 0; i < ids.Length; i++)
        {
            paramNames[i] = $"@Id{i}";
            parameters[i] = new NpgsqlParameter($"@Id{i}", long.Parse(ids[i], CultureInfo.InvariantCulture));
        }

        parameters[^1] = new NpgsqlParameter("@StatusName", nameof(StatusName.Delayed));

        var sql = $"UPDATE {_pubName} SET \"StatusName\"=@StatusName WHERE \"Id\" IN ({string.Join(',', paramNames)});";

        await using var connection = postgreSqlOptions.Value.CreateConnection();

        await connection.ExecuteNonQueryAsync(sql, cancellationToken: cancellationToken, sqlParams: parameters).AnyContext();
    }

    public ValueTask ChangePublishStateAsync(
        MediumMessage message,
        StatusName state,
        object? transaction = null,
        CancellationToken cancellationToken = default
    )
    {
        return _ChangeMessageStateAsync(_pubName, message, state, transaction, cancellationToken);
    }

    public ValueTask ChangeReceiveStateAsync(
        MediumMessage message,
        StatusName state,
        CancellationToken cancellationToken = default
    )
    {
        return _ChangeMessageStateAsync(_recName, message, state, cancellationToken: cancellationToken);
    }

    public async ValueTask<MediumMessage> StoreMessageAsync(
        string name,
        Message content,
        object? transaction = null,
        CancellationToken cancellationToken = default
    )
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
            await using var connection = postgreSqlOptions.Value.CreateConnection();
            await connection
                .ExecuteNonQueryAsync(sql, cancellationToken: cancellationToken, sqlParams: sqlParams)
                .AnyContext();
        }
        else
        {
            var dbTrans = transaction as DbTransaction;
            if (dbTrans == null && transaction is IDbContextTransaction dbContextTrans)
            {
                dbTrans = dbContextTrans.GetDbTransaction();
            }

            var conn = dbTrans?.Connection!;
            await conn.ExecuteNonQueryAsync(sql, dbTrans, cancellationToken, sqlParams).AnyContext();
        }

        return message;
    }

    public async ValueTask StoreReceivedExceptionMessageAsync(
        string name,
        string group,
        string content,
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
        ];

        await _StoreReceivedMessage(sqlParams, cancellationToken).AnyContext();
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

        await _StoreReceivedMessage(sqlParams, cancellationToken).AnyContext();

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

        object[] sqlParams =
        [
            new NpgsqlParameter("@timeout", timeout),
            new NpgsqlParameter("@batchCount", batchCount),
        ];

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
            .AnyContext();
    }

    public async ValueTask<IEnumerable<MediumMessage>> GetPublishedMessagesOfNeedRetry(
        TimeSpan lookbackSeconds,
        CancellationToken cancellationToken = default
    )
    {
        return await _GetMessagesOfNeedRetryAsync(_pubName, lookbackSeconds, cancellationToken).AnyContext();
    }

    public async ValueTask<IEnumerable<MediumMessage>> GetReceivedMessagesOfNeedRetry(
        TimeSpan lookbackSeconds,
        CancellationToken cancellationToken = default
    )
    {
        return await _GetMessagesOfNeedRetryAsync(_recName, lookbackSeconds, cancellationToken).AnyContext();
    }

    public async ValueTask<int> DeleteReceivedMessageAsync(long id, CancellationToken cancellationToken = default)
    {
        var sql = $"""DELETE FROM {_recName} WHERE "Id"={id.ToString(CultureInfo.InvariantCulture)}""";

        await using var connection = postgreSqlOptions.Value.CreateConnection();
        var result = await connection.ExecuteNonQueryAsync(sql, cancellationToken: cancellationToken);
        return result;
    }

    public async ValueTask<int> DeletePublishedMessageAsync(long id, CancellationToken cancellationToken = default)
    {
        var sql = $"""DELETE FROM {_pubName} WHERE "Id"={id.ToString(CultureInfo.InvariantCulture)}""";

        await using var connection = postgreSqlOptions.Value.CreateConnection();
        var result = await connection.ExecuteNonQueryAsync(sql, cancellationToken: cancellationToken);
        return result;
    }

    public async ValueTask ScheduleMessagesOfDelayedAsync(
        Func<object, IEnumerable<MediumMessage>, ValueTask> scheduleTask,
        CancellationToken cancellationToken = default
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
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var messageList = await connection
            .ExecuteReaderAsync(
                sql,
                async (reader, token) =>
                {
                    var messages = new List<MediumMessage>();
                    while (await reader.ReadAsync(token).AnyContext())
                    {
                        var content = reader.GetString(1);

                        var mediumMessage = new MediumMessage
                        {
                            DbId = reader.GetInt64(0).ToString(CultureInfo.InvariantCulture),
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
            .AnyContext();

        await scheduleTask(transaction, messageList);

        await transaction.CommitAsync(cancellationToken);
    }

    private DateTime _QueuedMessageFetchTime()
    {
        return timeProvider.GetUtcNow().UtcDateTime.AddMinutes(-1);
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
            new NpgsqlParameter("@Id", long.Parse(message.DbId, CultureInfo.InvariantCulture)),
            new NpgsqlParameter("@Content", serializer.Serialize(message.Origin)),
            new NpgsqlParameter("@Retries", message.Retries),
            new NpgsqlParameter("@ExpiresAt", message.ExpiresAt.HasValue ? message.ExpiresAt.Value : DBNull.Value),
            new NpgsqlParameter("@StatusName", state.ToString("G")),
        ];

        if (transaction is DbTransaction dbTransaction)
        {
            var connection = (NpgsqlConnection)dbTransaction.Connection!;
            await connection.ExecuteNonQueryAsync(sql, dbTransaction, cancellationToken, sqlParams).AnyContext();
        }
        else
        {
            await using var connection = postgreSqlOptions.Value.CreateConnection();

            await connection
                .ExecuteNonQueryAsync(sql, cancellationToken: cancellationToken, sqlParams: sqlParams)
                .AnyContext();
        }
    }

    private async ValueTask _StoreReceivedMessage(object[] sqlParams, CancellationToken cancellationToken = default)
    {
        var sql =
            $"INSERT INTO {_recName}(\"Id\",\"Version\",\"Name\",\"Group\",\"Content\",\"Retries\",\"Added\",\"ExpiresAt\",\"StatusName\")"
            + $"VALUES(@Id,'{messagingOptions.Value.Version}',@Name,@Group,@Content,@Retries,@Added,@ExpiresAt,@StatusName) RETURNING \"Id\";";

        await using var connection = postgreSqlOptions.Value.CreateConnection();

        await connection
            .ExecuteNonQueryAsync(sql, cancellationToken: cancellationToken, sqlParams: sqlParams)
            .AnyContext();
    }

    private async ValueTask<IEnumerable<MediumMessage>> _GetMessagesOfNeedRetryAsync(
        string tableName,
        TimeSpan lookbackSeconds,
        CancellationToken cancellationToken = default
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

        await using var connection = postgreSqlOptions.Value.CreateConnection();

        var result = await connection
            .ExecuteReaderAsync(
                sql,
                async (reader, token) =>
                {
                    var messages = new List<MediumMessage>();
                    while (await reader.ReadAsync(token).AnyContext())
                    {
                        var content = reader.GetString(1);

                        var mediumMessage = new MediumMessage
                        {
                            DbId = reader.GetInt64(0).ToString(CultureInfo.InvariantCulture),
                            Origin = serializer.Deserialize(content)!,
                            Content = content,
                            Retries = reader.GetInt32(2),
                            Added = reader.GetDateTime(3),
                        };

                        messages.Add(mediumMessage);
                    }

                    return messages;
                },
                cancellationToken: cancellationToken,
                sqlParams: sqlParams
            )
            .AnyContext();

        return result;
    }
}
