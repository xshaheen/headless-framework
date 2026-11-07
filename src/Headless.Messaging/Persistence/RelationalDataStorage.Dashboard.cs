// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Checks;
using Headless.Sql;

namespace Headless.Messaging.Persistence;

#pragma warning disable CA1849 // Buffered row reads cannot add blocking I/O.

// The inbox reads a dashboard needs beyond the single-row operations: capped counts, an Operation's frozen scope, and
// the operator ledger. An additional outbox's database holds no received or receipt table, so it reads nothing.
internal sealed partial class RelationalDataStorage
{
    /// <summary>The most generations one Operation scope chunk returns.</summary>
    private const int _MaxOperationChunkSize = 500;

    public async ValueTask<MessageCount> CountAsync(
        InboxGenerationQuery query,
        OperatorAuthorizationContext authorization,
        int cap = MessageCount.DefaultCap,
        CancellationToken cancellationToken = default
    )
    {
        authorization.ValidateRead();
        Argument.IsPositive(cap);

        if (!_holdsReceived)
        {
            return MessageCount.Of(0, cap);
        }

        var (where, bind) = _InboxGenerationFilter(query);
        await using var connection = _CreateConnection();
        var matched = await RelationalMonitoringApi
            .CountCappedAsync(
                _dialect,
                connection,
                _receivedTable,
                where,
                bind,
                cap,
                CommandTimeoutSeconds,
                cancellationToken
            )
            .ConfigureAwait(false);

        return MessageCount.Of(matched, cap);
    }

    public async ValueTask<MessageCount> CountOperationScopeAsync(
        MessageOperationScope scope,
        int cap = MessageCount.DefaultCap,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsPositive(cap);
        var (where, bind) = _OperationScopeFilter(scope);

        if (!_holdsReceived)
        {
            return MessageCount.Of(0, cap);
        }

        await using var connection = _CreateConnection();
        var matched = await RelationalMonitoringApi
            .CountCappedAsync(
                _dialect,
                connection,
                _receivedTable,
                where,
                bind,
                cap,
                CommandTimeoutSeconds,
                cancellationToken
            )
            .ConfigureAwait(false);

        return MessageCount.Of(matched, cap);
    }

    public async ValueTask<MessageOperationChunk> ReadOperationScopeAsync(
        MessageOperationScope scope,
        Guid? afterId,
        int chunkSize,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsInclusiveBetween(chunkSize, 1, _MaxOperationChunkSize);
        var (where, bind) = _OperationScopeFilter(scope);

        if (!_holdsReceived)
        {
            return new MessageOperationChunk { Targets = [] };
        }

        if (afterId is not null)
        {
            where += $" AND {_t.Id} > @AfterId";
        }

        var sql = $"""
            SELECT {_t.Id},{_t.GenerationIncarnationId},{_t.StatusName},{_t.ChangedAt}
            FROM {_receivedTable}
            WHERE {where}
            ORDER BY {_t.Id}
            {_dialect.Limit("Limit")};
            """;

        await using var connection = _CreateConnection();
        var targets = await RelationalCommand
            .ExecuteReaderAsync(
                connection,
                transaction: null,
                sql,
                CommandTimeoutSeconds,
                command =>
                {
                    bind(command);
                    _dialect.AddParameter(command, "Limit", SqlColumnType.Int32, chunkSize);
                    if (afterId is { } id)
                    {
                        _dialect.AddParameter(command, "AfterId", SqlColumnType.Guid, id);
                    }
                },
                static async (reader, ct) =>
                {
                    var rows = new List<MessageOperationTarget>();
                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        rows.Add(
                            new MessageOperationTarget(
                                reader.GetGuid(0),
                                reader.GetGuid(1),
                                Enum.Parse<StatusName>(reader.GetString(2)),
                                (
                                    await reader.GetFieldValueAsync<DateTimeOffset>(3, ct).ConfigureAwait(false)
                                ).ToUniversalTime()
                            )
                        );
                    }

                    return rows;
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        // A full chunk may be the scope's last; the next read then returns an empty chunk with no cursor.
        return new MessageOperationChunk
        {
            Targets = targets,
            Next = targets.Count == chunkSize ? targets[^1].StorageId : null,
        };
    }

    /// <summary>
    /// The predicate of an Operation's frozen scope: the query's inbox generations, terminal, stamped at or before the
    /// high-water mark, and still eligible for the command.
    /// </summary>
    private (string Where, Action<DbCommand> Bind) _OperationScopeFilter(MessageOperationScope scope)
    {
        Argument.IsNotNull(scope);
        Argument.IsNotNull(scope.Query);

        if (scope.Query.MessageType != MessageType.Subscribe)
        {
            throw new ArgumentException("An Operation scope reads received inbox generations only.", nameof(scope));
        }

        var (where, bind) = RelationalMessageFilter.Build(_t, _dialect, scope.Query);
        where +=
            $" AND {_t.IsInboxRecord} = {_t.True} AND {RelationalMessageFilter.Terminal(_t)} AND {_t.ChangedAt} <= @HighWaterMark";
        where += scope.Kind switch
        {
            MessageOperationKind.ReExecute => $" AND {_t.IsCurrentGeneration} = {_t.True}",
            MessageOperationKind.Delete => $" AND {_t.IsHeld} = {_t.False}",
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope.Kind, "Unknown Operation kind."),
        };

        return (
            where,
            command =>
            {
                bind(command);
                _dialect.AddParameter(command, "HighWaterMark", SqlColumnType.Timestamp, scope.HighWaterMark);
            }
        );
    }

