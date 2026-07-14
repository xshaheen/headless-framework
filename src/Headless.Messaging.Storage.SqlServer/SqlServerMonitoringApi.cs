// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.Checks;
using Headless.Messaging.Configuration;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.Serialization;
using Headless.Primitives;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Storage.SqlServer;

/// <summary>
/// SQL Server implementation of <see cref="IMonitoringApi"/> for querying message statistics and history.
/// </summary>
internal sealed class SqlServerMonitoringApi(
    IOptions<SqlServerOptions> options,
    IOptions<MessagingOptions> messagingOptions,
    IStorageInitializer initializer,
    ISerializer serializer,
    TimeProvider timeProvider
) : IMonitoringApi
{
    private readonly SqlServerOptions _options = Argument.IsNotNull(options.Value);
    private readonly MessagingOptions _messagingOptions = messagingOptions.Value;
    private readonly string _publishedTable = initializer.GetPublishedTableName();
    private readonly string _receivedTable = initializer.GetReceivedTableName();

    /// <summary>
    /// Returns aggregate message counts broken down by status (succeeded, failed, delayed, pending retry)
    /// for both the published and received tables.
    /// </summary>
    public async ValueTask<StatisticsView> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var sql = $"""
            SELECT
                (SELECT COUNT_BIG(Id) FROM {_publishedTable} WHERE StatusName = N'Succeeded') AS PublishedSucceeded,
                (SELECT COUNT_BIG(Id) FROM {_receivedTable} WHERE StatusName = N'Succeeded') AS ReceivedSucceeded,
                (SELECT COUNT_BIG(Id) FROM {_publishedTable} WHERE StatusName = N'Failed') AS PublishedFailed,
                (SELECT COUNT_BIG(Id) FROM {_receivedTable} WHERE StatusName = N'Failed') AS ReceivedFailed,
                (SELECT COUNT_BIG(Id) FROM {_publishedTable} WHERE StatusName = N'Delayed') AS PublishedDelayed,
                (SELECT COUNT_BIG(Id) FROM {_publishedTable} WHERE NextRetryAt IS NOT NULL) AS PublishedPendingRetry,
                (SELECT COUNT_BIG(Id) FROM {_receivedTable} WHERE NextRetryAt IS NOT NULL) AS ReceivedPendingRetry;
            """;

        await using var connection = new SqlConnection(_options.ConnectionString);

        var statistics = await connection
            .ExecuteReaderAsync(
                sql,
                async (reader, ct) =>
                {
                    var statisticsDto = new StatisticsView();

                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        statisticsDto.PublishedSucceeded = reader.GetInt64(0);
                        statisticsDto.ReceivedSucceeded = reader.GetInt64(1);
                        statisticsDto.PublishedFailed = reader.GetInt64(2);
                        statisticsDto.ReceivedFailed = reader.GetInt64(3);
                        statisticsDto.PublishedDelayed = reader.GetInt64(4);
                        statisticsDto.PublishedPendingRetry = reader.GetInt64(5);
                        statisticsDto.ReceivedPendingRetry = reader.GetInt64(6);
                    }

                    return statisticsDto;
                },
                commandTimeout: _messagingOptions.CommandTimeout,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        return statistics;
    }

    /// <summary>
    /// Returns a dictionary of UTC hour buckets to <c>Failed</c> message counts for the past 24 hours,
    /// from the published or received table depending on <paramref name="type"/>.
    /// </summary>
    public async ValueTask<Dictionary<DateTime, int>> HourlyFailedJobs(
        MessageType type,
        CancellationToken cancellationToken = default
    )
    {
        var tableName = type == MessageType.Publish ? _publishedTable : _receivedTable;
        return await _GetHourlyTimelineStats(tableName, nameof(StatusName.Failed), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Returns a dictionary of UTC hour buckets to <c>Succeeded</c> message counts for the past 24 hours,
    /// from the published or received table depending on <paramref name="type"/>.
    /// </summary>
    public async ValueTask<Dictionary<DateTime, int>> HourlySucceededJobs(
        MessageType type,
        CancellationToken cancellationToken = default
    )
    {
        var tableName = type == MessageType.Publish ? _publishedTable : _receivedTable;
        return await _GetHourlyTimelineStats(tableName, nameof(StatusName.Succeeded), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Returns a paginated list of messages from either the published or received table,
    /// filtered by the criteria in <paramref name="query"/> (status, name, group, content substring, intent type).
    /// </summary>
    public async ValueTask<IndexPage<MessageView>> GetMessagesAsync(
        MessageQuery query,
        CancellationToken cancellationToken = default
    )
    {
        var tableName = query.MessageType == MessageType.Publish ? _publishedTable : _receivedTable;
        var selectColumns =
            query.MessageType == MessageType.Publish
                ? "[Id],[MessageId],[Version],[Name],CAST(NULL AS nvarchar(200)) AS [Group],[Content],[IntentType],[Retries],[Added],[ExpiresAt],[StatusName],[NextRetryAt],[LockedUntil]"
                : "[Id],[MessageId],[Version],[Name],[Group],[Content],[IntentType],[Retries],[Added],[ExpiresAt],[StatusName],[NextRetryAt],[LockedUntil]";
        var where = string.Empty;
        if (query.StatusName is not null)
        {
            where += " AND [StatusName]=@StatusName";
        }

        if (!string.IsNullOrEmpty(query.Name))
        {
            where += " AND [Name]=@Name";
        }

        if (!string.IsNullOrEmpty(query.Group))
        {
            where += " AND [Group]=@Group";
        }

        if (!string.IsNullOrEmpty(query.Content))
        {
            where += " AND [Content] LIKE @Content ESCAPE '\\'";
        }

        if (query.IntentType is { })
        {
            where += " AND [IntentType]=@IntentType";
        }

        // Keep the total count in a separate query: COUNT(*) OVER() returns no count row when OFFSET/FETCH yields
        // an empty later page, which breaks pagination metadata even though matching rows still exist.
        var countQuery = $"SELECT COUNT_BIG(Id) FROM {tableName} WHERE 1=1 {where}";

        var sqlQuery =
            $"SELECT {selectColumns} FROM {tableName} WHERE 1=1 {where} ORDER BY Added DESC OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY";

        object[] countSqlParams =
        [
            new SqlParameter("@StatusName", query.StatusName?.ToString("G") ?? string.Empty),
            new SqlParameter("@Group", query.Group ?? string.Empty),
            new SqlParameter("@Name", query.Name ?? string.Empty),
            new SqlParameter("@Content", $"%{_EscapeLike(query.Content)}%"),
            new SqlParameter("@IntentType", SqlDbType.SmallInt) { Value = (short?)query.IntentType ?? 0 },
        ];

        object[] pageSqlParams =
        [
            new SqlParameter("@StatusName", query.StatusName?.ToString("G") ?? string.Empty),
            new SqlParameter("@Group", query.Group ?? string.Empty),
            new SqlParameter("@Name", query.Name ?? string.Empty),
            new SqlParameter("@Content", $"%{_EscapeLike(query.Content)}%"),
            new SqlParameter("@IntentType", SqlDbType.SmallInt) { Value = (short?)query.IntentType ?? 0 },
            new SqlParameter("@Offset", query.CurrentPage * query.PageSize),
            new SqlParameter("@Limit", query.PageSize),
        ];

        await using var connection = new SqlConnection(_options.ConnectionString);

        var totalCount = await connection
            .ExecuteScalarAsync(
                countQuery,
                commandTimeout: _messagingOptions.CommandTimeout,
                sqlParams: countSqlParams,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        if (totalCount == 0)
        {
            return new([], query.CurrentPage, query.PageSize, 0);
        }

        var items = await connection
            .ExecuteReaderAsync(
                sqlQuery,
                async (reader, ct) =>
                {
                    var messages = new List<MessageView>();

                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        var index = 0;
                        messages.Add(
                            new MessageView
                            {
                                StorageId = reader.GetGuid(index++),
                                MessageId = reader.GetString(index++),
                                Version = reader.GetString(index++),
                                Name = reader.GetString(index++),
                                Group = await reader.IsDBNullAsync(index++, ct).ConfigureAwait(false)
                                    ? null
                                    : reader.GetString(index - 1),
                                Content = await reader.IsDBNullAsync(index++, ct).ConfigureAwait(false)
                                    ? null
                                    : reader.GetString(index - 1),
                                IntentType = (IntentType)reader.GetInt16(index++),
                                Retries = reader.GetInt32(index++),
                                Added = await reader
                                    .GetFieldValueAsync<DateTimeOffset>(index++, ct)
                                    .ConfigureAwait(false),
                                ExpiresAt = await reader.IsDBNullAsync(index++, ct).ConfigureAwait(false)
                                    ? null
                                    : await reader
                                        .GetFieldValueAsync<DateTimeOffset>(index - 1, ct)
                                        .ConfigureAwait(false),
                                StatusName = Enum.Parse<StatusName>(reader.GetString(index++)),
                                NextRetryAt = await reader.IsDBNullAsync(index++, ct).ConfigureAwait(false)
                                    ? null
                                    : await reader
                                        .GetFieldValueAsync<DateTimeOffset>(index - 1, ct)
                                        .ConfigureAwait(false),
                                LockedUntil = await reader.IsDBNullAsync(index++, ct).ConfigureAwait(false)
                                    ? null
                                    : await reader
                                        .GetFieldValueAsync<DateTimeOffset>(index - 1, ct)
                                        .ConfigureAwait(false),
                            }
                        );
                    }

                    return messages;
                },
                commandTimeout: _messagingOptions.CommandTimeout,
                sqlParams: pageSqlParams,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        return new(items, query.CurrentPage, query.PageSize, (int)Math.Min(totalCount, int.MaxValue));
    }

    /// <summary>Returns the total count of published messages in the <c>Failed</c> state.</summary>
    public ValueTask<long> PublishedFailedCount(CancellationToken cancellationToken = default)
    {
        return _GetNumberOfMessage(_publishedTable, nameof(StatusName.Failed), cancellationToken);
    }

    /// <summary>Returns the total count of published messages in the <c>Succeeded</c> state.</summary>
    public ValueTask<long> PublishedSucceededCount(CancellationToken cancellationToken = default)
    {
        return _GetNumberOfMessage(_publishedTable, nameof(StatusName.Succeeded), cancellationToken);
    }

    /// <summary>Returns the total count of received messages in the <c>Failed</c> state.</summary>
    public ValueTask<long> ReceivedFailedCount(CancellationToken cancellationToken = default)
    {
        return _GetNumberOfMessage(_receivedTable, nameof(StatusName.Failed), cancellationToken);
    }

    /// <summary>Returns the total count of received messages in the <c>Succeeded</c> state.</summary>
    public ValueTask<long> ReceivedSucceededCount(CancellationToken cancellationToken = default)
    {
        return _GetNumberOfMessage(_receivedTable, nameof(StatusName.Succeeded), cancellationToken);
    }

    /// <summary>Returns a single published message by its storage identifier, or <see langword="null"/> if not found.</summary>
    public async ValueTask<MediumMessage?> GetPublishedMessageAsync(
        Guid id,
        CancellationToken cancellationToken = default
    )
    {
        return await _GetMessageAsync(_publishedTable, id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns the published messages matching the supplied storage identifiers.</summary>
    public async ValueTask<IReadOnlyList<MediumMessage>> GetPublishedMessagesAsync(
        IReadOnlyList<Guid> storageIds,
        CancellationToken cancellationToken = default
    )
    {
        return await _GetMessagesAsync(_publishedTable, storageIds, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns a single received message by its storage identifier, or <see langword="null"/> if not found.</summary>
    public async ValueTask<MediumMessage?> GetReceivedMessageAsync(
        Guid id,
        CancellationToken cancellationToken = default
    )
    {
        return await _GetMessageAsync(_receivedTable, id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns the received messages matching the supplied storage identifiers.</summary>
    public async ValueTask<IReadOnlyList<MediumMessage>> GetReceivedMessagesAsync(
        IReadOnlyList<Guid> storageIds,
        CancellationToken cancellationToken = default
    )
    {
        return await _GetMessagesAsync(_receivedTable, storageIds, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<long> _GetNumberOfMessage(
        string tableName,
        string statusName,
        CancellationToken cancellationToken = default
    )
    {
        var sqlQuery = $"SELECT COUNT_BIG(Id) FROM {tableName} WITH (NOLOCK) WHERE StatusName = @StatusName";
        await using var connection = new SqlConnection(_options.ConnectionString);

        return await connection
            .ExecuteScalarAsync(
                sqlQuery,
                commandTimeout: _messagingOptions.CommandTimeout,
                sqlParams: [new SqlParameter("@StatusName", statusName)],
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
    }

    private Task<Dictionary<DateTime, int>> _GetHourlyTimelineStats(
        string tableName,
        string statusName,
        CancellationToken cancellationToken = default
    )
    {
        var now = timeProvider.GetUtcNow();
        var dates = new List<DateTime>();
        // Hourly buckets are label keys, not persisted instants: keep them DateTime.
        var nowUtc = now.UtcDateTime;

        for (var i = 0; i < 24; i++)
        {
            dates.Add(nowUtc.AddHours(-i));
        }

        var keyMaps = dates.ToDictionary(
            x => x.ToString("yyyy-MM-dd-HH", CultureInfo.InvariantCulture),
            x => x,
            StringComparer.Ordinal
        );

        return _GetTimelineStats(tableName, statusName, keyMaps, dates[^1], nowUtc, cancellationToken);
    }

    private async Task<Dictionary<DateTime, int>> _GetTimelineStats(
        string tableName,
        string statusName,
        Dictionary<string, DateTime> keyMaps,
        DateTime minAdded,
        DateTime maxAdded,
        CancellationToken cancellationToken = default
    )
    {
        // Use CONVERT instead of FORMAT to avoid CLR dependency (Azure SQL Edge doesn't support CLR)
        var sqlQuery = $"""
            WITH Aggr AS (
            -- COUNT (int) not COUNT_BIG: the reader maps [Count] via GetInt32 into a Dictionary<string,int>, and
            -- a single-hour bucket can never overflow int. SqlClient.GetInt32 rejects a bigint column outright.
            SELECT CONVERT(CHAR(10), Added, 120) + '-' + RIGHT('0' + CAST(DATEPART(HOUR, Added) AS VARCHAR(2)), 2) AS [Key],
                COUNT(Id) [Count]
            FROM  {tableName}
            WHERE StatusName = @StatusName AND Added >= @MinAdded AND Added <= @MaxAdded
            GROUP BY CONVERT(CHAR(10), Added, 120) + '-' + RIGHT('0' + CAST(DATEPART(HOUR, Added) AS VARCHAR(2)), 2)
            )
            SELECT [Key], [Count] FROM Aggr WITH (NOLOCK);
            """;

        object[] sqlParams =
        [
            new SqlParameter("@StatusName", statusName),
            new SqlParameter("@MinAdded", minAdded),
            new SqlParameter("@MaxAdded", maxAdded),
        ];

        Dictionary<string, int> valuesMap;

        var connection = new SqlConnection(_options.ConnectionString);

        await using (connection)
        {
            valuesMap = await connection
                .ExecuteReaderAsync(
                    sqlQuery,
                    async (reader, ct) =>
                    {
                        var dictionary = new Dictionary<string, int>(StringComparer.Ordinal);

                        while (await reader.ReadAsync(ct).ConfigureAwait(false))
                        {
                            dictionary.Add(reader.GetString(0), reader.GetInt32(1));
                        }

                        return dictionary;
                    },
                    commandTimeout: _messagingOptions.CommandTimeout,
                    sqlParams: sqlParams,
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);
        }

        var result = new Dictionary<DateTime, int>(keyMaps.Count);
        foreach (var (key, dateTime) in keyMaps)
        {
            result[dateTime] = valuesMap.TryGetValue(key, out var count) ? count : 0;
        }

        return result;
    }

    private async Task<IReadOnlyList<MediumMessage>> _GetMessagesAsync(
        string tableName,
        IReadOnlyList<Guid> storageIds,
        CancellationToken cancellationToken = default
    )
    {
        if (storageIds.Count == 0)
        {
            return [];
        }

        var exceptionInfoSql = string.Equals(tableName, _receivedTable, StringComparison.Ordinal)
            ? "ExceptionInfo"
            : "CAST(NULL AS nvarchar(max)) AS ExceptionInfo";

        // Pass the id set through the HeadlessMessagingIdList table-valued parameter (provisioned by the
        // storage initializer and already used by SqlServerDataStorage). The SQL text and the @Ids shape
        // stay constant regardless of id count, so SQL Server reuses one cached query plan instead of
        // compiling a fresh plan per dynamic IN-list length — and it stays portable to older engines
        // (table types need no OPENJSON / compatibility level 130).
        var tvpTypeName = $"[{_options.Schema}].[HeadlessMessagingIdList]";

        var idsTable = new DataTable();
        idsTable.Columns.Add("Id", typeof(Guid));
        foreach (var id in storageIds)
        {
            idsTable.Rows.Add(id);
        }

        var sqlParams = new object[]
        {
            new SqlParameter("@Ids", SqlDbType.Structured) { TypeName = tvpTypeName, Value = idsTable },
        };

        var sql =
            $"SELECT Id, Content, IntentType, Added, ExpiresAt, Retries, {exceptionInfoSql}, NextRetryAt, LockedUntil FROM {tableName} WITH (READPAST) WHERE Id IN (SELECT Id FROM @Ids)";

        await using var connection = new SqlConnection(_options.ConnectionString);

        return await connection
            .ExecuteReaderAsync(
                sql,
                async (reader, ct) =>
                {
                    var messages = new List<MediumMessage>();

                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        messages.Add(
                            new MediumMessage
                            {
                                StorageId = reader.GetGuid(0),
                                Origin = serializer.Deserialize(reader.GetString(1))!,
                                Content = reader.GetString(1),
                                IntentType = (IntentType)reader.GetInt16(2),
                                Added = await reader.GetFieldValueAsync<DateTimeOffset>(3, ct).ConfigureAwait(false),
                                ExpiresAt = await reader.IsDBNullAsync(4, ct).ConfigureAwait(false)
                                    ? null
                                    : await reader.GetFieldValueAsync<DateTimeOffset>(4, ct).ConfigureAwait(false),
                                Retries = reader.GetInt32(5),
                                ExceptionInfo = await reader.IsDBNullAsync(6, ct).ConfigureAwait(false)
                                    ? null
                                    : reader.GetString(6),
                                NextRetryAt = await reader.IsDBNullAsync(7, ct).ConfigureAwait(false)
                                    ? null
                                    : await reader.GetFieldValueAsync<DateTimeOffset>(7, ct).ConfigureAwait(false),
                                LockedUntil = await reader.IsDBNullAsync(8, ct).ConfigureAwait(false)
                                    ? null
                                    : await reader.GetFieldValueAsync<DateTimeOffset>(8, ct).ConfigureAwait(false),
                            }
                        );
                    }

                    return (IReadOnlyList<MediumMessage>)messages;
                },
                commandTimeout: _messagingOptions.CommandTimeout,
                sqlParams: sqlParams,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
    }

    private async Task<MediumMessage?> _GetMessageAsync(
        string tableName,
        Guid id,
        CancellationToken cancellationToken = default
    )
    {
        var exceptionInfoSql = string.Equals(tableName, _receivedTable, StringComparison.Ordinal)
            ? "ExceptionInfo"
            : "CAST(NULL AS nvarchar(max)) AS ExceptionInfo";
        var sql =
            $"SELECT TOP(1) Id, Content, IntentType, Added, ExpiresAt, Retries, {exceptionInfoSql}, NextRetryAt, LockedUntil FROM {tableName} WITH (READPAST) WHERE Id=@Id";

        await using var connection = new SqlConnection(_options.ConnectionString);

        var mediumMessage = await connection
            .ExecuteReaderAsync(
                sql,
                async (reader, ct) =>
                {
                    MediumMessage? message = null;

                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        var expiresAtIsNull = await reader.IsDBNullAsync(4, ct).ConfigureAwait(false);
                        message = new MediumMessage
                        {
                            StorageId = reader.GetGuid(0),
                            Origin = serializer.Deserialize(reader.GetString(1))!,
                            Content = reader.GetString(1),
                            IntentType = (IntentType)reader.GetInt16(2),
                            Added = await reader.GetFieldValueAsync<DateTimeOffset>(3, ct).ConfigureAwait(false),
                            ExpiresAt = expiresAtIsNull
                                ? null
                                : await reader.GetFieldValueAsync<DateTimeOffset>(4, ct).ConfigureAwait(false),
                            Retries = reader.GetInt32(5),
                            ExceptionInfo = await reader.IsDBNullAsync(6, ct).ConfigureAwait(false)
                                ? null
                                : reader.GetString(6),
                            NextRetryAt = await reader.IsDBNullAsync(7, ct).ConfigureAwait(false)
                                ? null
                                : await reader.GetFieldValueAsync<DateTimeOffset>(7, ct).ConfigureAwait(false),
                            LockedUntil = await reader.IsDBNullAsync(8, ct).ConfigureAwait(false)
                                ? null
                                : await reader.GetFieldValueAsync<DateTimeOffset>(8, ct).ConfigureAwait(false),
                        };
                    }

                    return message;
                },
                commandTimeout: _messagingOptions.CommandTimeout,
                sqlParams: [new SqlParameter("@Id", id)],
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        return mediumMessage;
    }

    private static string _EscapeLike(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        // Escape the ESCAPE character first, then the LIKE wildcards (% _ and the [ character class),
        // so user text matches literally instead of acting as a wildcard.
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal);
    }
}
