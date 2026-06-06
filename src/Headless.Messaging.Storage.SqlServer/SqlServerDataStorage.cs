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

namespace Headless.Messaging.Storage.SqlServer;

/// <summary>
/// SQL Server implementation of <see cref="IDataStorage"/> for message persistence.
/// Handles storage, retrieval, and state transitions for published and received messages.
/// </summary>
public sealed class SqlServerDataStorage(
    IOptions<MessagingOptions> messagingOptions,
    IOptions<SqlServerOptions> options,
    IStorageInitializer initializer,
    ISerializer serializer,
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
        "NOT (StatusName IN ('Succeeded','Failed') AND NextRetryAt IS NULL) AND (@OriginalRetries IS NULL OR Retries=@OriginalRetries)";

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

    public async ValueTask ChangePublishStateToDelayedAsync(
        Guid[] storageIds,
        CancellationToken cancellationToken = default
    )
    {
        if (storageIds.Length == 0)
        {
            return;
        }

        var schema = options.Value.Schema;
        var tvpTypeName = $"[{schema}].[HeadlessMessagingIdList]";

        var idsTable = new DataTable();
        idsTable.Columns.Add("Id", typeof(Guid));
        foreach (var id in storageIds)
        {
            idsTable.Rows.Add(id);
        }

        var tvpParam = new SqlParameter("@Ids", SqlDbType.Structured) { TypeName = tvpTypeName, Value = idsTable };
        var statusParam = new SqlParameter("@StatusName", nameof(StatusName.Delayed));

        var sql = $"UPDATE {_publishedTable} SET [StatusName]=@StatusName WHERE [Id] IN (SELECT [Id] FROM @Ids);";

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
        var sql =
            // X1 terminal-row guard: refuses updates to rows that are already terminal AND
            // have NextRetryAt cleared. Failed rows with non-null NextRetryAt stay mutable so
            // the retry processor can rewrite them — see the matching note in PostgreSqlDataStorage.
            $"UPDATE {_receivedTable} SET Content=@Content, Retries=@Retries, ExpiresAt=@ExpiresAt, NextRetryAt=@NextRetryAt, LockedUntil=@LockedUntil, StatusName=@StatusName, ExceptionInfo=@ExceptionInfo WHERE Id=@Id AND {_TerminalRowGuardWithRetries}";

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
            new SqlParameter("@OriginalRetries", SqlDbType.Int) { Value = originalRetries ?? (object)DBNull.Value },
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
        MediumMessage message,
        object? transaction = null,
        CancellationToken cancellationToken = default
    )
    {
        var sql =
            $"INSERT INTO {_publishedTable} ([Id],[Version],[Name],[Content],[IntentType],[Retries],[Added],[ExpiresAt],[NextRetryAt],[LockedUntil],[StatusName],[MessageId])"
            + $"VALUES(@Id,'{messagingOptions.Value.Version}',@Name,@Content,@IntentType,@Retries,@Added,@ExpiresAt,@NextRetryAt,@LockedUntil,@StatusName,@MessageId);";

        var added = timeProvider.GetUtcNow().UtcDateTime;
        var stored = new MediumMessage
        {
            StorageId = Guid.NewGuid(),
            Origin = message.Origin,
            Content = serializer.Serialize(message.Origin),
            IntentType = message.IntentType,
            Added = added,
            ExpiresAt = null,
            NextRetryAt = added.Add(messagingOptions.Value.RetryPolicy.InitialDispatchGrace),
            LockedUntil = null,
            Retries = 0,
        };

        object[] sqlParams =
        [
            new SqlParameter("@Id", stored.StorageId),
            new SqlParameter("@Name", name),
            new SqlParameter("@Content", stored.Content),
            new SqlParameter("@IntentType", SqlDbType.SmallInt) { Value = (short)stored.IntentType },
            new SqlParameter("@Retries", stored.Retries),
            new SqlParameter("@Added", stored.Added),
            new SqlParameter("@ExpiresAt", SqlDbType.DateTime2)
            {
                Value = stored.ExpiresAt.HasValue ? stored.ExpiresAt.Value : DBNull.Value,
            },
            new SqlParameter("@NextRetryAt", SqlDbType.DateTime2) { Value = stored.NextRetryAt.ToUtcParameterValue() },
            new SqlParameter("@LockedUntil", SqlDbType.DateTime2) { Value = stored.LockedUntil.ToUtcParameterValue() },
            new SqlParameter("@StatusName", nameof(StatusName.Scheduled)),
            new SqlParameter("@MessageId", message.Origin.GetId()),
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

        return stored;
    }

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
            new SqlParameter("@Id", Guid.NewGuid()),
            new SqlParameter("@Name", name),
            new SqlParameter("@Group", SqlDbType.NVarChar, 200) { Value = (object?)group ?? DBNull.Value },
            new SqlParameter(
                "@Content",
                string.IsNullOrEmpty(message.Content) ? serializer.Serialize(message.Origin) : message.Content
            ),
            new SqlParameter("@IntentType", SqlDbType.SmallInt) { Value = (short)message.IntentType },
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
            new SqlParameter("@MessageId", message.Origin.GetId()),
            new SqlParameter("@Version", messagingOptions.Value.Version),
            new SqlParameter("@ExceptionInfo", exceptionInfo ?? (object)DBNull.Value),
        ];

        return await _StoreReceivedMessage(sqlParams, cancellationToken).ConfigureAwait(false);
    }

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
            StorageId = Guid.NewGuid(),
            Origin = message.Origin,
            Content = serializer.Serialize(message.Origin),
            IntentType = message.IntentType,
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
            new SqlParameter("@Group", SqlDbType.NVarChar, 200) { Value = (object?)group ?? DBNull.Value },
            new SqlParameter("@Content", mediumMessage.Content),
            new SqlParameter("@IntentType", SqlDbType.SmallInt) { Value = (short)mediumMessage.IntentType },
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
            new SqlParameter("@MessageId", message.Origin.GetId()),
            new SqlParameter("@Version", messagingOptions.Value.Version),
            new SqlParameter("@ExceptionInfo", DBNull.Value),
        ];

        await _StoreReceivedMessage(sqlParams, cancellationToken).ConfigureAwait(false);

        return mediumMessage;
    }

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

    public async ValueTask<int> DeleteReceivedMessagesAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken = default
    )
    {
        if (ids.Count == 0)
        {
            return 0;
        }

        var paramNames = new string[ids.Count];
        var sqlParams = new object[ids.Count];
        for (var i = 0; i < ids.Count; i++)
        {
            paramNames[i] = $"@Id{i}";
            sqlParams[i] = new SqlParameter($"@Id{i}", ids[i]);
        }

        var sql = $"DELETE FROM {_receivedTable} WHERE Id IN ({string.Join(',', paramNames)})";

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

    public async ValueTask<int> DeletePublishedMessagesAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken = default
    )
    {
        if (ids.Count == 0)
        {
            return 0;
        }

        var paramNames = new string[ids.Count];
        var sqlParams = new object[ids.Count];
        for (var i = 0; i < ids.Count; i++)
        {
            paramNames[i] = $"@Id{i}";
            sqlParams[i] = new SqlParameter($"@Id{i}", ids[i]);
        }

        var sql = $"DELETE FROM {_publishedTable} WHERE Id IN ({string.Join(',', paramNames)})";

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

    public async ValueTask ScheduleMessagesOfDelayedAsync(
        Func<object?, IEnumerable<MediumMessage>, ValueTask> scheduleTask,
        CancellationToken cancellationToken = default
    )
    {
        var sql = $"""
            SELECT TOP (@BatchSize) Id, Content, IntentType, Retries, Added, ExpiresAt FROM {_publishedTable} WITH (UPDLOCK, READPAST)
            WHERE Version = @Version AND StatusName = '{nameof(StatusName.Delayed)}' AND ExpiresAt < @TwoMinutesLater
            UNION ALL
            SELECT TOP (@BatchSize) Id, Content, IntentType, Retries, Added, ExpiresAt FROM {_publishedTable} WITH (UPDLOCK, READPAST)
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
                                StorageId = reader.GetGuid(0),
                                Origin = serializer.Deserialize(content)!,
                                Content = content,
                                IntentType = (IntentType)reader.GetInt16(2),
                                Retries = reader.GetInt32(3),
                                Added = reader.GetDateTime(4),
                                ExpiresAt = reader.GetDateTime(5),
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
            $"UPDATE {tableName} SET Content=@Content, Retries=@Retries,ExpiresAt=@ExpiresAt,NextRetryAt=@NextRetryAt,LockedUntil=@LockedUntil,StatusName=@StatusName WHERE Id=@Id AND {_TerminalRowGuardWithRetries}";

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
            new SqlParameter("@OriginalRetries", SqlDbType.Int) { Value = originalRetries ?? (object)DBNull.Value },
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
        //
        // #9 — narrow the WHEN MATCHED predicate to additionally skip rows whose lease is still
        // active (LockedUntil in the future). A redelivered message that arrives while the row
        // is being dispatched would otherwise overwrite LockedUntil = NULL and Retries = 0,
        // releasing the active pickup lease mid-attempt and causing the retry processor to
        // re-pick the row while the inline retry loop is still in flight.
        var sql = $"""
            MERGE {_receivedTable} WITH (HOLDLOCK) AS target
            USING (SELECT @Version AS Version, @MessageId AS MessageId, @Group AS [Group], @IntentType AS IntentType) AS source
            ON target.Version = source.Version AND target.MessageId = source.MessageId AND (target.[Group] = source.[Group] OR (target.[Group] IS NULL AND source.[Group] IS NULL)) AND target.IntentType = source.IntentType
            WHEN MATCHED
                AND NOT (target.StatusName IN ('{nameof(StatusName.Succeeded)}','{nameof(
                    StatusName.Failed
                )}') AND target.NextRetryAt IS NULL)
                AND (target.LockedUntil IS NULL OR target.LockedUntil <= GETUTCDATE())
            THEN
                UPDATE SET StatusName = @StatusName, Retries = @Retries, ExpiresAt = @ExpiresAt, NextRetryAt = @NextRetryAt, LockedUntil = @LockedUntil, Content = @Content, ExceptionInfo = @ExceptionInfo
            WHEN NOT MATCHED THEN
                INSERT ([Id],[Version],[Name],[Group],[Content],[IntentType],[Retries],[Added],[ExpiresAt],[NextRetryAt],[LockedUntil],[StatusName],[MessageId],[ExceptionInfo])
                VALUES (@Id,@Version,@Name,@Group,@Content,@IntentType,@Retries,@Added,@ExpiresAt,@NextRetryAt,@LockedUntil,@StatusName,@MessageId,@ExceptionInfo);
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
        // #15 — explicit lease-contention predicate: only acquire the lease when the row is unleased
        // OR its existing lease has expired. Without this, two replicas racing on a fresh-from-broker
        // dispatch could both UPDATE LockedUntil and both believe they hold the lease (the SqlServer
        // and PostgreSql atomic-claim pickup paths already filter on LockedUntil, but the lease call
        // from the consume/publish path itself was unconditional). Returning false here surfaces the
        // contention to the inline retry loop, which skips dispatch.
        var sql =
            $"UPDATE {tableName} SET LockedUntil=@LockedUntil WHERE Id=@Id "
            + "AND (LockedUntil IS NULL OR LockedUntil <= @Now) "
            + $"AND {_TerminalRowGuardSimple}";

        object[] sqlParams =
        [
            new SqlParameter("@Id", message.StorageId),
            new SqlParameter("@LockedUntil", SqlDbType.DateTime2)
            {
                Value = ((DateTime?)lockedUntil).ToUtcParameterValue(),
            },
            new SqlParameter("@Now", SqlDbType.DateTime2) { Value = timeProvider.GetUtcNow().UtcDateTime },
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
        // Atomic claim-and-return: UPDATE TOP (N) ... OUTPUT inserted.* leases the rows (sets
        // LockedUntil) and returns the selected columns in one round-trip. Replaces the previous
        // two-step SELECT-UPDLOCK-then-Lease pattern, which committed the SELECT transaction
        // before _LeaseAsync wrote LockedUntil. In between, a concurrent replica could pass the
        // same "LockedUntil IS NULL" filter and lease the same row — double-dispatch.
        //
        // WITH (UPDLOCK, READPAST, ROWLOCK) on the UPDATE preserves the "skip rows another
        // replica is mid-claim on" behaviour. The UPDATE assigns @NewLease = now + DispatchTimeout
        // so subsequent pickup polls (anywhere) see the row as leased until the dispatch attempt
        // completes (or the lease expires).
        //
        // Use the injected TimeProvider rather than the DB server clock (GETUTCDATE()) so InMemory
        // and SQL providers share identical pickup semantics — keeps tests with a fake clock honest
        // and avoids subtle drift between application time and DB time.
        var sql = $"""
            UPDATE TOP (@BatchSize) {tableName} WITH (UPDLOCK, READPAST, ROWLOCK)
            SET LockedUntil = @NewLease
            OUTPUT inserted.Id, inserted.Content, inserted.IntentType, inserted.Retries, inserted.Added, inserted.NextRetryAt, inserted.LockedUntil
            WHERE Retries <= @Retries
              AND Version = @Version
              AND NextRetryAt IS NOT NULL AND NextRetryAt <= @Now
              AND (LockedUntil IS NULL OR LockedUntil <= @Now)
              AND {_TerminalRowGuardSimple};
            """;

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var newLease = now.Add(messagingOptions.Value.RetryPolicy.DispatchTimeout);

        object[] sqlParams =
        [
            new SqlParameter("@BatchSize", _RetryBatchSize),
            new SqlParameter("@Retries", messagingOptions.Value.RetryPolicy.MaxPersistedRetries),
            new SqlParameter("@Version", messagingOptions.Value.Version),
            new SqlParameter("@Now", SqlDbType.DateTime2) { Value = now },
            new SqlParameter("@NewLease", SqlDbType.DateTime2) { Value = newLease },
        ];

        await using var connection = new SqlConnection(options.Value.ConnectionString);

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
                                StorageId = reader.GetGuid(0),
                                Origin = serializer.Deserialize(content)!,
                                Content = content,
                                IntentType = (IntentType)reader.GetInt16(2),
                                Retries = reader.GetInt32(3),
                                Added = reader.GetDateTime(4),
                                NextRetryAt = await reader.IsDBNullAsync(5, ct).ConfigureAwait(false)
                                    ? null
                                    : reader.GetDateTime(5),
                                LockedUntil = await reader.IsDBNullAsync(6, ct).ConfigureAwait(false)
                                    ? null
                                    : reader.GetDateTime(6),
                            }
                        );
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
