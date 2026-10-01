// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Sql;
using Npgsql;
using NpgsqlTypes;

#pragma warning disable RCS1084 // Use coalesce expression instead of conditional expression
namespace Headless.Messaging.Storage.PostgreSql;

internal sealed partial class PostgreSqlDataStorage
{
    /// <summary>
    /// Atomically selects delayed and stale-queued messages within a database transaction and
    /// invokes <paramref name="scheduleTask"/> to re-enqueue them. The SELECT uses
    /// <c>FOR UPDATE SKIP LOCKED</c> so concurrent replicas skip rows another node is scheduling.
    /// The transaction is committed after <paramref name="scheduleTask"/> completes.
    /// </summary>
    public async ValueTask ScheduleMessagesOfDelayedAsync(
        Func<DbTransaction?, IEnumerable<MediumMessage>, ValueTask> scheduleTask,
        CancellationToken cancellationToken = default
    )
    {
        // Due time is the database's: the windows are measured from the database clock, the clock every other due
        // decision reads, so a replica whose clock is skewed neither schedules a message early nor holds it back.
        var sql = _Dialect.Render(
            new SqlClockedStatement(
                $"""
                SELECT "id","content","intent_type","retries","inline_attempts","added","expires_at"
                FROM {_publishedTable}
                WHERE "version"=@Version
                  AND "intent_type" IN (0, 1)
                  AND (("expires_at" < {_Dialect.ShiftByDuration(
                    SqlDialectTokens.Now,
                    "Lookahead"
                )} AND "status_name" = '{nameof(StatusName.Delayed)}')
                    OR ("expires_at" < {_Dialect.ShiftByDuration(
                    SqlDialectTokens.Now,
                    "Lookback",
                    subtract: true
                )} AND "status_name" = '{nameof(StatusName.Queued)}'))
                FOR UPDATE SKIP LOCKED
                LIMIT @BatchSize;
                """
            )
        );

        var sqlParams = new object[]
        {
            _VersionParameter(),
            _Duration("Lookahead", _DelayedMessageLookahead),
            _Duration("Lookback", _QueuedMessageLookback),
            new NpgsqlParameter("@BatchSize", messagingOptions.Value.SchedulerBatchSize),
        };

        await using var connection = postgreSqlOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var poisonMessages = new List<PoisonMessage>();
        var messageList = await connection
            .ExecuteReaderAsync(
                sql,
                async (reader, token) =>
                {
                    var messages = new List<MediumMessage>();
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
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
                                Added = await reader.GetFieldValueAsync<DateTimeOffset>(5, token).ConfigureAwait(false),
                                ExpiresAt = await reader.IsDBNullAsync(6, token).ConfigureAwait(false)
                                    ? null
                                    : await reader.GetFieldValueAsync<DateTimeOffset>(6, token).ConfigureAwait(false),
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
                ["\"id\""],
                $"""
                "version"=@Version
                AND "intent_type" IN (0, 1)
                AND ("locked_until" IS NULL OR "locked_until" <= {SqlDialectTokens.Now})
                AND (
                    ("status_name"=@DelayedStatusName AND "expires_at" < {_Dialect.ShiftByDuration(
                    SqlDialectTokens.Now,
                    "Lookahead"
                )})
                    OR ("status_name"=@QueuedStatusName AND "expires_at" < {_Dialect.ShiftByDuration(
                    SqlDialectTokens.Now,
                    "Lookback",
                    subtract: true
                )})
                )
                AND {_TerminalRowGuardSimple}
                """,
                ["\"expires_at\"", "\"id\""],
                $"\"status_name\"=@QueuedStatusName, \"locked_until\"={_Dialect.ShiftByDuration($"GREATEST({SqlDialectTokens.Now}, \"expires_at\")", "Lease")}, \"owner\"=@Owner",
                [
                    "\"id\"",
                    "\"content\"",
                    "\"intent_type\"",
                    "\"retries\"",
                    "\"inline_attempts\"",
                    "\"added\"",
                    "\"expires_at\"",
                    "\"locked_until\"",
                    "\"owner\"",
                ],
                BatchSizeParameter: "BatchSize"
            )
        );

        // The transaction commits without the caller's token: PostgreSQL may commit after accepting COMMIT even when
        // the client then observes cancellation, and the claim must not lose winners it already leased.
        var claimed = await SqlAutonomousTransaction
            .RunAsync(
                _Dialect,
                () => postgreSqlOptions.Value.CreateConnection(),
                async (connection, transaction, ct) =>
                {
                    object[] sqlParams =
                    [
                        _VersionParameter(),
                        new NpgsqlParameter("@DelayedStatusName", nameof(StatusName.Delayed)),
                        new NpgsqlParameter("@QueuedStatusName", nameof(StatusName.Queued)),
                        _Duration("Lookahead", _DelayedMessageLookahead),
                        _Duration("Lookback", _QueuedMessageLookback),
                        new NpgsqlParameter("@BatchSize", messagingOptions.Value.SchedulerBatchSize),
                        _Duration("Lease", messagingOptions.Value.RetryPolicy.DispatchTimeout),
                        new NpgsqlParameter("@Owner", NpgsqlDbType.Varchar)
                        {
                            Value = nodeMembership.GetOwnerTag() ?? (object)DBNull.Value,
                        },
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
                    ct.ThrowIfCancellationRequested();

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
