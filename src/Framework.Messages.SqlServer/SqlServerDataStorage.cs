// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Framework.Abstractions;
using Framework.Messages.Configuration;
using Framework.Messages.Internal;
using Framework.Messages.Messages;
using Framework.Messages.Monitoring;
using Framework.Messages.Persistence;
using Framework.Messages.Serialization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace Framework.Messages;

public class SqlServerDataStorage(
    IOptions<MessagingOptions> messagingOptions,
    IOptions<SqlServerOptions> options,
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
            $"UPDATE {_lockName} SET [Instance]=@Instance,[LastLockTime]=@LastLockTime WHERE [Key]=@Key AND [LastLockTime] < @TTL;";
        var connection = new SqlConnection(options.Value.ConnectionString);
        await using var _ = connection;
        object[] sqlParams =
        [
            new SqlParameter("@Instance", instance),
            new SqlParameter("@LastLockTime", timeProvider.GetUtcNow().UtcDateTime),
            new SqlParameter("@Key", key),
            new SqlParameter("@TTL", timeProvider.GetUtcNow().UtcDateTime.Subtract(ttl)),
        ];
        var opResult = await connection.ExecuteNonQueryAsync(sql, sqlParams: sqlParams).AnyContext();
        return opResult > 0;
    }

    public async Task ReleaseLockAsync(string key, string instance, CancellationToken cancellationToken = default)
    {
        var sql =
            $"UPDATE {_lockName} SET [Instance]='',[LastLockTime]=@LastLockTime WHERE [Key]=@Key AND [Instance]=@Instance;";
        var connection = new SqlConnection(options.Value.ConnectionString);
        await using var _ = connection;
        object[] sqlParams =
        [
            new SqlParameter("@Instance", instance),
            new SqlParameter("@LastLockTime", DateTime.MinValue) { SqlDbType = SqlDbType.DateTime2 },
            new SqlParameter("@Key", key),
        ];
        await connection.ExecuteNonQueryAsync(sql, sqlParams: sqlParams).AnyContext();
    }

    public async Task RenewLockAsync(string key, TimeSpan ttl, string instance, CancellationToken token = default)
    {
        var sql =
            $"UPDATE {_lockName} SET [LastLockTime]=DATEADD(s,{ttl.TotalSeconds},[LastLockTime]) WHERE [Key]=@Key AND [Instance]=@Instance;";
        var connection = new SqlConnection(options.Value.ConnectionString);
        await using var _ = connection;
        object[] sqlParams = [new SqlParameter("@Key", key), new SqlParameter("@Instance", instance)];
        await connection.ExecuteNonQueryAsync(sql, sqlParams: sqlParams).AnyContext();
    }

    public async Task ChangePublishStateToDelayedAsync(string[] ids)
    {
        var sql = $"UPDATE {_pubName} SET [StatusName]='{StatusName.Delayed}' WHERE [Id] IN ({string.Join(',', ids)});";
        var connection = new SqlConnection(options.Value.ConnectionString);
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
            $"INSERT INTO {_pubName} ([Id],[Version],[Name],[Content],[Retries],[Added],[ExpiresAt],[StatusName])"
            + $"VALUES(@Id,'{options.Value.Version}',@Name,@Content,@Retries,@Added,@ExpiresAt,@StatusName);";

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
            new SqlParameter("@Id", message.DbId),
            new SqlParameter("@Name", name),
            new SqlParameter("@Content", message.Content),
            new SqlParameter("@Retries", message.Retries),
            new SqlParameter("@Added", message.Added),
            new SqlParameter("@ExpiresAt", message.ExpiresAt.HasValue ? message.ExpiresAt.Value : DBNull.Value),
            new SqlParameter("@StatusName", nameof(StatusName.Scheduled)),
        ];

        if (transaction == null)
        {
            var connection = new SqlConnection(options.Value.ConnectionString);
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

            var conn = dbTrans?.Connection;
            await conn!.ExecuteNonQueryAsync(sql, dbTrans, sqlParams).AnyContext();
        }

        return message;
    }

    public async Task StoreReceivedExceptionMessageAsync(string name, string group, string content)
    {
        object[] sqlParams =
        [
            new SqlParameter("@Id", longIdGenerator.Create().ToString(CultureInfo.InvariantCulture)),
            new SqlParameter("@Name", name),
            new SqlParameter("@Group", group),
            new SqlParameter("@Content", content),
            new SqlParameter("@Retries", messagingOptions.Value.FailedRetryCount),
            new SqlParameter("@Added", timeProvider.GetUtcNow().UtcDateTime),
            new SqlParameter(
                "@ExpiresAt",
                timeProvider.GetUtcNow().UtcDateTime.AddSeconds(messagingOptions.Value.FailedMessageExpiredAfter)
            ),
            new SqlParameter("@StatusName", nameof(StatusName.Failed)),
            new SqlParameter("@MessageId", serializer.Deserialize(content)!.GetId()),
            new SqlParameter("@Version", messagingOptions.Value.Version),
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
            new SqlParameter("@Id", mediumMessage.DbId),
            new SqlParameter("@Name", name),
            new SqlParameter("@Group", group),
            new SqlParameter("@Content", mediumMessage.Content),
            new SqlParameter("@Retries", mediumMessage.Retries),
            new SqlParameter("@Added", mediumMessage.Added),
            new SqlParameter(
                "@ExpiresAt",
                mediumMessage.ExpiresAt.HasValue ? mediumMessage.ExpiresAt.Value : DBNull.Value
            ),
            new SqlParameter("@StatusName", nameof(StatusName.Scheduled)),
            new SqlParameter("@MessageId", message.GetId()),
            new SqlParameter("@Version", messagingOptions.Value.Version),
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
        var connection = new SqlConnection(options.Value.ConnectionString);
        await using var _ = connection;

        return await connection
            .ExecuteNonQueryAsync(
                $@"DELETE FROM {table}
               WHERE Id IN (
                   SELECT TOP (@batchCount) Id
                   FROM {table} WITH (READPAST)
                   WHERE ExpiresAt < @timeout
                   AND StatusName IN('{StatusName.Succeeded}','{StatusName.Failed}')
               );",
                null,
                new SqlParameter("@timeout", timeout),
                new SqlParameter("@batchCount", batchCount)
            )
            .AnyContext();
    }

    public Task<IEnumerable<MediumMessage>> GetPublishedMessagesOfNeedRetry(TimeSpan lookbackSeconds)
    {
        return _GetMessagesOfNeedRetryAsync(_pubName, lookbackSeconds);
    }

    public Task<IEnumerable<MediumMessage>> GetReceivedMessagesOfNeedRetry(TimeSpan lookbackSeconds)
    {
        return _GetMessagesOfNeedRetryAsync(_recName, lookbackSeconds);
    }

    public async Task<int> DeleteReceivedMessageAsync(long id)
    {
        var sql = $"DELETE FROM {_recName} WHERE Id={id}";

        var connection = new SqlConnection(options.Value.ConnectionString);
        await using var _ = connection;
        var affectedRowCount = await connection.ExecuteNonQueryAsync(sql).AnyContext();
        return affectedRowCount;
    }

    public async Task<int> DeletePublishedMessageAsync(long id)
    {
        var sql = $"DELETE FROM {_pubName} WHERE Id={id}";

        var connection = new SqlConnection(options.Value.ConnectionString);
        await using var _ = connection;
        var affectedRowCount = await connection.ExecuteNonQueryAsync(sql).AnyContext();
        return affectedRowCount;
    }

    public async Task ScheduleMessagesOfDelayedAsync(
        Func<object, IEnumerable<MediumMessage>, Task> scheduleTask,
        CancellationToken token = default
    )
    {
        var sql =
            $@"
            SELECT TOP (@BatchSize) Id, Content, Retries, Added, ExpiresAt FROM {_pubName} WITH (UPDLOCK, READPAST)
                WHERE Version = @Version AND StatusName = '{StatusName.Delayed}' AND ExpiresAt < @TwoMinutesLater
            UNION ALL
            SELECT TOP (@BatchSize) Id, Content, Retries, Added, ExpiresAt FROM {_pubName} WITH (UPDLOCK, READPAST)
                WHERE Version = @Version AND StatusName = '{StatusName.Queued}' AND ExpiresAt < @OneMinutesAgo;";

        object[] sqlParams =
        [
            new SqlParameter("@Version", messagingOptions.Value.Version),
            new SqlParameter("@TwoMinutesLater", timeProvider.GetUtcNow().UtcDateTime.AddMinutes(2)),
            new SqlParameter("@OneMinutesAgo", timeProvider.GetUtcNow().UtcDateTime.AddMinutes(-1)),
            new SqlParameter("@BatchSize", messagingOptions.Value.SchedulerBatchSize),
        ];

        await using var connection = new SqlConnection(options.Value.ConnectionString);
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
        return new SqlServerMonitoringApi(options, initializer, serializer, timeProvider);
    }

    private async Task _ChangeMessageStateAsync(
        string tableName,
        MediumMessage message,
        StatusName state,
        object? transaction = null
    )
    {
        var sql =
            $"UPDATE {tableName} SET Content=@Content, Retries=@Retries,ExpiresAt=@ExpiresAt,StatusName=@StatusName WHERE Id=@Id";

        object[] sqlParams =
        [
            new SqlParameter("@Id", message.DbId),
            new SqlParameter("@Content", serializer.Serialize(message.Origin)),
            new SqlParameter("@Retries", message.Retries),
            new SqlParameter("@ExpiresAt", message.ExpiresAt),
            new SqlParameter("@StatusName", state.ToString("G")),
        ];

        if (transaction is DbTransaction dbTransaction)
        {
            var connection = (SqlConnection)dbTransaction.Connection!;
            await connection.ExecuteNonQueryAsync(sql, dbTransaction, sqlParams).AnyContext();
        }
        else
        {
            var connection = new SqlConnection(options.Value.ConnectionString);
            await using var _ = connection;
            await connection.ExecuteNonQueryAsync(sql, sqlParams: sqlParams).AnyContext();
        }
    }

    private async Task _StoreReceivedMessage(object[] sqlParams)
    {
        var sql = $"""
            MERGE {_recName} AS target
            USING (SELECT @MessageId AS MessageId, @Group AS [Group]) AS source
            ON target.MessageId = source.MessageId AND target.[Group] = source.[Group]
            WHEN MATCHED THEN
                UPDATE SET StatusName = @StatusName, Retries = @Retries, ExpiresAt = @ExpiresAt, Content = @Content
            WHEN NOT MATCHED THEN
                INSERT ([Id],[Version],[Name],[Group],[Content],[Retries],[Added],[ExpiresAt],[StatusName],[MessageId])
                VALUES (@Id,@Version,@Name,@Group,@Content,@Retries,@Added,@ExpiresAt,@StatusName,@MessageId);
            """;

        var connection = new SqlConnection(options.Value.ConnectionString);
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
            $"SELECT TOP (200) Id, Content, Retries, Added FROM {tableName} WITH (READPAST) "
            + $"WHERE Retries < @Retries AND Version = @Version AND Added < @Added AND StatusName IN ('{StatusName.Failed}', '{StatusName.Scheduled}');";

        object[] sqlParams =
        [
            new SqlParameter("@Retries", messagingOptions.Value.FailedRetryCount),
            new SqlParameter("@Version", messagingOptions.Value.Version),
            new SqlParameter("@Added", fourMinAgo),
        ];

        var connection = new SqlConnection(options.Value.ConnectionString);
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
                                DbId = reader.GetInt64(0).ToString(CultureInfo.InvariantCulture),
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
