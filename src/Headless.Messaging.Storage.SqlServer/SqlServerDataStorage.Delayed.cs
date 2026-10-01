// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Sql;
using Microsoft.Data.SqlClient;

#pragma warning disable RCS1084 // Use coalesce expression instead of conditional expression
namespace Headless.Messaging.Storage.SqlServer;

internal sealed partial class SqlServerDataStorage
{
    /// <summary>
    /// Atomically selects delayed and stale-queued messages within a database transaction and
    /// invokes <paramref name="scheduleTask"/> to re-enqueue them. Uses branch-bounded ordered
    /// <c>TOP</c> reads with <c>UPDLOCK, READPAST</c> so concurrent replicas skip rows another
    /// node is scheduling without locking an unbounded candidate set.
    /// The transaction is committed after <paramref name="scheduleTask"/> completes.
    /// </summary>
    public async ValueTask ScheduleMessagesOfDelayedAsync(
        Func<DbTransaction?, IEnumerable<MediumMessage>, ValueTask> scheduleTask,
        CancellationToken cancellationToken = default
    )
    {
        // Due time is the database's: the windows are measured from the database clock, the clock every other due
        // decision reads, so a replica whose clock is skewed neither schedules a message early nor holds it back.
        var sql = $"""
            DECLARE @now datetimeoffset(7) = TODATETIMEOFFSET(SYSUTCDATETIME(), 0);

            WITH DelayedCandidates AS (
                SELECT TOP (@BatchSize) Id, Content, IntentType, Retries, InlineAttempts, Added, ExpiresAt
                FROM {_publishedTable} WITH (UPDLOCK, READPAST, READCOMMITTEDLOCK)
                WHERE Version = @Version
                  AND IntentType IN (0, 1)
                  AND StatusName = @DelayedStatusName
                  AND ExpiresAt < {_Dialect.ShiftByDuration("@now", "Lookahead")}
                ORDER BY ExpiresAt, Id
            ),
            QueuedCandidates AS (
                SELECT TOP (@BatchSize) Id, Content, IntentType, Retries, InlineAttempts, Added, ExpiresAt
                FROM {_publishedTable} WITH (UPDLOCK, READPAST, READCOMMITTEDLOCK)
                WHERE Version = @Version
                  AND IntentType IN (0, 1)
                  AND StatusName = @QueuedStatusName
                  AND ExpiresAt < {_Dialect.ShiftByDuration("@now", "Lookback", subtract: true)}
                ORDER BY ExpiresAt, Id
            ),
            Candidates AS (
                SELECT Id, Content, IntentType, Retries, InlineAttempts, Added, ExpiresAt FROM DelayedCandidates
                UNION ALL
                SELECT Id, Content, IntentType, Retries, InlineAttempts, Added, ExpiresAt FROM QueuedCandidates
            )
            SELECT TOP (@BatchSize) Id, Content, IntentType, Retries, InlineAttempts, Added, ExpiresAt
            FROM Candidates
            ORDER BY ExpiresAt, Id;
            """;

        object[] sqlParams =
        [
            _VersionParameter(),
            new SqlParameter("@DelayedStatusName", nameof(StatusName.Delayed)),
            new SqlParameter("@QueuedStatusName", nameof(StatusName.Queued)),
            .. _Duration("Lookahead", _DelayedMessageLookahead),
            .. _Duration("Lookback", _QueuedMessageLookback),
            new SqlParameter("@BatchSize", messagingOptions.Value.SchedulerBatchSize),
        ];

        await using var connection = new SqlConnection(options.Value.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        // A pooled session may retain Serializable from inbox admission; READPAST needs a compatible isolation level.
        await using var transaction = await connection
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            .ConfigureAwait(false);
        var poisonMessages = new List<PoisonMessage>();
        var messageList = await connection
            .ExecuteReaderAsync(
                sql,
                async (reader, ct) =>
                {
                    var messages = new List<MediumMessage>();
                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        var storageId = reader.GetGuid(0);
                        var content = reader.GetString(1);

                        MediumMessage mediumMessage;
                        try
                        {
                            mediumMessage = new MediumMessage
                            {
                                StorageId = storageId,
                                Origin = serializer.Deserialize(content)!,
                                Content = content,
                                Lane = MessageLaneCompatibility.FromPersistedValue(reader.GetInt16(2)),
                                Retries = reader.GetInt32(3),
                                InlineAttempts = reader.GetInt32(4),
                                Added = await reader.GetFieldValueAsync<DateTimeOffset>(5, ct).ConfigureAwait(false),
                                ExpiresAt = await reader
                                    .GetFieldValueAsync<DateTimeOffset>(6, ct)
                                    .ConfigureAwait(false),
                            };
                        }
#pragma warning disable CA1031 // deliberately broad: one un-deserializable row must not abort the schedule batch (#3)
                        catch (Exception ex)
#pragma warning restore CA1031
                        {
                            logger.LogPoisonMessageSkipped(storageId, _publishedTable, ex);
                            poisonMessages.Add(_CreatePoisonMessage(storageId, ex));
                            continue;
                        }

                        messages.Add(mediumMessage);
                    }

                    return messages;
                },
                transaction: transaction,
                commandTimeout: messagingOptions.Value.CommandTimeout,
                sqlParams: sqlParams,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        logger.LogSchedulerBatchFetched(messageList.Count, _publishedTable);

        await _MarkPoisonMessagesTerminalAsync(
                connection,
                transaction,
                _publishedTable,
                poisonMessages,
                cancellationToken
            )
            .ConfigureAwait(false);

        await scheduleTask(transaction, messageList).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyList<MediumMessage>> ClaimDelayedMessagesAsync(
        CancellationToken cancellationToken = default
    )
    {
        // One claim statement moves due delayed (and stale queued) rows to Queued and leases them past their due
        // time, skipping rows another replica holds. Due time and lease are both the database's clock.
        var sql = _Dialect.Render(
            new SqlClaimNext(
                _publishedTable,
                ["[Id]"],
                $"""
                Version=@Version
                AND IntentType IN (0, 1)
                AND (LockedUntil IS NULL OR LockedUntil <= {SqlDialectTokens.Now})
                AND (
                    (StatusName=@DelayedStatusName AND ExpiresAt < {_Dialect.ShiftByDuration(
                    SqlDialectTokens.Now,
                    "Lookahead"
                )})
                    OR (StatusName=@QueuedStatusName AND ExpiresAt < {_Dialect.ShiftByDuration(
                    SqlDialectTokens.Now,
                    "Lookback",
                    subtract: true
                )})
                )
                AND {_TerminalRowGuardSimple}
                """,
                ["[ExpiresAt]", "[Id]"],
                $"StatusName=@QueuedStatusName, LockedUntil={_Dialect.ShiftByDuration($"CASE WHEN ExpiresAt > {SqlDialectTokens.Now} THEN ExpiresAt ELSE {SqlDialectTokens.Now} END", "Lease")}, Owner=@Owner",
                [
                    "[Id]",
                    "[Content]",
                    "[IntentType]",
                    "[Retries]",
                    "[InlineAttempts]",
                    "[Added]",
                    "[ExpiresAt]",
                    "[LockedUntil]",
                    "[Owner]",
                ],
                BatchSizeParameter: "BatchSize"
            )
        );

        var claimed = await SqlAutonomousTransaction
            .RunAsync(
                () => new SqlConnection(options.Value.ConnectionString),
                async (connection, transaction, ct) =>
                {
                    object[] sqlParams =
                    [
                        new SqlParameter("@BatchSize", messagingOptions.Value.SchedulerBatchSize),
                        _VersionParameter(),
                        new SqlParameter("@DelayedStatusName", nameof(StatusName.Delayed)),
                        new SqlParameter("@QueuedStatusName", nameof(StatusName.Queued)),
                        .. _Duration("Lookahead", _DelayedMessageLookahead),
                        .. _Duration("Lookback", _QueuedMessageLookback),
                        .. _Duration("Lease", messagingOptions.Value.RetryPolicy.DispatchTimeout),
                        _OwnerParameter("@Owner", hasLease: true),
                    ];
                    var poisonMessages = new List<PoisonMessage>();
                    var messages = await connection
                        .ExecuteReaderAsync(
                            sql,
                            (reader, token) => _ReadDelayedClaimAsync(reader, poisonMessages, token),
                            transaction: transaction,
                            commandTimeout: messagingOptions.Value.CommandTimeout,
                            sqlParams: sqlParams,
                            cancellationToken: ct
                        )
                        .ConfigureAwait(false);

                    await _MarkPoisonMessagesTerminalAsync(connection, transaction, _publishedTable, poisonMessages, ct)
                        .ConfigureAwait(false);

                    return messages;
                },
                timeProvider,
                cancellationToken
            )
            .ConfigureAwait(false);

        // The claim reports rows in no particular order; callers dispatch in due order.
        claimed.Sort(
            static (left, right) =>
            {
                var expiresComparison = Nullable.Compare(left.ExpiresAt, right.ExpiresAt);
                return expiresComparison != 0 ? expiresComparison : left.StorageId.CompareTo(right.StorageId);
            }
        );
        return claimed;
    }

    private async Task<List<MediumMessage>> _ReadDelayedClaimAsync(
        DbDataReader reader,
        List<PoisonMessage> poisonMessages,
        CancellationToken cancellationToken
    )
    {
        var messages = new List<MediumMessage>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var storageId = reader.GetGuid(0);
            var content = reader.GetString(1);
            try
            {
                messages.Add(
                    new MediumMessage
                    {
                        StorageId = storageId,
                        Origin = serializer.Deserialize(content)!,
                        Content = content,
                        Lane = MessageLaneCompatibility.FromPersistedValue(reader.GetInt16(2)),
                        Retries = reader.GetInt32(3),
                        InlineAttempts = reader.GetInt32(4),
                        Added = await reader
                            .GetFieldValueAsync<DateTimeOffset>(5, cancellationToken)
                            .ConfigureAwait(false),
                        ExpiresAt = await reader
                            .GetFieldValueAsync<DateTimeOffset>(6, cancellationToken)
                            .ConfigureAwait(false),
                        LockedUntil = await reader
                            .GetFieldValueAsync<DateTimeOffset>(7, cancellationToken)
                            .ConfigureAwait(false),
                        Owner = await reader.IsDBNullAsync(8, cancellationToken).ConfigureAwait(false)
                            ? null
                            : reader.GetString(8),
                    }
                );
            }
#pragma warning disable CA1031 // one un-deserializable row must not abort or starve the batch
            catch (Exception ex)
#pragma warning restore CA1031
            {
                logger.LogPoisonMessageSkipped(storageId, _publishedTable, ex);
                poisonMessages.Add(_CreatePoisonMessage(storageId, ex));
            }
        }

        return messages;
    }
}
