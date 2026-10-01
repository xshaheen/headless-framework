// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Checks;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Serialization;
using Headless.Primitives;
using Headless.Sql;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Persistence;

#pragma warning disable CA2100 // SQL text is rendered from dialect output, table names, and fixed fragments; every value is a parameter.

/// <summary>
/// The relational <see cref="IMonitoringApi"/>: message lookups, counts, pages, and hourly timelines for the dashboard,
/// written once over the storage's dialect.
/// </summary>
internal sealed class RelationalMonitoringApi(
    RelationalMessagingStorage storage,
    MessagingTables tables,
    IOptions<MessagingOptions> messagingOptions,
    ISerializer serializer,
    TimeProvider timeProvider
) : IMonitoringApi
{
    private const int _HourlyBuckets = 24;

    private readonly RelationalMessagingStorage _storage = Argument.IsNotNull(storage);
    private readonly ISqlDialect _dialect = storage.Dialect;
    private readonly MessagingTables _t = Argument.IsNotNull(tables);
    private readonly MessagingOptions _messagingOptions = messagingOptions.Value;
    private readonly string _publishedTable = tables.Published;
    private readonly string _receivedTable = tables.Received;

    private int CommandTimeoutSeconds =>
        (int)Math.Min(Math.Ceiling(_messagingOptions.CommandTimeout.TotalSeconds), int.MaxValue);

    /// <summary>Returns a single published message by its storage identifier, or <see langword="null"/> if not found.</summary>
    public async ValueTask<MediumMessage?> GetPublishedMessageAsync(
        Guid id,
        CancellationToken cancellationToken = default
    )
    {
        var messages = await _GetMessagesAsync(_publishedTable, [id], cancellationToken).ConfigureAwait(false);
        return messages.Count == 0 ? null : messages[0];
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
        var messages = await _GetMessagesAsync(_receivedTable, [id], cancellationToken).ConfigureAwait(false);
        return messages.Count == 0 ? null : messages[0];
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
        var known = $"{_t.IntentType} IN (0, 1)";
        var sql = $"""
            SELECT
                (SELECT COUNT(*) FROM {_publishedTable} WHERE {known} AND {_t.StatusName} = '{nameof(
                StatusName.Succeeded
            )}'),
                (SELECT COUNT(*) FROM {_receivedTable} WHERE {known} AND {_t.StatusName} = '{nameof(
                StatusName.Succeeded
            )}'),
                (SELECT COUNT(*) FROM {_publishedTable} WHERE {known} AND {_t.StatusName} = '{nameof(
                StatusName.Failed
            )}'),
                (SELECT COUNT(*) FROM {_receivedTable} WHERE {known} AND {_t.StatusName} = '{nameof(
                StatusName.Failed
            )}'),
                (SELECT COUNT(*) FROM {_publishedTable} WHERE {known} AND {_t.StatusName} = '{nameof(
                StatusName.Delayed
            )}'),
                (SELECT COUNT(*) FROM {_publishedTable} WHERE {known} AND {_t.NextRetryAt} IS NOT NULL),
                (SELECT COUNT(*) FROM {_receivedTable} WHERE {known} AND {_t.NextRetryAt} IS NOT NULL);
            """;

        await using var connection = _storage.CreateConnection();
        return await RelationalCommand
            .ExecuteReaderAsync(
                connection,
                transaction: null,
                sql,
                CommandTimeoutSeconds,
                bind: null,
                static async (reader, ct) =>
                {
                    var statistics = new StatisticsView();
                    if (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        statistics.PublishedSucceeded = _Int64(reader, 0);
                        statistics.ReceivedSucceeded = _Int64(reader, 1);
                        statistics.PublishedFailed = _Int64(reader, 2);
                        statistics.ReceivedFailed = _Int64(reader, 3);
                        statistics.PublishedDelayed = _Int64(reader, 4);
                        statistics.PublishedPendingRetry = _Int64(reader, 5);
                        statistics.ReceivedPendingRetry = _Int64(reader, 6);
                    }

                    return statistics;
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Returns a paginated list of messages from either the published or received table,
    /// filtered by the criteria in <paramref name="query"/> (status, name, consumer identity, content substring, intent type).
    /// </summary>
    public async ValueTask<IndexPage<MessageView>> GetMessagesAsync(
        MessageQuery query,
        CancellationToken cancellationToken = default
    )
    {
        var publish = query.MessageType == MessageType.Publish;
        var tableName = publish ? _publishedTable : _receivedTable;
        // Only received rows belong to a consumer; the published table has no identity column.
        var consumerIdentity = publish ? $"NULL AS {_t.ConsumerIdentity}" : _t.ConsumerIdentity;
        var selectColumns =
            $"{_t.Id},{_t.MessageId},{_t.Version},{_t.Name},{consumerIdentity},{_t.Content},{_t.IntentType},{_t.Retries},{_t.Added},{_t.ExpiresAt},{_t.StatusName},{_t.NextRetryAt},{_t.LockedUntil}";
        var where = $"{_t.IntentType} IN (0, 1)";

        if (query.StatusName is not null)
        {
            where += $" AND {_t.StatusName} = @StatusName";
        }

        if (!string.IsNullOrEmpty(query.Name))
        {
            where += $" AND {_t.Name} = @Name";
        }

        if (!publish && !string.IsNullOrEmpty(query.ConsumerIdentity))
        {
            where += $" AND {_t.ConsumerIdentity} = @ConsumerIdentity";
        }

        if (!string.IsNullOrEmpty(query.Content))
        {
            where += $" AND {_dialect.LikeIgnoringCase(_t.Content, "Content")}";
        }

        if (query.Lane is { })
        {
            where += $" AND {_t.IntentType} = @IntentType";
        }

        // CurrentPage is zero-based (it is returned as IndexPage.Index). Clamping keeps a negative
        // index off the wire, where it would be rejected as a negative OFFSET.
        var currentPage = Math.Max(query.CurrentPage, 0);

        void bind(DbCommand command)
        {
            _dialect.AddParameter(command, "StatusName", SqlColumnType.Text(50), query.StatusName?.ToString("G"));
            _dialect.AddParameter(command, "ConsumerIdentity", SqlColumnType.KeyText(200), query.ConsumerIdentity);
            _dialect.AddParameter(command, "Name", SqlColumnType.Text(200), query.Name);
            // Escaped so a literal %, _, [ or \\ in the user's search term matches literally instead of as a wildcard.
            _dialect.AddParameter(command, "Content", SqlColumnType.Text(-1), $"%{EscapeLike(query.Content)}%");
            _dialect.AddParameter(
                command,
                "IntentType",
                SqlColumnType.Int16,
                query.Lane is null ? (short)0 : MessageLaneCompatibility.ToPersistedValue(query.Lane.Value)
            );
        }

        await using var connection = _storage.CreateConnection();

        // Keep the total count in a separate query: COUNT(*) OVER() returns no count row when the page is past the
        // last row, which breaks pagination metadata even though matching rows still exist.
        var totalCount = Convert.ToInt64(
            await RelationalCommand
                .ExecuteScalarAsync(
                    connection,
                    transaction: null,
                    $"SELECT COUNT(*) FROM {tableName} WHERE {where};",
                    CommandTimeoutSeconds,
                    bind,
                    cancellationToken
                )
                .ConfigureAwait(false),
            CultureInfo.InvariantCulture
        );

        if (totalCount == 0)
        {
            return new([], currentPage, query.PageSize, 0);
        }

        var items = await RelationalCommand
            .ExecuteReaderAsync(
                connection,
                transaction: null,
                $"SELECT {selectColumns} FROM {tableName} WHERE {where} ORDER BY {_t.Added} DESC {_dialect.Limit("Limit", "Offset")};",
                CommandTimeoutSeconds,
                command =>
                {
                    bind(command);
                    _dialect.AddParameter(command, "Offset", SqlColumnType.Int64, (long)currentPage * query.PageSize);
                    _dialect.AddParameter(command, "Limit", SqlColumnType.Int32, query.PageSize);
                },
                async (reader, token) =>
                {
                    var messages = new List<MessageView>();

                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        var message = new MessageView
                        {
                            StorageId = reader.GetGuid(0),
                            MessageId = reader.GetString(1),
                            Version = reader.GetString(2),
                            Name = reader.GetString(3),
                            ConsumerIdentity = await _StringAsync(reader, 4, token).ConfigureAwait(false),
                            Content = await _StringAsync(reader, 5, token).ConfigureAwait(false),
                            Lane = MessageLaneCompatibility.FromPersistedValue(reader.GetInt16(6)),
                            Retries = reader.GetInt32(7),
                            Added = await reader.GetFieldValueAsync<DateTimeOffset>(8, token).ConfigureAwait(false),
                            ExpiresAt = await _InstantAsync(reader, 9, token).ConfigureAwait(false),
                            StatusName = Enum.Parse<StatusName>(reader.GetString(10)),
                            NextRetryAt = await _InstantAsync(reader, 11, token).ConfigureAwait(false),
                            LockedUntil = await _InstantAsync(reader, 12, token).ConfigureAwait(false),
                        };
                        var delivery = DeliveryMetadata.ReadStoredEnvelope(serializer, message.Content);
                        message.RequestedDeliveryMode = delivery.RequestedDeliveryMode;
                        message.ResolvedDeliveryMode = delivery.ResolvedDeliveryMode;
                        message.IsCoordinated = delivery.IsCoordinated;
                        messages.Add(message);
                    }

                    return messages;
                },
                cancellationToken
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

        await using var connection = _storage.CreateConnection();
        var totalCount = Convert.ToInt64(
            await RelationalCommand
                .ExecuteScalarAsync(
                    connection,
                    transaction: null,
                    $"SELECT COUNT(*) FROM {tableName} WHERE {_t.IntentType} NOT IN (0, 1);",
                    CommandTimeoutSeconds,
                    bind: null,
                    cancellationToken
                )
                .ConfigureAwait(false),
            CultureInfo.InvariantCulture
        );
        var items = await RelationalCommand
            .ExecuteReaderAsync(
                connection,
                transaction: null,
                $"""
                SELECT {_t.Id},{_t.IntentType},{_t.Name},{_t.StatusName},{_t.Added},{_t.NextRetryAt},{_t.LockedUntil}
                FROM {tableName}
                WHERE {_t.IntentType} NOT IN (0, 1)
                ORDER BY {_t.Added},{_t.Id}
                {_dialect.Limit("Limit", "Offset")};
                """,
                CommandTimeoutSeconds,
                command =>
                {
                    _dialect.AddParameter(command, "Offset", SqlColumnType.Int64, offset);
                    _dialect.AddParameter(command, "Limit", SqlColumnType.Int32, pageSize);
                },
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
                                NextRetryAt = await _InstantAsync(reader, 5, token).ConfigureAwait(false),
                                LockedUntil = await _InstantAsync(reader, 6, token).ConfigureAwait(false),
                            }
                        );
                    }

                    return messages;
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        return new(items, currentPage - 1, pageSize, (int)Math.Min(totalCount, int.MaxValue));
    }

    /// <summary>Returns the total count of published messages in the <c>Failed</c> state.</summary>
    public ValueTask<long> GetPublishedFailedCountAsync(CancellationToken cancellationToken = default)
    {
        return _GetNumberOfMessageAsync(_publishedTable, nameof(StatusName.Failed), cancellationToken);
    }

    /// <summary>Returns the total count of published messages in the <c>Succeeded</c> state.</summary>
    public ValueTask<long> GetPublishedSucceededCountAsync(CancellationToken cancellationToken = default)
    {
        return _GetNumberOfMessageAsync(_publishedTable, nameof(StatusName.Succeeded), cancellationToken);
    }

    /// <summary>Returns the total count of received messages in the <c>Failed</c> state.</summary>
    public ValueTask<long> GetReceivedFailedCountAsync(CancellationToken cancellationToken = default)
    {
        return _GetNumberOfMessageAsync(_receivedTable, nameof(StatusName.Failed), cancellationToken);
    }

    /// <summary>Returns the total count of received messages in the <c>Succeeded</c> state.</summary>
    public ValueTask<long> GetReceivedSucceededCountAsync(CancellationToken cancellationToken = default)
    {
        return _GetNumberOfMessageAsync(_receivedTable, nameof(StatusName.Succeeded), cancellationToken);
    }

    /// <summary>
    /// Returns a dictionary of UTC hour buckets to <c>Succeeded</c> message counts for the past 24 hours,
    /// from the published or received table depending on <paramref name="type"/>.
    /// </summary>
    public ValueTask<IReadOnlyDictionary<DateTimeOffset, int>> GetHourlySucceededJobsAsync(
        MessageType type,
        CancellationToken cancellationToken = default
    )
    {
        var tableName = type == MessageType.Publish ? _publishedTable : _receivedTable;
        return _GetHourlyTimelineAsync(tableName, nameof(StatusName.Succeeded), cancellationToken);
    }

    /// <summary>
    /// Returns a dictionary of UTC hour buckets to <c>Failed</c> message counts for the past 24 hours,
    /// from the published or received table depending on <paramref name="type"/>.
    /// </summary>
    public ValueTask<IReadOnlyDictionary<DateTimeOffset, int>> GetHourlyFailedJobsAsync(
        MessageType type,
        CancellationToken cancellationToken = default
    )
    {
        var tableName = type == MessageType.Publish ? _publishedTable : _receivedTable;
        return _GetHourlyTimelineAsync(tableName, nameof(StatusName.Failed), cancellationToken);
    }

    /// <summary>
    /// Escapes the escape character and the LIKE wildcards (<c>%</c>, <c>_</c>, and SQL Server's <c>[</c>) so user text
    /// matches literally under <c>ESCAPE '\'</c>.
    /// </summary>
    internal static string EscapeLike(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal);
    }

    private async ValueTask<long> _GetNumberOfMessageAsync(
        string tableName,
        string statusName,
        CancellationToken cancellationToken
    )
    {
        await using var connection = _storage.CreateConnection();
        return Convert.ToInt64(
            await RelationalCommand
                .ExecuteScalarAsync(
                    connection,
                    transaction: null,
                    $"SELECT COUNT(*) FROM {_dialect.ReadWithoutWaiting(tableName)} WHERE {_t.IntentType} IN (0, 1) AND {_t.StatusName} = @StatusName;",
                    CommandTimeoutSeconds,
                    command => _dialect.AddParameter(command, "StatusName", SqlColumnType.Text(50), statusName),
                    cancellationToken
                )
                .ConfigureAwait(false),
            CultureInfo.InvariantCulture
        );
    }

    /// <summary>
    /// Counts the rows added in each of the current UTC hour and the 23 before it, keyed by hour start (a zero-offset
    /// instant). The bucket boundaries are bound as instants, so every engine counts the same rows whatever the offset
    /// a row was written with, and one range read of the (status, added) index serves all 24 buckets.
    /// </summary>
    private async ValueTask<IReadOnlyDictionary<DateTimeOffset, int>> _GetHourlyTimelineAsync(
        string tableName,
        string statusName,
        CancellationToken cancellationToken
    )
    {
        var currentHour = timeProvider.GetUtcNow().TruncateToHours();
        var buckets = new DateTimeOffset[_HourlyBuckets + 1];
        for (var i = 0; i <= _HourlyBuckets; i++)
        {
            // buckets[0] is the oldest hour's start; buckets[24] is the end of the current hour.
            buckets[i] = currentHour.AddHours(i - (_HourlyBuckets - 1));
        }

        var counts = string.Join(
            ',',
            Enumerable
                .Range(0, _HourlyBuckets)
                .Select(i =>
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"SUM(CASE WHEN {_t.Added} >= @B{i} AND {_t.Added} < @B{i + 1} THEN 1 ELSE 0 END)"
                    )
                )
        );
        var sql =
            $"SELECT {counts} FROM {_dialect.ReadWithoutWaiting(tableName)} WHERE {_t.IntentType} IN (0, 1) AND {_t.StatusName} = @StatusName AND {_t.Added} >= @B0 AND {_t.Added} < @B{_HourlyBuckets.ToString(CultureInfo.InvariantCulture)};";

        await using var connection = _storage.CreateConnection();
        var values = await RelationalCommand
            .ExecuteReaderAsync(
                connection,
                transaction: null,
                sql,
                CommandTimeoutSeconds,
                command =>
                {
                    _dialect.AddParameter(command, "StatusName", SqlColumnType.Text(50), statusName);
                    for (var i = 0; i <= _HourlyBuckets; i++)
                    {
                        _dialect.AddParameter(
                            command,
                            "B" + i.ToString(CultureInfo.InvariantCulture),
                            SqlColumnType.Timestamp,
                            buckets[i]
                        );
                    }
                },
                static async (reader, ct) =>
                {
                    var row = new int[_HourlyBuckets];
                    if (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        for (var i = 0; i < _HourlyBuckets; i++)
                        {
                            // SUM over no rows is NULL; a count past int.MaxValue saturates rather than overflowing.
                            row[i] = await reader.IsDBNullAsync(i, ct).ConfigureAwait(false)
                                ? 0
                                : (int)Math.Min(_Int64(reader, i), int.MaxValue);
                        }
                    }

                    return row;
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        var result = new Dictionary<DateTimeOffset, int>(capacity: _HourlyBuckets);
        for (var i = _HourlyBuckets - 1; i >= 0; i--)
        {
            result.Add(buckets[i], values[i]);
        }

        return result;
    }

    private async Task<IReadOnlyList<MediumMessage>> _GetMessagesAsync(
        string tableName,
        IReadOnlyList<Guid> storageIds,
        CancellationToken cancellationToken
    )
    {
        if (storageIds.Count == 0)
        {
            return [];
        }

        // Only the received table records a failure. One list parameter keeps the statement text, and its cached
        // plan, the same whatever the number of ids.
        var exceptionInfo = string.Equals(tableName, _receivedTable, StringComparison.Ordinal)
            ? _t.ExceptionInfo
            : $"NULL AS {_t.ExceptionInfo}";
        var sql =
            $"SELECT {_t.Id},{_t.Content},{_t.IntentType},{_t.Added},{_t.ExpiresAt},{_t.Retries},{exceptionInfo},{_t.NextRetryAt},{_t.LockedUntil} FROM {_dialect.ReadWithoutWaiting(tableName)} WHERE {_dialect.InList(_t.Id, "Ids", SqlColumnType.Guid)} AND {_t.IntentType} IN (0, 1);";

        await using var connection = _storage.CreateConnection();
        return await RelationalCommand
            .ExecuteReaderAsync(
                connection,
                transaction: null,
                sql,
                CommandTimeoutSeconds,
                command => _dialect.AddListParameter(command, "Ids", SqlColumnType.Guid, storageIds),
                async (reader, token) =>
                {
                    var messages = new List<MediumMessage>();

                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        var content = reader.GetString(1);
                        messages.Add(
                            new MediumMessage
                            {
                                StorageId = reader.GetGuid(0),
                                Origin = serializer.Deserialize(content)!,
                                Content = content,
                                Lane = MessageLaneCompatibility.FromPersistedValue(reader.GetInt16(2)),
                                Added = await reader.GetFieldValueAsync<DateTimeOffset>(3, token).ConfigureAwait(false),
                                ExpiresAt = await _InstantAsync(reader, 4, token).ConfigureAwait(false),
                                Retries = reader.GetInt32(5),
                                ExceptionInfo = await _StringAsync(reader, 6, token).ConfigureAwait(false),
                                NextRetryAt = await _InstantAsync(reader, 7, token).ConfigureAwait(false),
                                LockedUntil = await _InstantAsync(reader, 8, token).ConfigureAwait(false),
                            }
                        );
                    }

                    return (IReadOnlyList<MediumMessage>)messages;
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private static long _Int64(DbDataReader reader, int ordinal)
    {
        // COUNT is bigint on PostgreSQL and int on SQL Server, and SUM follows its input on each.
        return Convert.ToInt64(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    private static async Task<DateTimeOffset?> _InstantAsync(DbDataReader reader, int ordinal, CancellationToken ct)
    {
        return await reader.IsDBNullAsync(ordinal, ct).ConfigureAwait(false)
            ? null
            : await reader.GetFieldValueAsync<DateTimeOffset>(ordinal, ct).ConfigureAwait(false);
    }

    private static async Task<string?> _StringAsync(DbDataReader reader, int ordinal, CancellationToken ct)
    {
        return await reader.IsDBNullAsync(ordinal, ct).ConfigureAwait(false) ? null : reader.GetString(ordinal);
    }
}

#pragma warning restore CA2100
