// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Primitives;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.Serialization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.SqlServer;

internal class SqlServerMonitoringApi(
    IOptions<SqlServerOptions> options,
    IStorageInitializer initializer,
    ISerializer serializer,
    TimeProvider timeProvider
) : IMonitoringApi
{
    private readonly SqlServerOptions _options = options.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly string _pubName = initializer.GetPublishedTableName();
    private readonly string _recName = initializer.GetReceivedTableName();

    public async ValueTask<StatisticsView> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var sql = $"""
            SELECT
            (
                SELECT COUNT(Id) FROM {_pubName} WHERE StatusName = N'Succeeded'
            ) AS PublishedSucceeded,
            (
                SELECT COUNT(Id) FROM {_recName} WHERE StatusName = N'Succeeded'
            ) AS ReceivedSucceeded,
            (
                SELECT COUNT(Id) FROM {_pubName} WHERE StatusName = N'Failed'
            ) AS PublishedFailed,
            (
                SELECT COUNT(Id) FROM {_recName} WHERE StatusName = N'Failed'
            ) AS ReceivedFailed,
            (
                SELECT COUNT(Id) FROM {_pubName} WHERE StatusName = N'Delayed'
            ) AS PublishedDelayed;
            """;

        await using var connection = new SqlConnection(_options.ConnectionString);

        var statistics = await connection
            .ExecuteReaderAsync(
                sql,
                async (reader, ct) =>
                {
                    var statisticsDto = new StatisticsView();

                    while (await reader.ReadAsync(ct))
                    {
                        statisticsDto.PublishedSucceeded = reader.GetInt32(0);
                        statisticsDto.ReceivedSucceeded = reader.GetInt32(1);
                        statisticsDto.PublishedFailed = reader.GetInt32(2);
                        statisticsDto.ReceivedFailed = reader.GetInt32(3);
                        statisticsDto.PublishedDelayed = reader.GetInt32(4);
                    }

                    return statisticsDto;
                },
                cancellationToken: cancellationToken
            )
            .AnyContext();

        return statistics;
    }

    public async ValueTask<Dictionary<DateTime, int>> HourlyFailedJobs(
        MessageType type,
        CancellationToken cancellationToken = default
    )
    {
        var tableName = type == MessageType.Publish ? _pubName : _recName;
        return await _GetHourlyTimelineStats(tableName, nameof(StatusName.Failed), cancellationToken).AnyContext();
    }

    public async ValueTask<Dictionary<DateTime, int>> HourlySucceededJobs(
        MessageType type,
        CancellationToken cancellationToken = default
    )
    {
        var tableName = type == MessageType.Publish ? _pubName : _recName;
        return await _GetHourlyTimelineStats(tableName, nameof(StatusName.Succeeded), cancellationToken).AnyContext();
    }

    public async ValueTask<IndexPage<MessageView>> GetMessagesAsync(
        MessageQuery query,
        CancellationToken cancellationToken = default
    )
    {
        var tableName = query.MessageType == MessageType.Publish ? _pubName : _recName;
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

        var sqlQuery2008 =
            $"SELECT * FROM (SELECT p.*, ROW_NUMBER() OVER(ORDER BY p.Added DESC) AS RowNum FROM {tableName} AS p WHERE 1=1 {where}) as tbl WHERE tbl.RowNum BETWEEN @Offset AND @Offset + @Limit";

        var sqlQuery =
            $"SELECT * FROM {tableName} WHERE 1=1 {where} ORDER BY Added DESC OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY";

        object[] sqlParams =
        [
            new SqlParameter("@StatusName", query.StatusName ?? string.Empty),
            new SqlParameter("@Group", query.Group ?? string.Empty),
            new SqlParameter("@Name", query.Name ?? string.Empty),
            new SqlParameter("@Content", $"%{query.Content}%"),
            new SqlParameter("@Offset", query.CurrentPage * query.PageSize),
            new SqlParameter("@Limit", query.PageSize),
        ];

        await using var connection = new SqlConnection(_options.ConnectionString);

        var count = await connection
            .ExecuteScalarAsync(
                $"SELECT COUNT(1) FROM {tableName} WHERE 1=1 {where}",
                cancellationToken: cancellationToken,
                sqlParams:
                [
                    new SqlParameter("@StatusName", query.StatusName ?? string.Empty),
                    new SqlParameter("@Group", query.Group ?? string.Empty),
                    new SqlParameter("@Name", query.Name ?? string.Empty),
                    new SqlParameter("@Content", $"%{query.Content}%"),
                ]
            )
            .AnyContext();

        var items = await connection
            .ExecuteReaderAsync(
                _options.IsSqlServer2008 ? sqlQuery2008 : sqlQuery,
                async (reader, ct) =>
                {
                    var messages = new List<MessageView>();

                    while (await reader.ReadAsync(ct).AnyContext())
                    {
                        var index = 0;
                        messages.Add(
                            new MessageView
                            {
                                Id = reader.GetInt64(index++).ToString(CultureInfo.InvariantCulture),
                                Version = reader.GetString(index++),
                                Name = reader.GetString(index++),
                                Group = query.MessageType == MessageType.Subscribe ? reader.GetString(index++) : null,
                                Content = reader.GetString(index++),
                                Retries = reader.GetInt32(index++),
                                Added = reader.GetDateTime(index++),
                                ExpiresAt = await reader.IsDBNullAsync(index++) ? null : reader.GetDateTime(index - 1),
                                StatusName = reader.GetString(index),
                            }
                        );
                    }

                    return messages;
                },
                cancellationToken: cancellationToken,
                sqlParams: sqlParams
            )
            .AnyContext();

        return new(items, query.CurrentPage, query.PageSize, count);
    }

    public ValueTask<int> PublishedFailedCount(CancellationToken cancellationToken = default)
    {
        return _GetNumberOfMessage(_pubName, nameof(StatusName.Failed), cancellationToken);
    }

    public ValueTask<int> PublishedSucceededCount(CancellationToken cancellationToken = default)
    {
        return _GetNumberOfMessage(_pubName, nameof(StatusName.Succeeded), cancellationToken);
    }

    public ValueTask<int> ReceivedFailedCount(CancellationToken cancellationToken = default)
    {
        return _GetNumberOfMessage(_recName, nameof(StatusName.Failed), cancellationToken);
    }

    public ValueTask<int> ReceivedSucceededCount(CancellationToken cancellationToken = default)
    {
        return _GetNumberOfMessage(_recName, nameof(StatusName.Succeeded), cancellationToken);
    }

    public async ValueTask<MediumMessage?> GetPublishedMessageAsync(
        long id,
        CancellationToken cancellationToken = default
    )
    {
        return await _GetMessageAsync(_pubName, id, cancellationToken).AnyContext();
    }

    public async ValueTask<MediumMessage?> GetReceivedMessageAsync(
        long id,
        CancellationToken cancellationToken = default
    )
    {
        return await _GetMessageAsync(_recName, id, cancellationToken).AnyContext();
    }

    private async ValueTask<int> _GetNumberOfMessage(
        string tableName,
        string statusName,
        CancellationToken cancellationToken = default
    )
    {
        var sqlQuery = $"SELECT COUNT(Id) FROM {tableName} WITH (NOLOCK) WHERE StatusName = @StatusName";
        await using var connection = new SqlConnection(_options.ConnectionString);

        return await connection
            .ExecuteScalarAsync(
                sqlQuery,
                cancellationToken: cancellationToken,
                sqlParams: new SqlParameter("@StatusName", statusName)
            )
            .AnyContext();
    }

    private Task<Dictionary<DateTime, int>> _GetHourlyTimelineStats(
        string tableName,
        string statusName,
        CancellationToken cancellationToken = default
    )
    {
        var endDate = timeProvider.GetUtcNow().UtcDateTime;
        var dates = new List<DateTime>();
        for (var i = 0; i < 24; i++)
        {
            dates.Add(endDate);
            endDate = endDate.AddHours(-1);
        }

        var keyMaps = dates.ToDictionary(
            x => x.ToString("yyyy-MM-dd-HH", CultureInfo.InvariantCulture),
            x => x,
            StringComparer.Ordinal
        );

        return _GetTimelineStats(tableName, statusName, keyMaps, cancellationToken);
    }

    private async Task<Dictionary<DateTime, int>> _GetTimelineStats(
        string tableName,
        string statusName,
        Dictionary<string, DateTime> keyMaps,
        CancellationToken cancellationToken = default
    )
    {
        var sqlQuery2008 = $"""
            WITH Aggr AS (
            SELECT REPLACE(CONVERT(varchar, Added, 111), '/','-') + '-' + CONVERT(varchar, DATEPART(hh, Added)) AS [Key],
                COUNT(Id) [Count]
            FROM  {tableName}
            WHERE StatusName = @StatusName
            GROUP BY REPLACE(CONVERT(varchar, Added, 111), '/','-') + '-' + CONVERT(varchar, DATEPART(hh, Added))
            )
            SELECT [Key], [Count] FROM Aggr WITH (NOLOCK) WHERE [Key] >= @MinKey AND [Key] <= @MaxKey;
            """;

        //SQL Server 2012+
        var sqlQuery = $"""
            WITH Aggr AS (
            SELECT FORMAT(Added,'yyyy-MM-dd-HH') AS [Key],
                COUNT(Id) [Count]
            FROM  {tableName}
            WHERE StatusName = @StatusName
            GROUP BY FORMAT(Added,'yyyy-MM-dd-HH')
            )
            SELECT [Key], [Count] FROM Aggr WITH (NOLOCK) WHERE [Key] >= @MinKey AND [Key] <= @MaxKey;
            """;

        object[] sqlParams =
        [
            new SqlParameter("@StatusName", statusName),
            new SqlParameter("@MinKey", keyMaps.Keys.Min()),
            new SqlParameter("@MaxKey", keyMaps.Keys.Max()),
        ];

        Dictionary<string, int> valuesMap;

        var connection = new SqlConnection(_options.ConnectionString);

        await using (connection)
        {
            valuesMap = await connection
                .ExecuteReaderAsync(
                    _options.IsSqlServer2008 ? sqlQuery2008 : sqlQuery,
                    async (reader, ct) =>
                    {
                        var dictionary = new Dictionary<string, int>(StringComparer.Ordinal);

                        while (await reader.ReadAsync(ct).AnyContext())
                        {
                            dictionary.Add(reader.GetString(0), reader.GetInt32(1));
                        }

                        return dictionary;
                    },
                    cancellationToken: cancellationToken,
                    sqlParams: sqlParams
                )
                .AnyContext();
        }

        foreach (var key in keyMaps.Keys)
        {
            valuesMap.TryAdd(key, 0);
        }

        var result = new Dictionary<DateTime, int>();
        for (var i = 0; i < keyMaps.Count; i++)
        {
            var value = valuesMap[keyMaps.ElementAt(i).Key];
            result.Add(keyMaps.ElementAt(i).Value, value);
        }

        return result;
    }

    private async Task<MediumMessage?> _GetMessageAsync(
        string tableName,
        long id,
        CancellationToken cancellationToken = default
    )
    {
        var sql =
            $"SELECT TOP(1) Id AS DbId, Content, Added, ExpiresAt, Retries FROM {tableName} WITH (READPAST) WHERE Id={id}";

        await using var connection = new SqlConnection(_options.ConnectionString);

        var mediumMessage = await connection
            .ExecuteReaderAsync(
                sql,
                async (reader, ct) =>
                {
                    MediumMessage? message = null;

                    while (await reader.ReadAsync(ct).AnyContext())
                    {
                        message = new MediumMessage
                        {
                            DbId = reader.GetInt64(0).ToString(CultureInfo.InvariantCulture),
                            Origin = serializer.Deserialize(reader.GetString(1))!,
                            Content = reader.GetString(1),
                            Added = reader.GetDateTime(2),
                            ExpiresAt = reader.GetDateTime(3),
                            Retries = reader.GetInt32(4),
                        };
                    }

                    return message;
                },
                cancellationToken: cancellationToken
            )
            .AnyContext();

        return mediumMessage;
    }
}
