// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Messages.Internal;
using Framework.Messages.Messages;
using Framework.Messages.Monitoring;
using Framework.Messages.Persistence;
using Framework.Messages.Serialization;
using Framework.Primitives;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Framework.Messages;

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

    public async ValueTask<StatisticsView> GetStatisticsAsync()
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

        var connection = new SqlConnection(_options.ConnectionString);
        await using var _ = connection.ConfigureAwait(false);

        var statistics = await connection
            .ExecuteReaderAsync(
                sql,
                async reader =>
                {
                    var statisticsDto = new StatisticsView();

                    while (await reader.ReadAsync())
                    {
                        statisticsDto.PublishedSucceeded = reader.GetInt32(0);
                        statisticsDto.ReceivedSucceeded = reader.GetInt32(1);
                        statisticsDto.PublishedFailed = reader.GetInt32(2);
                        statisticsDto.ReceivedFailed = reader.GetInt32(3);
                        statisticsDto.PublishedDelayed = reader.GetInt32(4);
                    }

                    return statisticsDto;
                }
            )
            .ConfigureAwait(false);

        return statistics;
    }

    public async ValueTask<Dictionary<DateTime, int>> HourlyFailedJobs(MessageType type)
    {
        var tableName = type == MessageType.Publish ? _pubName : _recName;
        return await _GetHourlyTimelineStats(tableName, nameof(StatusName.Failed)).ConfigureAwait(false);
    }

    public async ValueTask<Dictionary<DateTime, int>> HourlySucceededJobs(MessageType type)
    {
        var tableName = type == MessageType.Publish ? _pubName : _recName;
        return await _GetHourlyTimelineStats(tableName, nameof(StatusName.Succeeded)).ConfigureAwait(false);
    }

    public async ValueTask<IndexPage<MessageView>> GetMessagesAsync(MessageQuery query)
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

        var connection = new SqlConnection(_options.ConnectionString);
        await using var _ = connection.ConfigureAwait(false);

        var count = await connection
            .ExecuteScalarAsync<int>(
                $"SELECT COUNT(1) FROM {tableName} WHERE 1=1 {where}",
                new SqlParameter("@StatusName", query.StatusName ?? string.Empty),
                new SqlParameter("@Group", query.Group ?? string.Empty),
                new SqlParameter("@Name", query.Name ?? string.Empty),
                new SqlParameter("@Content", $"%{query.Content}%")
            )
            .ConfigureAwait(false);

        var items = await connection
            .ExecuteReaderAsync(
                _options.IsSqlServer2008 ? sqlQuery2008 : sqlQuery,
                async reader =>
                {
                    var messages = new List<MessageView>();

                    while (await reader.ReadAsync().ConfigureAwait(false))
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
                sqlParams: sqlParams
            )
            .ConfigureAwait(false);

        return new(items, query.CurrentPage, query.PageSize, count);
    }

    public ValueTask<int> PublishedFailedCount()
    {
        return _GetNumberOfMessage(_pubName, nameof(StatusName.Failed));
    }

    public ValueTask<int> PublishedSucceededCount()
    {
        return _GetNumberOfMessage(_pubName, nameof(StatusName.Succeeded));
    }

    public ValueTask<int> ReceivedFailedCount()
    {
        return _GetNumberOfMessage(_recName, nameof(StatusName.Failed));
    }

    public ValueTask<int> ReceivedSucceededCount()
    {
        return _GetNumberOfMessage(_recName, nameof(StatusName.Succeeded));
    }

    public async ValueTask<MediumMessage?> GetPublishedMessageAsync(long id)
    {
        return await _GetMessageAsync(_pubName, id).ConfigureAwait(false);
    }

    public async ValueTask<MediumMessage?> GetReceivedMessageAsync(long id)
    {
        return await _GetMessageAsync(_recName, id).ConfigureAwait(false);
    }

    private async ValueTask<int> _GetNumberOfMessage(string tableName, string statusName)
    {
        var sqlQuery = $"SELECT COUNT(Id) FROM {tableName} WITH (NOLOCK) WHERE StatusName = @StatusName";
        var connection = new SqlConnection(_options.ConnectionString);
        await using var _ = connection.ConfigureAwait(false);
        return await connection
            .ExecuteScalarAsync<int>(sqlQuery, new SqlParameter("@StatusName", statusName))
            .ConfigureAwait(false);
    }

    private Task<Dictionary<DateTime, int>> _GetHourlyTimelineStats(string tableName, string statusName)
    {
        var endDate = timeProvider.GetUtcNow().UtcDateTime;
        var dates = new List<DateTime>();
        for (var i = 0; i < 24; i++)
        {
            dates.Add(endDate);
            endDate = endDate.AddHours(-1);
        }

        var keyMaps = dates.ToDictionary(x => x.ToString("yyyy-MM-dd-HH"), x => x, StringComparer.Ordinal);

        return _GetTimelineStats(tableName, statusName, keyMaps);
    }

    private async Task<Dictionary<DateTime, int>> _GetTimelineStats(
        string tableName,
        string statusName,
        IDictionary<string, DateTime> keyMaps
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
        await using (connection.ConfigureAwait(false))
        {
            valuesMap = await connection
                .ExecuteReaderAsync(
                    _options.IsSqlServer2008 ? sqlQuery2008 : sqlQuery,
                    async reader =>
                    {
                        var dictionary = new Dictionary<string, int>(StringComparer.Ordinal);

                        while (await reader.ReadAsync().ConfigureAwait(false))
                        {
                            dictionary.Add(reader.GetString(0), reader.GetInt32(1));
                        }

                        return dictionary;
                    },
                    sqlParams: sqlParams
                )
                .ConfigureAwait(false);
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

    private async Task<MediumMessage?> _GetMessageAsync(string tableName, long id)
    {
        var sql =
            $"SELECT TOP 1 Id AS DbId, Content, Added, ExpiresAt, Retries FROM {tableName} WITH (READPAST) WHERE Id={id}";

        var connection = new SqlConnection(_options.ConnectionString);

        await using var _ = connection.ConfigureAwait(false);

        var mediumMessage = await connection
            .ExecuteReaderAsync(
                sql,
                async reader =>
                {
                    MediumMessage? message = null;

                    while (await reader.ReadAsync().ConfigureAwait(false))
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
                }
            )
            .ConfigureAwait(false);

        return mediumMessage;
    }
}
