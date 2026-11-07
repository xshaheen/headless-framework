// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Checks;
using Headless.Messaging.Internal;
using Headless.Primitives;
using Headless.Sql;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Persistence;

/// <summary>
/// The relational <see cref="IMonitoringApi"/>: message lookups, counts, pages, and hourly timelines for the dashboard,
/// written once over the storage's dialect.
/// </summary>
internal sealed class RelationalMonitoringApi(
    RelationalMessagingStorage storage,
    MessagingTables tables,
    IOptions<MessagingOptions> messagingOptions,
    IMessageSerializer serializer,
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
    /// Returns the pending rows of both tables and the age of the oldest of each, in one statement read on the
    /// database clock. See <see cref="BacklogView"/> for which rows count.
    /// </summary>
    public ValueTask<BacklogView> GetBacklogAsync(CancellationToken cancellationToken = default)
    {
        return _GetBacklogAsync(includeReceived: true, cancellationToken);
    }

    /// <summary>
    /// The published half of <see cref="GetBacklogAsync"/>, for an additional outbox, whose database holds no
    /// received table. <see cref="BacklogView.ReceivedPending"/> is always zero.
    /// </summary>
    internal ValueTask<BacklogView> GetPublishedBacklogAsync(CancellationToken cancellationToken)
    {
        return _GetBacklogAsync(includeReceived: false, cancellationToken);
    }

    private async ValueTask<BacklogView> _GetBacklogAsync(bool includeReceived, CancellationToken cancellationToken)
    {
        // The pending predicate names each non-terminal status instead of negating the terminal ones, so both engines
        // can seek the (status, added) index rather than scan the succeeded rows that make up most of a table. A
        // delayed or queued row holds its publish time in ExpiresAt: it counts only once that time has come, and
        // waits from then. The clock is the database's, read once for the whole statement.
        var published = _BacklogSubquery(_publishedTable);
        var sql = includeReceived
            ? $"SELECT p.backlog_count, p.oldest_at, r.backlog_count, r.oldest_at, {SqlDialectTokens.Now} FROM {published} AS p CROSS JOIN {_BacklogSubquery(_receivedTable)} AS r;"
            : $"SELECT p.backlog_count, p.oldest_at, {SqlDialectTokens.Now} FROM {published} AS p;";

        await using var connection = _storage.CreateConnection();
        return await RelationalCommand
            .ExecuteReaderAsync(
                connection,
                transaction: null,
                _dialect.Render(new SqlClockedStatement(sql)),
                CommandTimeoutSeconds,
                bind: null,
                async (reader, ct) =>
                {
                    if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        return new BacklogView { PublishedPending = 0, ReceivedPending = 0 };
                    }

                    var nowOrdinal = includeReceived ? 4 : 2;
                    var now = await reader.GetFieldValueAsync<DateTimeOffset>(nowOrdinal, ct).ConfigureAwait(false);
                    var oldestPublished = await _InstantAsync(reader, 1, ct).ConfigureAwait(false);

                    if (!includeReceived)
                    {
                        return new BacklogView
                        {
                            PublishedPending = _Int64(reader, 0),
                            ReceivedPending = 0,
                            OldestPublishedPendingAge = _AgeSince(oldestPublished, now),
                        };
                    }

                    var oldestReceived = await _InstantAsync(reader, 3, ct).ConfigureAwait(false);
                    return new BacklogView
                    {
                        PublishedPending = _Int64(reader, 0),
                        ReceivedPending = _Int64(reader, 2),
                        OldestPublishedPendingAge = _AgeSince(oldestPublished, now),
                        OldestReceivedPendingAge = _AgeSince(oldestReceived, now),
                    };
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private string _BacklogSubquery(string table)
    {
        const string scheduled = nameof(StatusName.Scheduled);
        const string delayed = nameof(StatusName.Delayed);
        const string queued = nameof(StatusName.Queued);
        const string failed = nameof(StatusName.Failed);
        const string now = SqlDialectTokens.Now;

        return $"(SELECT COUNT(*) AS backlog_count, MIN(CASE WHEN {_t.StatusName} IN ('{delayed}','{queued}') AND {_t.ExpiresAt} IS NOT NULL THEN {_t.ExpiresAt} ELSE {_t.Added} END) AS oldest_at FROM {_dialect.ReadWithoutWaiting(table)} WHERE {_t.IntentType} IN (0, 1) AND ({_t.StatusName} = '{scheduled}' OR ({_t.StatusName} IN ('{delayed}','{queued}') AND ({_t.ExpiresAt} IS NULL OR {_t.ExpiresAt} <= {now})) OR ({_t.StatusName} = '{failed}' AND {_t.NextRetryAt} IS NOT NULL)))";
    }

    // A row stamped a little after the clock read (the engine's precision, or a commit racing the read) is not older
    // than the read, so it reports no age rather than a negative one.
    private static TimeSpan? _AgeSince(DateTimeOffset? since, DateTimeOffset now)
    {
        return since is { } value ? TimeSpan.FromTicks(Math.Max(0, (now - value).Ticks)) : null;
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
            _dialect.AddParameter(
                command,
                "Content",
                SqlColumnType.Text(-1),
                query.Content is null ? null : SqlLikePattern.Contains(query.Content)
            );
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

    /// <inheritdoc />
    public async ValueTask<MessageListPage> ListMessagesAsync(
        MessageListQuery query,
        MessageListPosition? after,
        int pageSize,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(query);
        Argument.IsInclusiveBetween(pageSize, 1, MessageListPage.MaxPageSize);

        var received = query.MessageType == MessageType.Subscribe;
        var table = received ? _receivedTable : _publishedTable;
        var (where, bind) = RelationalMessageFilter.Build(_t, _dialect, query);

        if (after is not null)
        {
            where += " AND " + RelationalMessageFilter.Before(_t.Added, _t.Id, "AfterAdded", "AfterId");
        }

        var receivedColumns = received
            ? $",{_t.ConsumerIdentity},{_t.TenantPresent},{_t.TenantId},{_t.IsHeld},{_t.GenerationIncarnationId}"
            : "";
        var sql = $"""
            SELECT {_t.Id},{_t.MessageId},{_t.Version},{_t.Name},{_t.IntentType},{_t.Retries},{_t.Added},{_t.ExpiresAt},{_t.StatusName},{_t.NextRetryAt},{_t.LockedUntil},{_t.ChangedAt},{_t.FailureFingerprint}{receivedColumns}
            FROM {table}
            WHERE {where}
            ORDER BY {_t.Added} DESC,{_t.Id} DESC
            {_dialect.Limit("Limit")};
            """;

        await using var connection = _storage.CreateConnection();
        var rows = await RelationalCommand
            .ExecuteReaderAsync(
                connection,
                transaction: null,
                sql,
                CommandTimeoutSeconds,
                command =>
                {
                    bind(command);
                    _dialect.AddParameter(command, "Limit", SqlColumnType.Int32, pageSize + 1);
                    if (after is { } position)
                    {
                        _dialect.AddParameter(command, "AfterAdded", SqlColumnType.Timestamp, position.Added);
                        _dialect.AddParameter(command, "AfterId", SqlColumnType.Guid, position.Id);
                    }
                },
                async (reader, token) =>
                {
                    var views = new List<MessageView>();
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        var view = new MessageView
                        {
                            StorageId = reader.GetGuid(0),
                            MessageId = reader.GetString(1),
                            Version = reader.GetString(2),
                            Name = reader.GetString(3),
                            Lane = MessageLaneCompatibility.FromPersistedValue(reader.GetInt16(4)),
                            Retries = reader.GetInt32(5),
                            Added = await reader.GetFieldValueAsync<DateTimeOffset>(6, token).ConfigureAwait(false),
                            ExpiresAt = await _InstantAsync(reader, 7, token).ConfigureAwait(false),
                            StatusName = Enum.Parse<StatusName>(reader.GetString(8)),
                            NextRetryAt = await _InstantAsync(reader, 9, token).ConfigureAwait(false),
                            LockedUntil = await _InstantAsync(reader, 10, token).ConfigureAwait(false),
                            ChangedAt = (
                                await reader.GetFieldValueAsync<DateTimeOffset>(11, token).ConfigureAwait(false)
                            ).ToUniversalTime(),
                            FailureFingerprint = await _StringAsync(reader, 12, token).ConfigureAwait(false),
                        };

                        if (received)
                        {
                            view.ConsumerIdentity = reader.GetString(13);
                            view.TenantId = reader.GetBoolean(14) ? reader.GetString(15) : null;
                            view.IsHeld = reader.GetBoolean(16);
                            view.IncarnationId = await reader.IsDBNullAsync(17, token).ConfigureAwait(false)
                                ? null
                                : reader.GetGuid(17);
                        }

                        views.Add(view);
                    }

                    return views;
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        var page = rows.Count > pageSize ? rows.GetRange(0, pageSize) : rows;
        return new MessageListPage
        {
            Items = page,
            Next = rows.Count > pageSize ? new MessageListPosition(page[^1].Added, page[^1].StorageId) : null,
        };
    }

    /// <inheritdoc />
    public async ValueTask<MessageCount> CountMessagesAsync(
        MessageListQuery query,
        int cap = MessageCount.DefaultCap,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(query);
        Argument.IsPositive(cap);

        var table = query.MessageType == MessageType.Subscribe ? _receivedTable : _publishedTable;
        var (where, bind) = RelationalMessageFilter.Build(_t, _dialect, query);

        await using var connection = _storage.CreateConnection();
        var matched = await CountCappedAsync(
                _dialect,
                connection,
                _dialect.ReadWithoutWaiting(table),
                where,
                bind,
                cap,
                CommandTimeoutSeconds,
                cancellationToken
            )
            .ConfigureAwait(false);

        return MessageCount.Of(matched, cap);
    }

    /// <summary>
    /// Counts the rows of <paramref name="source"/> that <paramref name="where"/> matches, reading at most one row past
    /// <paramref name="cap"/>.
    /// </summary>
    internal static async Task<long> CountCappedAsync(
        ISqlDialect dialect,
        DbConnection connection,
        string source,
        string where,
        Action<DbCommand>? bind,
        int cap,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken
    )
    {
        // ORDER BY (SELECT NULL) satisfies an engine whose row limit needs an order without asking for a sort.
        var sql =
            $"SELECT COUNT(*) FROM (SELECT 1 AS hit FROM {source} WHERE {where} ORDER BY (SELECT NULL) {dialect.Limit("CountLimit")}) AS capped;";

        return Convert.ToInt64(
            await RelationalCommand
                .ExecuteScalarAsync(
                    connection,
                    transaction: null,
                    sql,
                    commandTimeoutSeconds,
                    command =>
                    {
                        bind?.Invoke(command);
                        dialect.AddParameter(command, "CountLimit", SqlColumnType.Int32, cap + 1);
                    },
                    cancellationToken
                )
                .ConfigureAwait(false),
            CultureInfo.InvariantCulture
        );
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<MessageFailureGroup>> GetFailureGroupsAsync(
        MessageType messageType,
        string? consumerIdentity = null,
        CancellationToken cancellationToken = default
    )
    {
        var received = messageType == MessageType.Subscribe;
        // The predicate is written as the partial failure-groups index declares it, so each engine reads that index.
        var where =
            $"{_t.StatusName} = '{nameof(StatusName.Failed)}' AND {_t.NextRetryAt} IS NULL AND {_t.IntentType} IN (0, 1)";
        string sql;

        if (received)
        {
            where += $" AND {_t.IsCurrentGeneration} = {_t.True}";
            if (consumerIdentity is not null)
            {
                where += $" AND {_t.ConsumerKeyHash} = @ConsumerKeyHash";
            }

            // Grouped on the identity's hash: GROUP BY on the text would merge identities that differ only by
            // trailing spaces on SQL Server. Every row of a group has the same identity bytes, so MIN picks it.
            sql = $"""
                SELECT {_t.FailureFingerprint},MIN({_t.ConsumerIdentity}),COUNT(*),MAX({_t.ChangedAt})
                FROM {_dialect.ReadWithoutWaiting(_receivedTable)}
                WHERE {where}
                GROUP BY {_t.FailureFingerprint},{_t.ConsumerKeyHash};
                """;
        }
        else
        {
            sql = $"""
                SELECT {_t.FailureFingerprint},NULL,COUNT(*),MAX({_t.ChangedAt})
                FROM {_dialect.ReadWithoutWaiting(_publishedTable)}
                WHERE {where}
                GROUP BY {_t.FailureFingerprint};
                """;
        }

        await using var connection = _storage.CreateConnection();
        var groups = await RelationalCommand
            .ExecuteReaderAsync(
                connection,
                transaction: null,
                sql,
                CommandTimeoutSeconds,
                command =>
                {
                    if (received && consumerIdentity is not null)
                    {
                        _dialect.AddParameter(
                            command,
                            "ConsumerKeyHash",
                            SqlColumnType.FixedBinary(32),
                            RelationalDataStorage.CreateConsumerKeyHash(consumerIdentity)
                        );
                    }
                },
                async (reader, token) =>
                {
                    var rows = new List<MessageFailureGroup>();
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        rows.Add(
                            new MessageFailureGroup
                            {
                                MessageType = messageType,
                                FailureFingerprint = await _StringAsync(reader, 0, token).ConfigureAwait(false),
                                ConsumerIdentity = received
                                    ? await _StringAsync(reader, 1, token).ConfigureAwait(false)
                                    : null,
                                Count = (int)Math.Min(_Int64(reader, 2), int.MaxValue),
                                LastFailedAt = (
                                    await reader.GetFieldValueAsync<DateTimeOffset>(3, token).ConfigureAwait(false)
                                ).ToUniversalTime(),
                            }
                        );
                    }

                    return rows;
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        return MessageFailureGroupOrder.Sort(groups);
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

        // One list parameter keeps the statement text, and its cached plan, the same whatever the number of ids.
        var sql =
            $"SELECT {_t.Id},{_t.Content},{_t.IntentType},{_t.Added},{_t.ExpiresAt},{_t.Retries},{_t.ExceptionInfo},{_t.NextRetryAt},{_t.LockedUntil},{_t.StatusName},{_t.FailureFingerprint} FROM {_dialect.ReadWithoutWaiting(tableName)} WHERE {_dialect.InList(_t.Id, "Ids", SqlColumnType.Guid)} AND {_t.IntentType} IN (0, 1);";

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
                                StatusName = Enum.Parse<StatusName>(reader.GetString(9)),
                                FailureFingerprint = await _StringAsync(reader, 10, token).ConfigureAwait(false),
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
