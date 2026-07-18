// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.Abstractions;
using Headless.Coordination;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.Serialization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
        var sql = $"""
            WITH DelayedCandidates AS (
                SELECT TOP (@BatchSize) Id, Content, IntentType, Retries, InlineAttempts, Added, ExpiresAt
                FROM {_publishedTable} WITH (UPDLOCK, READPAST)
                WHERE Version = @Version AND StatusName = @DelayedStatusName AND ExpiresAt < @TwoMinutesLater
                ORDER BY ExpiresAt, Id
            ),
            QueuedCandidates AS (
                SELECT TOP (@BatchSize) Id, Content, IntentType, Retries, InlineAttempts, Added, ExpiresAt
                FROM {_publishedTable} WITH (UPDLOCK, READPAST)
                WHERE Version = @Version AND StatusName = @QueuedStatusName AND ExpiresAt < @OneMinutesAgo
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
            new SqlParameter("@Version", messagingOptions.Value.Version),
            new SqlParameter("@DelayedStatusName", nameof(StatusName.Delayed)),
            new SqlParameter("@QueuedStatusName", nameof(StatusName.Queued)),
            new SqlParameter("@TwoMinutesLater", timeProvider.GetUtcNow().Add(_DelayedMessageLookahead)),
            new SqlParameter("@OneMinutesAgo", timeProvider.GetUtcNow().Subtract(_QueuedMessageLookback)),
            new SqlParameter("@BatchSize", messagingOptions.Value.SchedulerBatchSize),
        ];

        await using var connection = new SqlConnection(options.Value.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
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
                                IntentType = (IntentType)reader.GetInt16(2),
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
        var sql = $"""
            DECLARE @ClaimNow datetimeoffset(7) = SYSUTCDATETIME();

            WITH Candidates AS (
                SELECT TOP (@BatchSize) Id
                FROM {_publishedTable} WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE Version=@Version
                  AND (LockedUntil IS NULL OR LockedUntil <= @ClaimNow)
                  AND (
                      (StatusName=@DelayedStatusName AND ExpiresAt < @TwoMinutesLater)
                      OR (StatusName=@QueuedStatusName AND ExpiresAt < @OneMinuteAgo)
                  )
                ORDER BY ExpiresAt, Id
            )
            UPDATE target
            SET StatusName=@QueuedStatusName,
                LockedUntil=DATEADD(
                    nanosecond,
                    @LeaseNanoseconds,
                    DATEADD(
                        second,
                        @LeaseWholeSeconds,
                        CASE WHEN target.ExpiresAt > @ClaimNow THEN target.ExpiresAt ELSE @ClaimNow END
                    )
                ),
                Owner=@Owner
            OUTPUT inserted.Id,inserted.Content,inserted.IntentType,inserted.Retries,
                   inserted.InlineAttempts,inserted.Added,inserted.ExpiresAt,
                   inserted.LockedUntil,inserted.Owner
            FROM {_publishedTable} AS target
            INNER JOIN Candidates ON target.Id=Candidates.Id
            WHERE (target.LockedUntil IS NULL OR target.LockedUntil <= @ClaimNow)
              AND {_TerminalRowGuardSimple};
            """;

        var scheduleNow = timeProvider.GetUtcNow();
        var (leaseWholeSeconds, leaseNanoseconds) = _SplitLeaseDuration(
            messagingOptions.Value.RetryPolicy.DispatchTimeout
        );
        object[] sqlParams =
        [
            new SqlParameter("@BatchSize", messagingOptions.Value.SchedulerBatchSize),
            new SqlParameter("@Version", messagingOptions.Value.Version),
            new SqlParameter("@DelayedStatusName", nameof(StatusName.Delayed)),
            new SqlParameter("@QueuedStatusName", nameof(StatusName.Queued)),
            new SqlParameter("@TwoMinutesLater", SqlDbType.DateTimeOffset)
            {
                Value = scheduleNow.Add(_DelayedMessageLookahead),
            },
            new SqlParameter("@OneMinuteAgo", SqlDbType.DateTimeOffset)
            {
                Value = scheduleNow.Subtract(_QueuedMessageLookback),
            },
            new SqlParameter("@LeaseWholeSeconds", SqlDbType.Int) { Value = leaseWholeSeconds },
            new SqlParameter("@LeaseNanoseconds", SqlDbType.Int) { Value = leaseNanoseconds },
            _OwnerParameter("@Owner", hasLease: true),
        ];

        await using var connection = new SqlConnection(options.Value.ConnectionString);
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
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

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
