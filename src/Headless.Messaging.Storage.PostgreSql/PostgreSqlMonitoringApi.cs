// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Checks;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.Serialization;
using Headless.Primitives;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Headless.Messaging.Storage.PostgreSql;

/// <summary>
/// PostgreSQL implementation of <see cref="IMonitoringApi"/> for querying message statistics and history.
/// Provides dashboard data including message counts, status breakdowns, and hourly metrics.
/// </summary>
internal sealed class PostgreSqlMonitoringApi(
    IOptions<PostgreSqlOptions> options,
    IOptions<MessagingOptions> messagingOptions,
    IStorageInitializer initializer,
    ISerializer serializer,
    TimeProvider timeProvider
) : IMonitoringApi
{
    private readonly PostgreSqlOptions _options = Argument.IsNotNull(options.Value);
    private readonly MessagingOptions _messagingOptions = messagingOptions.Value;
    private readonly string _publishedTable = initializer.GetPublishedTableName();
    private readonly string _receivedTable = initializer.GetReceivedTableName();

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

    /// <summary>
    /// Returns aggregate message counts broken down by status (succeeded, failed, delayed, pending retry)
    /// for both the published and received tables.
    /// </summary>
    public async ValueTask<StatisticsView> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var sql = $"""
            SELECT
            (
                SELECT COUNT("Id") FROM {_publishedTable} WHERE "IntentType" IN (0, 1) AND "StatusName" = 'Succeeded'
            ) AS "PublishedSucceeded",
            (
                SELECT COUNT("Id") FROM {_receivedTable} WHERE "IntentType" IN (0, 1) AND "StatusName" = 'Succeeded'
            ) AS "ReceivedSucceeded",
            (
                SELECT COUNT("Id") FROM {_publishedTable} WHERE "IntentType" IN (0, 1) AND "StatusName" = 'Failed'
            ) AS "PublishedFailed",
            (
                SELECT COUNT("Id") FROM {_receivedTable} WHERE "IntentType" IN (0, 1) AND "StatusName" = 'Failed'
            ) AS "ReceivedFailed",
            (
                SELECT COUNT("Id") FROM {_publishedTable} WHERE "IntentType" IN (0, 1) AND "StatusName" = 'Delayed'
            ) AS "PublishedDelayed",
            (
                SELECT COUNT("Id") FROM {_publishedTable} WHERE "IntentType" IN (0, 1) AND "NextRetryAt" IS NOT NULL
            ) AS "PublishedPendingRetry",
            (
                SELECT COUNT("Id") FROM {_receivedTable} WHERE "IntentType" IN (0, 1) AND "NextRetryAt" IS NOT NULL
            ) AS "ReceivedPendingRetry";
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
                ? @"""Id"",""MessageId"",""Version"",""Name"",CAST(NULL AS VARCHAR(200)) AS ""Group"",""Content"",""IntentType"",""Retries"",""Added"",""ExpiresAt"",""StatusName"",""NextRetryAt"",""LockedUntil"""
                : @"""Id"",""MessageId"",""Version"",""Name"",""Group"",""Content"",""IntentType"",""Retries"",""Added"",""ExpiresAt"",""StatusName"",""NextRetryAt"",""LockedUntil""";
        var where = " AND \"IntentType\" IN (0, 1)";

        if (query.StatusName is not null)
        {
            where += " AND \"StatusName\" = @StatusName";
        }

        if (!string.IsNullOrEmpty(query.Name))
        {
            where += " AND \"Name\" = @Name";
        }

        if (!string.IsNullOrEmpty(query.Group))
        {
            where += " AND \"Group\" = @Group";
        }

        if (!string.IsNullOrEmpty(query.Content))
        {
            where += " AND \"Content\" ILIKE @Content ESCAPE '\\'";
        }

        if (query.Lane is { })
        {
            where += " AND \"IntentType\" = @IntentType";
        }

        // Keep the total count in a separate query: COUNT(*) OVER() returns no count row when OFFSET/LIMIT yields
        // an empty later page, which breaks pagination metadata even though matching rows still exist.
        var countQuery = $"SELECT COUNT(\"Id\") FROM {tableName} WHERE 1=1 {where}";

        var sqlQuery =
            $"SELECT {selectColumns} FROM {tableName} WHERE 1=1 {where} ORDER BY \"Added\" DESC OFFSET @Offset LIMIT @Limit";

        await using var connection = _options.CreateConnection();

        // Escape LIKE metacharacters so a literal %, _ or \ in the user's search term matches
        // literally instead of acting as a wildcard (paired with ESCAPE '\' in the ILIKE clause).
        var contentLike = $"%{_EscapeLike(query.Content)}%";

        // CurrentPage is zero-based (it is returned as IndexPage.Index). Clamping keeps a negative
        // index off the wire, where it would be rejected as a negative OFFSET.
        var currentPage = Math.Max(query.CurrentPage, 0);

        object[] sqlParams =
        [
            new NpgsqlParameter("@StatusName", query.StatusName?.ToString("G") ?? string.Empty),
            new NpgsqlParameter("@Group", query.Group ?? string.Empty),
            new NpgsqlParameter("@Name", query.Name ?? string.Empty),
            new NpgsqlParameter("@Content", contentLike),
            new NpgsqlParameter(
                "@IntentType",
                query.Lane is null ? 0 : MessageLaneCompatibility.ToPersistedValue(query.Lane.Value)
            ),
            new NpgsqlParameter("@Offset", (long)currentPage * query.PageSize),
            new NpgsqlParameter("@Limit", query.PageSize),
        ];

        var totalCount = await connection
            .ExecuteScalarAsync(
                countQuery,
                commandTimeout: _messagingOptions.CommandTimeout,
                sqlParams: sqlParams,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        if (totalCount == 0)
        {
            return new([], currentPage, query.PageSize, 0);
        }

        var items = await connection
            .ExecuteReaderAsync(
                sqlQuery,
                async (reader, token) =>
                {
                    var messages = new List<MessageView>();

                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        var index = 0;
                        var message = new MessageView
                        {
                            StorageId = reader.GetGuid(index++),
                            MessageId = reader.GetString(index++),
                            Version = reader.GetString(index++),
                            Name = reader.GetString(index++),
                            Group = await reader.IsDBNullAsync(index++, token).ConfigureAwait(false)
                                ? null
                                : reader.GetString(index - 1),
                            Content = await reader.IsDBNullAsync(index++, token).ConfigureAwait(false)
                                ? null
                                : reader.GetString(index - 1),
                            Lane = MessageLaneCompatibility.FromPersistedValue(reader.GetInt16(index++)),
                            Retries = reader.GetInt32(index++),
                            Added = await reader
                                .GetFieldValueAsync<DateTimeOffset>(index++, token)
                                .ConfigureAwait(false),
                            ExpiresAt = await reader.IsDBNullAsync(index++, token).ConfigureAwait(false)
                                ? null
                                : await reader
                                    .GetFieldValueAsync<DateTimeOffset>(index - 1, token)
                                    .ConfigureAwait(false),
                            StatusName = Enum.Parse<StatusName>(reader.GetString(index++)),
                            NextRetryAt = await reader.IsDBNullAsync(index++, token).ConfigureAwait(false)
                                ? null
                                : await reader
                                    .GetFieldValueAsync<DateTimeOffset>(index - 1, token)
                                    .ConfigureAwait(false),
                            LockedUntil = await reader.IsDBNullAsync(index++, token).ConfigureAwait(false)
                                ? null
                                : await reader
                                    .GetFieldValueAsync<DateTimeOffset>(index - 1, token)
                                    .ConfigureAwait(false),
                        };
                        var delivery = DeliveryMetadata.ReadStoredEnvelope(serializer, message.Content);
                        message.RequestedDeliveryMode = delivery.RequestedDeliveryMode;
                        message.ResolvedDeliveryMode = delivery.ResolvedDeliveryMode;
                        messages.Add(message);
                    }
                    return messages;
                },
                commandTimeout: _messagingOptions.CommandTimeout,
                sqlParams: sqlParams,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        return new(items, currentPage, query.PageSize, (int)Math.Min(totalCount, int.MaxValue));
    }

    /// <inheritdoc />
    public async ValueTask<IndexPage<UnknownLaneMessageView>> GetUnknownLaneMessagesAsync(
        UnknownLaneMessageQuery query,
        CancellationToken cancellationToken = default
    )
    {
        var tableName = query.MessageType switch
        {
            MessageType.Publish => _publishedTable,
            MessageType.Subscribe => _receivedTable,
            _ => throw new ArgumentOutOfRangeException(nameof(query), query.MessageType, "Unknown message type."),
        };
        var currentPage = Math.Max(query.CurrentPage, 1);
        var pageSize = query.PageSize <= 0 ? 50 : Math.Min(query.PageSize, 200);
        var offset = (long)(currentPage - 1) * pageSize;
        var countSql = $"SELECT COUNT(\"Id\") FROM {tableName} WHERE \"IntentType\" NOT IN (0, 1)";
        var pageSql = $"""
            SELECT "Id", "IntentType", "Name", "StatusName", "Added", "NextRetryAt", "LockedUntil"
            FROM {tableName}
            WHERE "IntentType" NOT IN (0, 1)
            ORDER BY "Added", "Id"
            OFFSET @Offset LIMIT @Limit;
            """;
        object[] sqlParams = [new NpgsqlParameter("@Offset", offset), new NpgsqlParameter("@Limit", pageSize)];

        await using var connection = _options.CreateConnection();
        var totalCount = await connection
            .ExecuteScalarAsync(
                countSql,
                commandTimeout: _messagingOptions.CommandTimeout,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
        var items = await connection
            .ExecuteReaderAsync(
                pageSql,
                async (reader, token) =>
                {
                    var messages = new List<UnknownLaneMessageView>();
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        messages.Add(
                            new UnknownLaneMessageView
                            {
                                StorageId = reader.GetGuid(0),
                                MessageType = query.MessageType,
                                RawLane = reader.GetInt16(1),
                                Name = reader.GetString(2),
                                StatusName = Enum.Parse<StatusName>(reader.GetString(3)),
                                Added = await reader.GetFieldValueAsync<DateTimeOffset>(4, token).ConfigureAwait(false),
                                NextRetryAt = await reader.IsDBNullAsync(5, token).ConfigureAwait(false)
                                    ? null
                                    : await reader.GetFieldValueAsync<DateTimeOffset>(5, token).ConfigureAwait(false),
                                LockedUntil = await reader.IsDBNullAsync(6, token).ConfigureAwait(false)
                                    ? null
                                    : await reader.GetFieldValueAsync<DateTimeOffset>(6, token).ConfigureAwait(false),
                            }
                        );
                    }

                    return messages;
                },
                commandTimeout: _messagingOptions.CommandTimeout,
                sqlParams: sqlParams,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        return new(items, currentPage - 1, pageSize, (int)Math.Min(totalCount, int.MaxValue));
    }

    /// <summary>Returns the total count of published messages in the <c>Failed</c> state.</summary>
    public ValueTask<long> GetPublishedFailedCountAsync(CancellationToken cancellationToken = default)
    {
        return _GetNumberOfMessage(_publishedTable, nameof(StatusName.Failed), cancellationToken);
    }

    /// <summary>Returns the total count of published messages in the <c>Succeeded</c> state.</summary>
    public ValueTask<long> GetPublishedSucceededCountAsync(CancellationToken cancellationToken = default)
    {
        return _GetNumberOfMessage(_publishedTable, nameof(StatusName.Succeeded), cancellationToken);
    }

    /// <summary>Returns the total count of received messages in the <c>Failed</c> state.</summary>
    public ValueTask<long> GetReceivedFailedCountAsync(CancellationToken cancellationToken = default)
    {
        return _GetNumberOfMessage(_receivedTable, nameof(StatusName.Failed), cancellationToken);
    }

    /// <summary>Returns the total count of received messages in the <c>Succeeded</c> state.</summary>
    public ValueTask<long> GetReceivedSucceededCountAsync(CancellationToken cancellationToken = default)
    {
        return _GetNumberOfMessage(_receivedTable, nameof(StatusName.Succeeded), cancellationToken);
    }

    /// <summary>
    /// Returns a dictionary of UTC hour buckets to <c>Succeeded</c> message counts for the past 24 hours,
    /// from the published or received table depending on <paramref name="type"/>.
    /// </summary>
    public async ValueTask<IReadOnlyDictionary<DateTimeOffset, int>> GetHourlySucceededJobsAsync(
        MessageType type,
        CancellationToken cancellationToken = default
    )
    {
        var tableName = type == MessageType.Publish ? _publishedTable : _receivedTable;

        return await _GetHourlyTimelineStats(tableName, nameof(StatusName.Succeeded), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Returns a dictionary of UTC hour buckets to <c>Failed</c> message counts for the past 24 hours,
    /// from the published or received table depending on <paramref name="type"/>.
    /// </summary>
    public async ValueTask<IReadOnlyDictionary<DateTimeOffset, int>> GetHourlyFailedJobsAsync(
        MessageType type,
        CancellationToken cancellationToken = default
    )
    {
        var tableName = type == MessageType.Publish ? _publishedTable : _receivedTable;
        return await _GetHourlyTimelineStats(tableName, nameof(StatusName.Failed), cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask<long> _GetNumberOfMessage(
        string tableName,
        string statusName,
        CancellationToken cancellationToken = default
    )
    {
        var sqlQuery =
            $"SELECT COUNT(\"Id\") FROM {tableName} WHERE \"IntentType\" IN (0, 1) AND \"StatusName\" = @State";

        await using var connection = _options.CreateConnection();

        object[] sqlParams = [new NpgsqlParameter("@State", statusName)];

        return await connection
            .ExecuteScalarAsync(
                sqlQuery,
                commandTimeout: _messagingOptions.CommandTimeout,
                sqlParams: sqlParams,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
    }

    private Task<IReadOnlyDictionary<DateTimeOffset, int>> _GetHourlyTimelineStats(
        string tableName,
        string statusName,
        CancellationToken cancellationToken = default
    )
    {
        // Buckets cover the current UTC hour and the 23 preceding hours, keyed by hour start
        // (a DateTimeOffset with zero offset), newest first.
        var currentHour = timeProvider.GetUtcNow().TruncateToHours();

        var keyMaps = new Dictionary<string, DateTimeOffset>(capacity: 24, StringComparer.Ordinal);

        for (var i = 0; i < 24; i++)
        {
            var bucket = currentHour.AddHours(-i);
            keyMaps[bucket.ToString("yyyy-MM-dd-HH", CultureInfo.InvariantCulture)] = bucket;
        }

        var oldestHour = currentHour.AddHours(-23);

        return _GetTimelineStats(
            tableName,
            statusName,
            keyMaps,
            oldestHour,
            currentHour.AddHours(1),
            cancellationToken
        );
    }

    private async Task<IReadOnlyDictionary<DateTimeOffset, int>> _GetTimelineStats(
        string tableName,
        string statusName,
        Dictionary<string, DateTimeOffset> keyMaps,
        DateTimeOffset minAdded,
        DateTimeOffset maxAddedExclusive,
        CancellationToken cancellationToken = default
    )
    {
        var sqlQuery = $"""
            WITH Aggr AS (
                -- AT TIME ZONE 'UTC' renders the timestamptz in UTC regardless of the session TimeZone,
                -- matching the UTC hour-bucket keys built in C#. HH24 (24-hour) matches the C# key built
                -- with ToString("yyyy-MM-dd-HH"); Postgres 'HH' is 12-hour, which silently mismatched
                -- buckets for hours 00 and 13-23.
                SELECT to_char("Added" AT TIME ZONE 'UTC','yyyy-MM-dd-HH24') AS "Key",
                COUNT("Id") AS "Count"
                FROM {tableName}
                    WHERE "IntentType" IN (0, 1) AND "StatusName" = @StatusName AND "Added" >= @MinAdded AND "Added" < @MaxAdded
                GROUP BY to_char("Added" AT TIME ZONE 'UTC', 'yyyy-MM-dd-HH24')
            )
            SELECT "Key","Count" from Aggr;
            """;

        object[] sqlParams =
        [
            new NpgsqlParameter("@StatusName", statusName),
            new NpgsqlParameter("@MinAdded", minAdded.UtcDateTime),
            new NpgsqlParameter("@MaxAdded", maxAddedExclusive.UtcDateTime),
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
                        // COUNT() is bigint in PostgreSQL; read as Int64 and saturate to int (GetStatisticsAsync
                        // uses GetInt64 for the same columns). Avoids an InvalidCastException past int.MaxValue rows.
                        dictionary.Add(reader.GetString(0), (int)Math.Min(reader.GetInt64(1), int.MaxValue));
                    }

                    return dictionary;
                },
                commandTimeout: _messagingOptions.CommandTimeout,
                sqlParams: sqlParams,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        var result = new Dictionary<DateTimeOffset, int>(capacity: keyMaps.Count);

        foreach (var (key, hourBucket) in keyMaps)
        {
            var value = valuesMap.GetValueOrDefault(key, 0);
            result.Add(hourBucket, value);
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

        var sql = _BuildSelectMessageSql(tableName, "WHERE \"Id\" = ANY(@Ids) AND \"IntentType\" IN (0, 1)");

        await using var connection = _options.CreateConnection();

        return await connection
            .ExecuteReaderAsync(
                sql,
                async (reader, token) =>
                {
                    var messages = new List<MediumMessage>();

                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        messages.Add(await _ReadMediumMessageAsync(reader, token).ConfigureAwait(false));
                    }

                    return (IReadOnlyList<MediumMessage>)messages;
                },
                commandTimeout: _messagingOptions.CommandTimeout,
                sqlParams: [new NpgsqlParameter("@Ids", storageIds.ToArray())],
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
        var sql = _BuildSelectMessageSql(tableName, "WHERE \"Id\"=@Id AND \"IntentType\" IN (0, 1)");

        await using var connection = _options.CreateConnection();

        var mediumMessage = await connection
            .ExecuteReaderAsync(
                sql,
                async (reader, token) =>
                {
                    MediumMessage? message = null;

                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        message = await _ReadMediumMessageAsync(reader, token).ConfigureAwait(false);
                    }

                    return message;
                },
                commandTimeout: _messagingOptions.CommandTimeout,
                sqlParams: [new NpgsqlParameter("@Id", id)],
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        return mediumMessage;
    }

    // Shared SELECT used by _GetMessageAsync / _GetMessagesAsync. The only structural difference is the
    // ExceptionInfo column (received table only) and the WHERE clause, so both are parameterized here to
    // keep the column list and reader (see _ReadMediumMessageAsync) in a single place.
    private string _BuildSelectMessageSql(string tableName, string whereClause)
    {
        var exceptionInfoSql = string.Equals(tableName, _receivedTable, StringComparison.Ordinal)
            ? @"""ExceptionInfo"""
            : "NULL AS \"ExceptionInfo\"";

        return $@"SELECT ""Id"" AS ""StorageId"", ""Content"", ""IntentType"", ""Added"", ""ExpiresAt"", ""Retries"", {exceptionInfoSql}, ""NextRetryAt"", ""LockedUntil"" FROM {tableName} {whereClause}";
    }

    private async Task<MediumMessage> _ReadMediumMessageAsync(DbDataReader reader, CancellationToken token)
    {
        var content = reader.GetString(1);

        return new MediumMessage
        {
            StorageId = reader.GetGuid(0),
            Origin = serializer.Deserialize(content)!,
            Content = content,
            Lane = MessageLaneCompatibility.FromPersistedValue(reader.GetInt16(2)),
            Added = await reader.GetFieldValueAsync<DateTimeOffset>(3, token).ConfigureAwait(false),
            ExpiresAt = await reader.IsDBNullAsync(4, token).ConfigureAwait(false)
                ? null
                : await reader.GetFieldValueAsync<DateTimeOffset>(4, token).ConfigureAwait(false),
            Retries = reader.GetInt32(5),
            ExceptionInfo = await reader.IsDBNullAsync(6, token).ConfigureAwait(false) ? null : reader.GetString(6),
            NextRetryAt = await reader.IsDBNullAsync(7, token).ConfigureAwait(false)
                ? null
                : await reader.GetFieldValueAsync<DateTimeOffset>(7, token).ConfigureAwait(false),
            LockedUntil = await reader.IsDBNullAsync(8, token).ConfigureAwait(false)
                ? null
                : await reader.GetFieldValueAsync<DateTimeOffset>(8, token).ConfigureAwait(false),
        };
    }

    private static string _EscapeLike(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        // Escape the ESCAPE character first, then the LIKE wildcards, so user text matches literally.
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
    }
}
