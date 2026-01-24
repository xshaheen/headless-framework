// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Framework.Abstractions;
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

    private readonly string _lockName = initializer.GetLockTableName();
    private readonly string _pubName = initializer.GetPublishedTableName();
    private readonly string _recName = initializer.GetReceivedTableName();

    public async ValueTask<bool> AcquireLockAsync(
        string key,
        TimeSpan ttl,
        string instance,
        CancellationToken cancellationToken = default
    )
    {
        var sql =
            $"UPDATE {_lockName} SET [Instance]=@Instance,[LastLockTime]=@LastLockTime WHERE [Key]=@Key AND [LastLockTime] < @TTL;";
        await using var connection = new SqlConnection(options.Value.ConnectionString);
        object[] sqlParams =
        [
            new SqlParameter("@Instance", instance),
            new SqlParameter("@LastLockTime", timeProvider.GetUtcNow().UtcDateTime),
            new SqlParameter("@Key", key),
            new SqlParameter("@TTL", timeProvider.GetUtcNow().UtcDateTime.Subtract(ttl)),
        ];
        var opResult = await connection
            .ExecuteNonQueryAsync(sql, cancellationToken: cancellationToken, sqlParams: sqlParams)
            .AnyContext();
        return opResult > 0;
    }

    public async ValueTask ReleaseLockAsync(string key, string instance, CancellationToken cancellationToken = default)
    {
        var sql =
            $"UPDATE {_lockName} SET [Instance]='',[LastLockTime]=@LastLockTime WHERE [Key]=@Key AND [Instance]=@Instance;";
        await using var connection = new SqlConnection(options.Value.ConnectionString);
        object[] sqlParams =
        [
            new SqlParameter("@Instance", instance),
            new SqlParameter("@LastLockTime", DateTime.MinValue) { SqlDbType = SqlDbType.DateTime2 },
            new SqlParameter("@Key", key),
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
            $"UPDATE {_lockName} SET [LastLockTime]=DATEADD(s,{ttl.TotalSeconds},[LastLockTime]) WHERE [Key]=@Key AND [Instance]=@Instance;";
        await using var connection = new SqlConnection(options.Value.ConnectionString);
        object[] sqlParams = [new SqlParameter("@Key", key), new SqlParameter("@Instance", instance)];
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
            parameters[i] = new SqlParameter($"@Id{i}", long.Parse(ids[i], CultureInfo.InvariantCulture));
        }

        parameters[^1] = new SqlParameter("@StatusName", nameof(StatusName.Delayed));

        var sql = $"UPDATE {_pubName} SET [StatusName]=@StatusName WHERE [Id] IN ({string.Join(',', paramNames)});";

        await using var connection = new SqlConnection(options.Value.ConnectionString);
        await connection
            .ExecuteNonQueryAsync(sql, cancellationToken: cancellationToken, sqlParams: parameters)
            .AnyContext();
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
            await using var connection = new SqlConnection(options.Value.ConnectionString);
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

            var conn = dbTrans?.Connection;
            await conn!.ExecuteNonQueryAsync(sql, dbTrans, cancellationToken, sqlParams).AnyContext();
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
        await using var connection = new SqlConnection(options.Value.ConnectionString);

        return await connection
            .ExecuteNonQueryAsync(
                $@"DELETE FROM {table}
               WHERE Id IN (
                   SELECT TOP (@batchCount) Id
                   FROM {table} WITH (READPAST)
                   WHERE ExpiresAt < @timeout
                   AND StatusName IN('{nameof(StatusName.Succeeded)}','{nameof(StatusName.Failed)}')
               );",
                null,
                cancellationToken,
                new SqlParameter("@timeout", timeout),
                new SqlParameter("@batchCount", batchCount)
            )
            .AnyContext();
    }

    public ValueTask<IEnumerable<MediumMessage>> GetPublishedMessagesOfNeedRetry(
        TimeSpan lookbackSeconds,
        CancellationToken cancellationToken = default
    )
    {
        return _GetMessagesOfNeedRetryAsync(_pubName, lookbackSeconds, cancellationToken);
    }

    public ValueTask<IEnumerable<MediumMessage>> GetReceivedMessagesOfNeedRetry(
        TimeSpan lookbackSeconds,
        CancellationToken cancellationToken = default
    )
    {
        return _GetMessagesOfNeedRetryAsync(_recName, lookbackSeconds, cancellationToken);
    }

    public async ValueTask<int> DeleteReceivedMessageAsync(long id, CancellationToken cancellationToken = default)
    {
        var sql = $"DELETE FROM {_recName} WHERE Id={id}";

        await using var connection = new SqlConnection(options.Value.ConnectionString);
        var affectedRowCount = await connection
            .ExecuteNonQueryAsync(sql, cancellationToken: cancellationToken)
            .AnyContext();
        return affectedRowCount;
    }

    public async ValueTask<int> DeletePublishedMessageAsync(long id, CancellationToken cancellationToken = default)
    {
        var sql = $"DELETE FROM {_pubName} WHERE Id={id}";

        await using var connection = new SqlConnection(options.Value.ConnectionString);
        var affectedRowCount = await connection
            .ExecuteNonQueryAsync(sql, cancellationToken: cancellationToken)
            .AnyContext();
        return affectedRowCount;
    }

    public async ValueTask ScheduleMessagesOfDelayedAsync(
        Func<object, IEnumerable<MediumMessage>, ValueTask> scheduleTask,
        CancellationToken cancellationToken = default
    )
    {
        var sql =
            $@"
            SELECT TOP (@BatchSize) Id, Content, Retries, Added, ExpiresAt FROM {_pubName} WITH (UPDLOCK, READPAST)
                WHERE Version = @Version AND StatusName = '{nameof(StatusName.Delayed)}' AND ExpiresAt < @TwoMinutesLater
            UNION ALL
            SELECT TOP (@BatchSize) Id, Content, Retries, Added, ExpiresAt FROM {_pubName} WITH (UPDLOCK, READPAST)
                WHERE Version = @Version AND StatusName = '{nameof(StatusName.Queued)}' AND ExpiresAt < @OneMinutesAgo;";

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
                    while (await reader.ReadAsync(ct).AnyContext())
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
                cancellationToken,
                sqlParams
            )
            .AnyContext();

        await scheduleTask(transaction, messageList);

        await transaction.CommitAsync(cancellationToken);
    }

    public IMonitoringApi GetMonitoringApi()
    {
        return new SqlServerMonitoringApi(options, initializer, serializer, timeProvider);
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
            await connection.ExecuteNonQueryAsync(sql, dbTransaction, cancellationToken, sqlParams).AnyContext();
        }
        else
        {
            await using var connection = new SqlConnection(options.Value.ConnectionString);
            await connection
                .ExecuteNonQueryAsync(sql, cancellationToken: cancellationToken, sqlParams: sqlParams)
                .AnyContext();
        }
    }

    private async ValueTask _StoreReceivedMessage(object[] sqlParams, CancellationToken cancellationToken = default)
    {
        var sql = $"""
            MERGE {_recName} WITH (HOLDLOCK) AS target
            USING (SELECT @MessageId AS MessageId, @Group AS [Group]) AS source
            ON target.MessageId = source.MessageId AND (target.[Group] = source.[Group] OR (target.[Group] IS NULL AND source.[Group] IS NULL))
            WHEN MATCHED THEN
                UPDATE SET StatusName = @StatusName, Retries = @Retries, ExpiresAt = @ExpiresAt, Content = @Content
            WHEN NOT MATCHED THEN
                INSERT ([Id],[Version],[Name],[Group],[Content],[Retries],[Added],[ExpiresAt],[StatusName],[MessageId])
                VALUES (@Id,@Version,@Name,@Group,@Content,@Retries,@Added,@ExpiresAt,@StatusName,@MessageId);
            """;

        await using var connection = new SqlConnection(options.Value.ConnectionString);
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
            $"SELECT TOP ({_RetryBatchSize}) Id, Content, Retries, Added FROM {tableName} WITH (READPAST) "
            + $"WHERE Retries < @Retries AND Version = @Version AND Added < @Added AND StatusName IN ('{nameof(StatusName.Failed)}', '{nameof(StatusName.Scheduled)}');";

        object[] sqlParams =
        [
            new SqlParameter("@Retries", messagingOptions.Value.FailedRetryCount),
            new SqlParameter("@Version", messagingOptions.Value.Version),
            new SqlParameter("@Added", fourMinAgo),
        ];

        await using var connection = new SqlConnection(options.Value.ConnectionString);
        var result = await connection
            .ExecuteReaderAsync(
                sql,
                async (reader, ct) =>
                {
                    var messages = new List<MediumMessage>();
                    while (await reader.ReadAsync(ct).AnyContext())
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
                cancellationToken: cancellationToken,
                sqlParams: sqlParams
            )
            .AnyContext();

        return result;
    }
}
