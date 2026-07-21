// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Abstractions;
using Headless.Coordination;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
        var sql =
            $"SELECT \"Id\",\"Content\",\"IntentType\",\"Retries\",\"InlineAttempts\",\"Added\",\"ExpiresAt\" FROM {_publishedTable} WHERE \"Version\"=@Version "
            + $"AND ((\"ExpiresAt\"< @TwoMinutesLater AND \"StatusName\" = '{nameof(StatusName.Delayed)}') OR (\"ExpiresAt\"< @OneMinutesAgo AND \"StatusName\" = '{nameof(StatusName.Queued)}')) FOR UPDATE SKIP LOCKED LIMIT @BatchSize;";

        var sqlParams = new object[]
        {
            new NpgsqlParameter("@Version", messagingOptions.Value.Version),
            new NpgsqlParameter("@TwoMinutesLater", timeProvider.GetUtcNow().Add(_DelayedMessageLookahead)),
            new NpgsqlParameter("@OneMinutesAgo", timeProvider.GetUtcNow().Subtract(_QueuedMessageLookback)),
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
                                IntentType = (IntentType)reader.GetInt16(2),
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
        var scheduleNow = timeProvider.GetUtcNow();
        var sql = $"""
            WITH claim_clock AS MATERIALIZED (
                SELECT clock_timestamp() AS now
            ),
            candidates AS MATERIALIZED (
                SELECT message."Id"
                FROM {_publishedTable} AS message, claim_clock
                WHERE message."Version"=@Version
                  AND (message."LockedUntil" IS NULL OR message."LockedUntil" <= claim_clock.now)
                  AND (
                      (message."StatusName"=@DelayedStatusName AND message."ExpiresAt" < @TwoMinutesLater)
                      OR (message."StatusName"=@QueuedStatusName AND message."ExpiresAt" < @OneMinuteAgo)
                  )
                ORDER BY message."ExpiresAt", message."Id"
                LIMIT @BatchSize
                FOR UPDATE OF message SKIP LOCKED
            )
            UPDATE {_publishedTable} AS message
            SET "StatusName"=@QueuedStatusName,
                "LockedUntil"=GREATEST(claim_clock.now, message."ExpiresAt")
                    + (@LeaseSeconds * INTERVAL '1 second'),
                "Owner"=@Owner
            FROM candidates, claim_clock
            WHERE message."Id"=candidates."Id"
              AND (message."LockedUntil" IS NULL OR message."LockedUntil" <= claim_clock.now)
              AND {_TerminalRowGuardSimple}
            RETURNING message."Id",message."Content",message."IntentType",message."Retries",
                      message."InlineAttempts",message."Added",message."ExpiresAt",
                      message."LockedUntil",message."Owner";
            """;

        object[] sqlParams =
        [
            new NpgsqlParameter("@Version", messagingOptions.Value.Version),
            new NpgsqlParameter("@DelayedStatusName", nameof(StatusName.Delayed)),
            new NpgsqlParameter("@QueuedStatusName", nameof(StatusName.Queued)),
            new NpgsqlParameter("@TwoMinutesLater", scheduleNow.Add(_DelayedMessageLookahead)),
            new NpgsqlParameter("@OneMinuteAgo", scheduleNow.Subtract(_QueuedMessageLookback)),
            new NpgsqlParameter("@BatchSize", messagingOptions.Value.SchedulerBatchSize),
            new NpgsqlParameter("@LeaseSeconds", messagingOptions.Value.RetryPolicy.DispatchTimeout.TotalSeconds),
            new NpgsqlParameter("@Owner", NpgsqlDbType.Varchar)
            {
                Value = nodeMembership.GetOwnerTag() ?? (object)DBNull.Value,
            },
        ];

        await using var connection = postgreSqlOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var poisonMessages = new List<PoisonMessage>();
        var claimed = await connection
            .ExecuteReaderAsync(
                sql,
                async (reader, token) =>
                {
                    var messages = new List<MediumMessage>();
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
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
                                    IntentType = (IntentType)reader.GetInt16(2),
                                    Retries = reader.GetInt32(3),
                                    InlineAttempts = reader.GetInt32(4),
                                    Added = await reader
                                        .GetFieldValueAsync<DateTimeOffset>(5, token)
                                        .ConfigureAwait(false),
                                    ExpiresAt = await reader
                                        .GetFieldValueAsync<DateTimeOffset>(6, token)
                                        .ConfigureAwait(false),
                                    LockedUntil = await reader
                                        .GetFieldValueAsync<DateTimeOffset>(7, token)
                                        .ConfigureAwait(false),
                                    Owner = await reader.IsDBNullAsync(8, token).ConfigureAwait(false)
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
                },
                transaction: transaction,
                commandTimeout: messagingOptions.Value.CommandTimeout,
                sqlParams: sqlParams,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        await _MarkPoisonMessagesTerminalAsync(
                connection,
                transaction,
                _publishedTable,
                poisonMessages,
                cancellationToken
            )
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        // PostgreSQL may commit after accepting COMMIT even when the client subsequently observes cancellation.
        // Once commit starts, observe its definitive outcome so callers never lose committed claim winners.
        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);

        claimed.Sort(
            static (left, right) =>
            {
                var expiresComparison = Nullable.Compare(left.ExpiresAt, right.ExpiresAt);
                return expiresComparison != 0 ? expiresComparison : left.StorageId.CompareTo(right.StorageId);
            }
        );
        return claimed;
    }
}
