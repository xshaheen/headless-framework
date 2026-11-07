// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Checks;
using Headless.Sql;

namespace Headless.Messaging.Persistence;

// Change reads over the ChangedAt marker every state-changing statement stamps from the database clock. They read
// without waiting, so an uncommitted write is skipped now and returned by the overlap window of a later pass.
internal sealed partial class RelationalDataStorage
{
    public async ValueTask<DateTimeOffset?> GetLatestMessageChangeMarkerAsync(
        CancellationToken cancellationToken = default
    )
    {
        // An additional outbox's database holds no received table.
        var latest = $"SELECT MAX({_t.ChangedAt}) AS m FROM {_dialect.ReadWithoutWaiting(_publishedTable)}";
        if (_holdsReceived)
        {
            latest += $" UNION ALL SELECT MAX({_t.ChangedAt}) FROM {_dialect.ReadWithoutWaiting(_receivedTable)}";
        }

        await using var connection = _CreateConnection();
        return await RelationalCommand
            .ExecuteReaderAsync(
                connection,
                transaction: null,
                $"SELECT MAX(x.m) FROM ({latest}) AS x;",
                CommandTimeoutSeconds,
                bind: null,
                static async (reader, ct) =>
                    await reader.ReadAsync(ct).ConfigureAwait(false)
                    && !await reader.IsDBNullAsync(0, ct).ConfigureAwait(false)
                        ? (
                            await reader.GetFieldValueAsync<DateTimeOffset>(0, ct).ConfigureAwait(false)
                        ).ToUniversalTime()
                        : (DateTimeOffset?)null,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyList<MessageChange>> ReadChangedMessagesAsync(
        MessageChangeKind kind,
        DateTimeOffset after,
        DateTimeOffset through,
        MessageChangePosition? resumeAfter,
        int maxCount,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsPositive(maxCount);

        var table = kind switch
        {
            MessageChangeKind.Published => _publishedTable,
            MessageChangeKind.Received => _receivedTable,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown change kind."),
        };

        if (kind is MessageChangeKind.Received && !_holdsReceived)
        {
            return [];
        }

        var keyset = resumeAfter is null
            ? ""
            : $" AND {_dialect.KeysetAfter([_t.ChangedAt, _t.Id], ["PositionAt", "PositionId"])}";
        var sql = $"""
            SELECT {_t.Id},{_t.ChangedAt},{_t.StatusName}
            FROM {_dialect.ReadWithoutWaiting(table)}
            WHERE {_t.ChangedAt} > @After AND {_t.ChangedAt} <= @Through{keyset}
            ORDER BY {_t.ChangedAt},{_t.Id}
            {_dialect.Limit("MaxCount")};
            """;

        await using var connection = _CreateConnection();
        return await RelationalCommand
            .ExecuteReaderAsync(
                connection,
                transaction: null,
                sql,
                CommandTimeoutSeconds,
                command =>
                {
                    _dialect.AddParameter(command, "After", SqlColumnType.Timestamp, after);
                    _dialect.AddParameter(command, "Through", SqlColumnType.Timestamp, through);
                    _dialect.AddParameter(command, "MaxCount", SqlColumnType.Int32, maxCount);
                    if (resumeAfter is { } position)
                    {
                        _dialect.AddParameter(command, "PositionAt", SqlColumnType.Timestamp, position.ChangedAt);
                        _dialect.AddParameter(command, "PositionId", SqlColumnType.Guid, position.Id);
                    }
                },
                async (reader, ct) => await _ReadChangesAsync(reader, kind, ct).ConfigureAwait(false),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<MessageChange>> _ReadChangesAsync(
        DbDataReader reader,
        MessageChangeKind kind,
        CancellationToken cancellationToken
    )
    {
        var changes = new List<MessageChange>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            changes.Add(
                new MessageChange
                {
                    Kind = kind,
                    Id = reader.GetGuid(0),
                    ChangedAt = (
                        await reader.GetFieldValueAsync<DateTimeOffset>(1, cancellationToken).ConfigureAwait(false)
                    ).ToUniversalTime(),
                    Status = Enum.Parse<StatusName>(reader.GetString(2)),
                }
            );
        }

        return changes;
    }
}
