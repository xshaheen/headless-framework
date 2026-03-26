// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.Serialization;
using Headless.Primitives;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.SqlServer;

internal sealed class SqlServerMonitoringApi(
    IOptions<SqlServerOptions> options,
    IStorageInitializer initializer,
    ISerializer serializer,
    TimeProvider timeProvider
) : IMonitoringApi
{
    private readonly SqlServerOptions _options = options.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly string _publishedTable = initializer.GetPublishedTableName();
    private readonly string _receivedTable = initializer.GetReceivedTableName();

    public async ValueTask<StatisticsView> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var sql = $"""
            SELECT
                (SELECT COUNT_BIG(Id) FROM {_publishedTable} WHERE StatusName = N'Succeeded') AS PublishedSucceeded,
                (SELECT COUNT_BIG(Id) FROM {_receivedTable} WHERE StatusName = N'Succeeded') AS ReceivedSucceeded,
                (SELECT COUNT_BIG(Id) FROM {_publishedTable} WHERE StatusName = N'Failed') AS PublishedFailed,
                (SELECT COUNT_BIG(Id) FROM {_receivedTable} WHERE StatusName = N'Failed') AS ReceivedFailed,
                (SELECT COUNT_BIG(Id) FROM {_publishedTable} WHERE StatusName = N'Delayed') AS PublishedDelayed;
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
                    }

                    return statisticsDto;
                },
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        return statistics;
    }

    public async ValueTask<Dictionary<DateTime, int>> HourlyFailedJobs(
        MessageType type,
        CancellationToken cancellationToken = default
    )
    {
        var tableName = type == MessageType.Publish ? _publishedTable : _receivedTable;
        return await _GetHourlyTimelineStats(tableName, nameof(StatusName.Failed), cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<Dictionary<DateTime, int>> HourlySucceededJobs(
        MessageType type,
        CancellationToken cancellationToken = default
    )
    {
        var tableName = type == MessageType.Publish ? _publishedTable : _receivedTable;
        return await _GetHourlyTimelineStats(tableName, nameof(StatusName.Succeeded), cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<IndexPage<MessageView>> GetMessagesAsync(
        MessageQuery query,
        CancellationToken cancellationToken = default
    )
    {
        var tableName = query.MessageType == MessageType.Publish ? _publishedTable : _receivedTable;
        var selectColumns =
            query.MessageType == MessageType.Publish
                ? "[Id],[MessageId],[Version],[Name],CAST(NULL AS nvarchar(200)) AS [Group],[Content],[Retries],[Added],[ExpiresAt],[StatusName]"
                : "[Id],[MessageId],[Version],[Name],[Group],[Content],[Retries],[Added],[ExpiresAt],[StatusName]";
        var where = string.Empty;
        if (!string.IsNullOrEmpty(query.StatusName))
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
            where += " AND [Content] LIKE @Content";
        }

        // Keep the total count in a separate query: COUNT(*) OVER() returns no count row when OFFSET/FETCH yields
        // an empty later page, which breaks pagination metadata even though matching rows still exist.
        var countQuery = $"SELECT COUNT_BIG(Id) FROM {tableName} WHERE 1=1 {where}";

        var sqlQuery =
            $"SELECT {selectColumns} FROM {tableName} WHERE 1=1 {where} ORDER BY Added DESC OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY";

        object[] countSqlParams =
        [
            new SqlParameter("@StatusName", query.StatusName ?? string.Empty),
            new SqlParameter("@Group", query.Group ?? string.Empty),
            new SqlParameter("@Name", query.Name ?? string.Empty),
            new SqlParameter("@Content", $"%{query.Content}%"),
        ];

        object[] pageSqlParams =
        [
            new SqlParameter("@StatusName", query.StatusName ?? string.Empty),
            new SqlParameter("@Group", query.Group ?? string.Empty),
            new SqlParameter("@Name", query.Name ?? string.Empty),
            new SqlParameter("@Content", $"%{query.Content}%"),
            new SqlParameter("@Offset", query.CurrentPage * query.PageSize),
            new SqlParameter("@Limit", query.PageSize),
        ];

        await using var connection = new SqlConnection(_options.ConnectionString);

        var totalCount = await connection
            .ExecuteScalarAsync(countQuery, cancellationToken, countSqlParams)
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
                                StorageId = reader.GetInt64(index++),
                                MessageId = reader.GetString(index++),
                                Version = reader.GetString(index++),
                                Name = reader.GetString(index++),
                                Group = await reader.IsDBNullAsync(index++, ct).ConfigureAwait(false)
                                    ? null
                                    : reader.GetString(index - 1),
                                Content = await reader.IsDBNullAsync(index++, ct).ConfigureAwait(false)
                                    ? null
                                    : reader.GetString(index - 1),
                                Retries = reader.GetInt32(index++),
                                Added = reader.GetDateTime(index++),
                                ExpiresAt = await reader.IsDBNullAsync(index++, ct).ConfigureAwait(false)
                                    ? null
                                    : reader.GetDateTime(index - 1),
                                StatusName = reader.GetString(index++),
                            }
                        );
                    }

                    return messages;
                },
                cancellationToken: cancellationToken,
                sqlParams: pageSqlParams
            )
            .ConfigureAwait(false);

        return new(items, query.CurrentPage, query.PageSize, (int)Math.Min(totalCount, int.MaxValue));
    }

    public ValueTask<long> PublishedFailedCount(CancellationToken cancellationToken = default)
    {
        return _GetNumberOfMessage(_publishedTable, nameof(StatusName.Failed), cancellationToken);
    }

    public ValueTask<long> PublishedSucceededCount(CancellationToken cancellationToken = default)
    {
        return _GetNumberOfMessage(_publishedTable, nameof(StatusName.Succeeded), cancellationToken);
    }

    public ValueTask<long> ReceivedFailedCount(CancellationToken cancellationToken = default)
    {
        return _GetNumberOfMessage(_receivedTable, nameof(StatusName.Failed), cancellationToken);
    }

    public ValueTask<long> ReceivedSucceededCount(CancellationToken cancellationToken = default)
    {
        return _GetNumberOfMessage(_receivedTable, nameof(StatusName.Succeeded), cancellationToken);
    }

    public async ValueTask<MediumMessage?> GetPublishedMessageAsync(
        long id,
        CancellationToken cancellationToken = default
    )
    {
        return await _GetMessageAsync(_publishedTable, id, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<MediumMessage?> GetReceivedMessageAsync(
        long id,
        CancellationToken cancellationToken = default
    )
    {
        return await _GetMessageAsync(_receivedTable, id, cancellationToken).ConfigureAwait(false);
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
                cancellationToken: cancellationToken,
                sqlParams: [new SqlParameter("@StatusName", statusName)]
            )
            .ConfigureAwait(false);
    }

    private Task<Dictionary<DateTime, int>> _GetHourlyTimelineStats(
        string tableName,
        string statusName,
        CancellationToken cancellationToken = default
    )
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var dates = new List<DateTime>();
        for (var i = 0; i < 24; i++)
        {
            dates.Add(now.AddHours(-i));
        }

        var keyMaps = dates.ToDictionary(
            x => x.ToString("yyyy-MM-dd-HH", CultureInfo.InvariantCulture),
            x => x,
            StringComparer.Ordinal
        );

        return _GetTimelineStats(tableName, statusName, keyMaps, dates[^1], now, cancellationToken);
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
            SELECT CONVERT(CHAR(10), Added, 120) + '-' + RIGHT('0' + CAST(DATEPART(HOUR, Added) AS VARCHAR(2)), 2) AS [Key],
                COUNT_BIG(Id) [Count]
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
                            dictionary.Add(reader.GetString(0), (int)reader.GetInt64(1));
                        }

                        return dictionary;
                    },
                    cancellationToken: cancellationToken,
                    sqlParams: sqlParams
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

    private async Task<MediumMessage?> _GetMessageAsync(
        string tableName,
        long id,
        CancellationToken cancellationToken = default
    )
    {
        var exceptionInfoSql = string.Equals(tableName, _receivedTable, StringComparison.Ordinal)
            ? "ExceptionInfo"
            : "CAST(NULL AS nvarchar(max)) AS ExceptionInfo";
        var sql =
            $"SELECT TOP(1) Id, Content, Added, ExpiresAt, Retries, {exceptionInfoSql} FROM {tableName} WITH (READPAST) WHERE Id=@Id";

        await using var connection = new SqlConnection(_options.ConnectionString);

        var mediumMessage = await connection
            .ExecuteReaderAsync(
                sql,
                async (reader, ct) =>
                {
                    MediumMessage? message = null;

                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        var expiresAtIsNull = await reader.IsDBNullAsync(3, ct).ConfigureAwait(false);
                        message = new MediumMessage
                        {
                            StorageId = reader.GetInt64(0),
                            Origin = serializer.Deserialize(reader.GetString(1))!,
                            Content = reader.GetString(1),
                            Added = reader.GetDateTime(2),
                            ExpiresAt = expiresAtIsNull ? null : reader.GetDateTime(3),
                            Retries = reader.GetInt32(4),
                            ExceptionInfo = await reader.IsDBNullAsync(5, ct).ConfigureAwait(false)
                                ? null
                                : reader.GetString(5),
                        };
                    }

                    return message;
                },
                cancellationToken: cancellationToken,
                sqlParams: [new SqlParameter("@Id", id)]
            )
            .ConfigureAwait(false);

        return mediumMessage;
    }
}