    public async ValueTask<MessagingOperationReceiptPage> QueryReceiptsAsync(
        MessagingOperationReceiptQuery query,
        OperatorAuthorizationContext authorization,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(query);
        authorization.ValidateRead();

        if (!_holdsReceived)
        {
            return new MessagingOperationReceiptPage { Items = [] };
        }

        var limit = Math.Clamp(query.Limit, 1, MessagingOperationReceiptQuery.MaxLimit);
        var where = "1 = 1";

        if (query.Actor is not null)
        {
            // The LIKE of the escaped value keeps the match exact on an engine whose equality ignores trailing spaces.
            where += $" AND {_t.Actor} = @Actor AND {_t.Actor} LIKE @ActorPattern ESCAPE '\\'";
        }

        if (query.OperationType is not null)
        {
            where += $" AND {_t.OperationType} = @OperationType";
        }

        if (query.TargetKind is not null)
        {
            where += $" AND {_t.TargetKind} = @TargetKind";
        }

        if (query.From is not null)
        {
            where += $" AND {_t.CreatedAt} >= @From";
        }

        if (query.Before is not null)
        {
            where += $" AND {_t.CreatedAt} < @Before";
        }

        if (query.After is not null)
        {
            where += " AND " + RelationalMessageFilter.Before(_t.CreatedAt, _t.OperationId, "AfterAt", "AfterId");
        }

        var sql = $"""
            SELECT {_t.OperationId},{_t.TargetKind},{_t.OperationType},{_t.Outcome},{_t.Actor},{_t.Reason},{_t.CreatedAt},{_t.StorageId},{_t.GenerationIncarnationId},{_t.ChildStorageId},{_t.MessageName},{_t.MessageId}
            FROM {_t.Receipts}
            WHERE {where}
            ORDER BY {_t.CreatedAt} DESC,{_t.OperationId} DESC
            {_dialect.Limit("Limit")};
            """;

        await using var connection = _CreateConnection();
        var rows = await RelationalCommand
            .ExecuteReaderAsync(
                connection,
                transaction: null,
                sql,
                CommandTimeoutSeconds,
                command =>
                {
                    if (query.Actor is { } actor)
                    {
                        _dialect.AddParameter(command, "Actor", SqlColumnType.KeyText(_ActorMaxLength), actor);
                        _dialect.AddParameter(
                            command,
                            "ActorPattern",
                            SqlColumnType.Text(_Unbounded),
                            SqlLikePattern.Escape(actor)
                        );
                    }

                    if (query.OperationType is { } operationType)
                    {
                        _dialect.AddParameter(command, "OperationType", _StatusType, operationType.ToString());
                    }

                    if (query.TargetKind is { } targetKind)
                    {
                        _dialect.AddParameter(command, "TargetKind", _StatusType, targetKind.ToString());
                    }

                    if (query.From is { } from)
                    {
                        _dialect.AddParameter(command, "From", SqlColumnType.Timestamp, from);
                    }

                    if (query.Before is { } before)
                    {
                        _dialect.AddParameter(command, "Before", SqlColumnType.Timestamp, before);
                    }

                    if (query.After is { } after)
                    {
                        _dialect.AddParameter(command, "AfterAt", SqlColumnType.Timestamp, after.CreatedAt);
                        _dialect.AddParameter(command, "AfterId", SqlColumnType.Guid, after.OperationId);
                    }

                    // One row past the page proves another page follows.
                    _dialect.AddParameter(command, "Limit", SqlColumnType.Int32, limit + 1);
                },
                static async (reader, ct) =>
                {
                    var receipts = new List<MessagingOperationReceiptView>();
                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        receipts.Add(
                            new MessagingOperationReceiptView(
                                reader.GetGuid(0),
                                Enum.Parse<MessagingOperationTargetKind>(reader.GetString(1)),
                                Enum.Parse<MessagingOperationType>(reader.GetString(2)),
                                Enum.Parse<InboxOperationOutcome>(reader.GetString(3)),
                                reader.GetString(4),
                                reader.IsDBNull(5) ? null : reader.GetString(5),
                                (
                                    await reader.GetFieldValueAsync<DateTimeOffset>(6, ct).ConfigureAwait(false)
                                ).ToUniversalTime(),
                                reader.IsDBNull(7) ? null : reader.GetGuid(7),
                                reader.IsDBNull(8) ? null : reader.GetGuid(8),
                                reader.IsDBNull(9) ? null : reader.GetGuid(9),
                                reader.IsDBNull(10) ? null : reader.GetString(10),
                                reader.IsDBNull(11) ? null : reader.GetString(11)
                            )
                        );
                    }

                    return receipts;
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        var page = rows.Count > limit ? rows.GetRange(0, limit) : rows;
        return new MessagingOperationReceiptPage
        {
            Items = page,
            Next =
                rows.Count > limit
                    ? new MessagingOperationReceiptPosition(page[^1].CreatedAt, page[^1].OperationId)
                    : null,
        };
    }
}

#pragma warning restore CA1849
