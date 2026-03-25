// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.Serialization;
using Headless.Primitives;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Headless.Messaging.PostgreSql;

/// <summary>
/// PostgreSQL implementation of <see cref="IMonitoringApi"/> for querying message statistics and history.
/// Provides dashboard data including message counts, status breakdowns, and hourly metrics.
/// </summary>
public sealed class PostgreSqlMonitoringApi(
    IOptions<PostgreSqlOptions> options,
    IStorageInitializer initializer,
    ISerializer serializer,
    TimeProvider timeProvider
) : IMonitoringApi
{
    private readonly PostgreSqlOptions _options = Argument.IsNotNull(options.Value);
    private readonly string _publishedTable = initializer.GetPublishedTableName();
    private readonly string _receivedTable = initializer.GetReceivedTableName();

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

    public async ValueTask<StatisticsView> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var sql = $"""
            SELECT
            (
                SELECT COUNT("Id") FROM {_publishedTable} WHERE "StatusName" = 'Succeeded'
            ) AS "PublishedSucceeded",
            (
                SELECT COUNT("Id") FROM {_receivedTable} WHERE "StatusName" = 'Succeeded'
            ) AS "ReceivedSucceeded",
            (
                SELECT COUNT("Id") FROM {_publishedTable} WHERE "StatusName" = 'Failed'
            ) AS "PublishedFailed",
            (
                SELECT COUNT("Id") FROM {_receivedTable} WHERE "StatusName" = 'Failed'
            ) AS "ReceivedFailed",
            (
                SELECT COUNT("Id") FROM {_publishedTable} WHERE "StatusName" = 'Delayed'
            ) AS "PublishedDelayed";
            """;

        await using var connection = _options.CreateConnection();

        var statistics = await connection
            .ExecuteReaderAsync(
                sql,
                static async (reader, cancellationToken) =>
                {
                    var statisticsDto = new StatisticsView();

                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
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

    public async ValueTask<IndexPage<MessageView>> GetMessagesAsync(
        MessageQuery query,
        CancellationToken cancellationToken = default
    )
    {
        var tableName = query.MessageType == MessageType.Publish ? _publishedTable : _receivedTable;
        var selectColumns =
            query.MessageType == MessageType.Publish
                ? @"""Id"",""MessageId"",""Version"",""Name"",CAST(NULL AS VARCHAR(200)) AS ""Group"",""Content"",""Retries"",""Added"",""ExpiresAt"",""StatusName"""
                : @"""Id"",""MessageId"",""Version"",""Name"",""Group"",""Content"",""Retries"",""Added"",""ExpiresAt"",""StatusName""";
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
            $"SELECT {selectColumns}, COUNT(*) OVER() AS \"TotalCount\" FROM {tableName} WHERE 1=1 {where} ORDER BY \"Added\" DESC OFFSET @Offset LIMIT @Limit";

        await using var connection = _options.CreateConnection();

        object[] sqlParams =
        [
            new NpgsqlParameter("@StatusName", query.StatusName ?? string.Empty),
            new NpgsqlParameter("@Group", query.Group ?? string.Empty),
            new NpgsqlParameter("@Name", query.Name ?? string.Empty),
            new NpgsqlParameter("@Content", $"%{query.Content}%"),
            new NpgsqlParameter("@Offset", query.CurrentPage * query.PageSize),
            new NpgsqlParameter("@Limit", query.PageSize),
        ];

        var totalCount = 0;
        var items = await connection
            .ExecuteReaderAsync(
                sqlQuery,
                async (reader, token) =>
                {
                    var messages = new List<MessageView>();

                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        var index = 0;
                        messages.Add(
                            new MessageView
                            {
                                StorageId = reader.GetInt64(index++),
                                MessageId = reader.GetString(index++),
                                Version = reader.GetString(index++),
                                Name = reader.GetString(index++),
                                Group = await reader.IsDBNullAsync(index++, token).ConfigureAwait(false)
                                    ? null
                                    : reader.GetString(index - 1),
                                Content = await reader.IsDBNullAsync(index++, token).ConfigureAwait(false)
                                    ? null
                                    : reader.GetString(index - 1),
                                Retries = reader.GetInt32(index++),
                                Added = reader.GetDateTime(index++),
                                ExpiresAt = await reader.IsDBNullAsync(index++, token).ConfigureAwait(false)
                                    ? null
                                    : reader.GetDateTime(index - 1),
                                StatusName = reader.GetString(index++),
                            }
                        );
                        totalCount = reader.GetInt32(index);
                    }
                    return messages;
                },
                cancellationToken: cancellationToken,
                sqlParams: sqlParams
            )
            .ConfigureAwait(false);

        return new(items, query.CurrentPage, query.PageSize, totalCount);
    }

    public ValueTask<long> PublishedFailedCount(CancellationToken cancellationToken = default)
    {
        return _GetNumberOfMessage(_publishedTable, nameof(StatusName.Failed));
    }

    public ValueTask<long> PublishedSucceededCount(CancellationToken cancellationToken = default)
    {
        return _GetNumberOfMessage(_publishedTable, nameof(StatusName.Succeeded));
    }

    public ValueTask<long> ReceivedFailedCount(CancellationToken cancellationToken = default)
    {
        return _GetNumberOfMessage(_receivedTable, nameof(StatusName.Failed));
    }

    public ValueTask<long> ReceivedSucceededCount(CancellationToken cancellationToken = default)
    {
        return _GetNumberOfMessage(_receivedTable, nameof(StatusName.Succeeded));
    }

    public async ValueTask<Dictionary<DateTime, int>> HourlySucceededJobs(
        MessageType type,
        CancellationToken cancellationToken = default
    )
    {
        var tableName = type == MessageType.Publish ? _publishedTable : _receivedTable;
        return await _GetHourlyTimelineStats(tableName, nameof(StatusName.Succeeded)).ConfigureAwait(false);
    }

    public async ValueTask<Dictionary<DateTime, int>> HourlyFailedJobs(
        MessageType type,
        CancellationToken cancellationToken = default
    )
    {
        var tableName = type == MessageType.Publish ? _publishedTable : _receivedTable;
        return await _GetHourlyTimelineStats(tableName, nameof(StatusName.Failed)).ConfigureAwait(false);
    }

    private async ValueTask<long> _GetNumberOfMessage(
        string tableName,
        string statusName,
        CancellationToken cancellationToken = default
    )
    {
        var sqlQuery = $"SELECT COUNT(\"Id\") FROM {tableName} WHERE Lower(\"StatusName\") = Lower(@State)";

        await using var connection = _options.CreateConnection();

        object[] sqlParams = [new NpgsqlParameter("@State", statusName)];

        return await connection.ExecuteScalarAsync(sqlQuery, cancellationToken, sqlParams).ConfigureAwait(false);
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
        var sqlQuery = $"""
            WITH Aggr AS (
                SELECT to_char("Added",'yyyy-MM-dd-HH') AS "Key",
                COUNT("Id") AS "Count"
                FROM {tableName}
                    WHERE "StatusName" = @StatusName AND "Added" >= @MinAdded AND "Added" <= @MaxAdded
                GROUP BY to_char("Added", 'yyyy-MM-dd-HH')
            )
            SELECT "Key","Count" from Aggr;
            """;

        object[] sqlParams =
        [
            new NpgsqlParameter("@StatusName", statusName),
            new NpgsqlParameter("@MinAdded", minAdded),
            new NpgsqlParameter("@MaxAdded", maxAdded),
        ];

        await using var connection = _options.CreateConnection();

        var valuesMap = await connection
            .ExecuteReaderAsync(
                sqlQuery,
                static async (reader, token) =>
                {
                    var dictionary = new Dictionary<string, int>(StringComparer.Ordinal);

                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        dictionary.Add(reader.GetString(0), reader.GetInt32(1));
                    }

                    return dictionary;
                },
                cancellationToken: cancellationToken,
                sqlParams: sqlParams
            )
            .ConfigureAwait(false);

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
            $@"SELECT ""Id"" AS ""StorageId"", ""Content"", ""Added"", ""ExpiresAt"", ""Retries"", ""ExceptionInfo"" FROM {tableName} WHERE ""Id""=@Id";

        await using var connection = _options.CreateConnection();

        var mediumMessage = await connection
            .ExecuteReaderAsync(
                sql,
                async (reader, token) =>
                {
                    MediumMessage? message = null;

                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        message = new MediumMessage
                        {
                            StorageId = reader.GetInt64(0),
                            Origin = serializer.Deserialize(reader.GetString(1))!,
                            Content = reader.GetString(1),
                            Added = reader.GetDateTime(2),
                            ExpiresAt = await reader.IsDBNullAsync(3, token).ConfigureAwait(false)
                                ? null
                                : reader.GetDateTime(3),
                            Retries = reader.GetInt32(4),
                            ExceptionInfo = await reader.IsDBNullAsync(5, token).ConfigureAwait(false)
                                ? null
                                : reader.GetString(5),
                        };
                    }

                    return message;
                },
                cancellationToken: cancellationToken,
                sqlParams: new NpgsqlParameter("@Id", id)
            )
            .ConfigureAwait(false);

        return mediumMessage;
    }
}
