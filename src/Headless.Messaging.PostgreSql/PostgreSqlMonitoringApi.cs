// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Framework.Primitives;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.Serialization;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Headless.Messaging.PostgreSql;

public sealed class PostgreSqlMonitoringApi(
    IOptions<PostgreSqlOptions> options,
    IStorageInitializer initializer,
    ISerializer serializer,
    TimeProvider timeProvider
) : IMonitoringApi
{
    private readonly PostgreSqlOptions _options = Argument.IsNotNull(options.Value);
    private readonly string _pubName = initializer.GetPublishedTableName();
    private readonly string _recName = initializer.GetReceivedTableName();

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

    public async ValueTask<StatisticsView> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var sql = $"""
            SELECT
            (
                SELECT COUNT("Id") FROM {_pubName} WHERE "StatusName" = N'Succeeded'
            ) AS "PublishedSucceeded",
            (
                SELECT COUNT("Id") FROM {_recName} WHERE "StatusName" = N'Succeeded'
            ) AS "ReceivedSucceeded",
            (
                SELECT COUNT("Id") FROM {_pubName} WHERE "StatusName" = N'Failed'
            ) AS "PublishedFailed",
            (
                SELECT COUNT("Id") FROM {_recName} WHERE "StatusName" = N'Failed'
            ) AS "ReceivedFailed",
            (
                SELECT COUNT("Id") FROM {_pubName} WHERE "StatusName" = N'Delayed'
            ) AS "PublishedDelayed";
            """;

        await using var connection = _options.CreateConnection();

        var statistics = await connection
            .ExecuteReaderAsync(
                sql,
                static async (reader, cancellationToken) =>
                {
                    var statisticsDto = new StatisticsView();

                    while (await reader.ReadAsync(cancellationToken).AnyContext())
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

    public async ValueTask<IndexPage<MessageView>> GetMessagesAsync(
        MessageQuery query,
        CancellationToken cancellationToken = default
    )
    {
        var tableName = query.MessageType == MessageType.Publish ? _pubName : _recName;
        var where = string.Empty;

        if (!string.IsNullOrEmpty(query.StatusName))
        {
            where += " AND Lower(\"StatusName\") = Lower(@StatusName)";
        }

        if (!string.IsNullOrEmpty(query.Name))
        {
            where += " AND Lower(\"Name\") = Lower(@Name)";
        }

        if (!string.IsNullOrEmpty(query.Group))
        {
            where += " AND Lower(\"Group\") = Lower(@Group)";
        }

        if (!string.IsNullOrEmpty(query.Content))
        {
            where += " AND \"Content\" ILike @Content";
        }

        var sqlQuery =
            $"SELECT * FROM {tableName} WHERE 1=1 {where} ORDER BY \"Added\" DESC OFFSET @Offset LIMIT @Limit";

        await using var connection = _options.CreateConnection();

        object[] countParams =
        [
            new NpgsqlParameter("@StatusName", query.StatusName ?? string.Empty),
            new NpgsqlParameter("@Group", query.Group ?? string.Empty),
            new NpgsqlParameter("@Name", query.Name ?? string.Empty),
            new NpgsqlParameter("@Content", $"%{query.Content}%"),
        ];

        var count = await connection
            .ExecuteScalarAsync(
                $"SELECT COUNT(1) FROM {tableName} WHERE 1=1 {where}",
                cancellationToken: cancellationToken,
                sqlParams: countParams
            )
            .AnyContext();

        object[] sqlParams =
        [
            new NpgsqlParameter("@StatusName", query.StatusName ?? string.Empty),
            new NpgsqlParameter("@Group", query.Group ?? string.Empty),
            new NpgsqlParameter("@Name", query.Name ?? string.Empty),
            new NpgsqlParameter("@Content", $"%{query.Content}%"),
            new NpgsqlParameter("@Offset", query.CurrentPage * query.PageSize),
            new NpgsqlParameter("@Limit", query.PageSize),
        ];

        var items = await connection
            .ExecuteReaderAsync(
                sqlQuery,
                async (reader, token) =>
                {
                    var messages = new List<MessageView>();

                    while (await reader.ReadAsync(token).AnyContext())
                    {
                        var index = 0;
                        messages.Add(
                            new MessageView
                            {
                                Id = reader.GetInt64(index++).ToString(CultureInfo.InvariantCulture),
                                Version = reader.GetString(index++),
                                Name = reader.GetString(index++),
                                Group = query.MessageType is MessageType.Subscribe ? reader.GetString(index++) : null,
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
        return _GetNumberOfMessage(_pubName, nameof(StatusName.Failed));
    }

    public ValueTask<int> PublishedSucceededCount(CancellationToken cancellationToken = default)
    {
        return _GetNumberOfMessage(_pubName, nameof(StatusName.Succeeded));
    }

    public ValueTask<int> ReceivedFailedCount(CancellationToken cancellationToken = default)
    {
        return _GetNumberOfMessage(_recName, nameof(StatusName.Failed));
    }

    public ValueTask<int> ReceivedSucceededCount(CancellationToken cancellationToken = default)
    {
        return _GetNumberOfMessage(_recName, nameof(StatusName.Succeeded));
    }

    public async ValueTask<Dictionary<DateTime, int>> HourlySucceededJobs(
        MessageType type,
        CancellationToken cancellationToken = default
    )
    {
        var tableName = type == MessageType.Publish ? _pubName : _recName;
        return await _GetHourlyTimelineStats(tableName, nameof(StatusName.Succeeded)).AnyContext();
    }

    public async ValueTask<Dictionary<DateTime, int>> HourlyFailedJobs(
        MessageType type,
        CancellationToken cancellationToken = default
    )
    {
        var tableName = type == MessageType.Publish ? _pubName : _recName;
        return await _GetHourlyTimelineStats(tableName, nameof(StatusName.Failed)).AnyContext();
    }

    private async ValueTask<int> _GetNumberOfMessage(
        string tableName,
        string statusName,
        CancellationToken cancellationToken = default
    )
    {
        var sqlQuery = $"SELECT COUNT(\"Id\") FROM {tableName} WHERE Lower(\"StatusName\") = Lower(@State)";

        await using var connection = _options.CreateConnection();

        object[] sqlParams = [new NpgsqlParameter("@State", statusName)];

        return await connection
            .ExecuteScalarAsync(sqlQuery, cancellationToken, sqlParams)
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
        var sqlQuery = $"""
            WITH Aggr AS (
                SELECT to_char("Added",'yyyy-MM-dd-HH') AS "Key",
                COUNT("Id") AS "Count"
                FROM {tableName}
                    WHERE "StatusName" = @StatusName
                GROUP BY to_char("Added", 'yyyy-MM-dd-HH')
            )
            SELECT "Key","Count" from Aggr WHERE "Key" >= @MinKey AND "Key" <= @MaxKey;
            """;

        object[] sqlParams =
        [
            new NpgsqlParameter("@StatusName", statusName),
            new NpgsqlParameter("@MinKey", keyMaps.Keys.Min()),
            new NpgsqlParameter("@MaxKey", keyMaps.Keys.Max()),
        ];

        Dictionary<string, int> valuesMap;
        var connection = _options.CreateConnection();

        await using (connection)
        {
            valuesMap = await connection
                .ExecuteReaderAsync(
                    sqlQuery,
                    static async (reader, token) =>
                    {
                        var dictionary = new Dictionary<string, int>(StringComparer.Ordinal);

                        while (await reader.ReadAsync(token).AnyContext())
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

        var result = new Dictionary<DateTime, int>();

        foreach (var (key, dateTime) in keyMaps)
        {
            var value = valuesMap.GetValueOrDefault(key, 0);
            result.Add(dateTime, value);
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
            $@"SELECT ""Id"" AS ""DbId"", ""Content"", ""Added"", ""ExpiresAt"", ""Retries"" FROM {tableName} WHERE ""Id""=@Id";

        await using var connection = _options.CreateConnection();

        var mediumMessage = await connection
            .ExecuteReaderAsync(
                sql,
                async (reader, token) =>
                {
                    MediumMessage? message = null;

                    while (await reader.ReadAsync(token).AnyContext())
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
                cancellationToken: cancellationToken,
                sqlParams: new NpgsqlParameter("@Id", id)
            )
            .AnyContext();

        return mediumMessage;
    }
}
