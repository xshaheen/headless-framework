// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Headless.Messaging.Redis;

internal sealed class RedisStreamManager(
    IRedisConnectionPool connectionsPool,
    IOptions<RedisMessagingOptions> options,
    ILogger<RedisStreamManager> logger,
    TimeProvider? timeProvider = null
) : IRedisStreamManager
{
    private readonly RedisMessagingOptions _options = options.Value;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private IConnectionMultiplexer? _redis;

    public async Task CreateStreamWithConsumerGroupAsync(
        string stream,
        string consumerGroup,
        CancellationToken cancellationToken = default
    )
    {
        await _ConnectAsync(cancellationToken).ConfigureAwait(false);

        //The object returned from GetDatabase is a cheap pass - thru object, and does not need to be stored
        var database = _redis!.GetDatabase();

        await database
            .TryGetOrCreateStreamConsumerGroupAsync(stream, consumerGroup)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task PublishAsync(
        string stream,
        NameValueEntry[] message,
        CancellationToken cancellationToken = default
    )
    {
        await _ConnectAsync(cancellationToken).ConfigureAwait(false);

        //The object returned from GetDatabase is a cheap pass - thru object, and does not need to be stored
        await _redis!.GetDatabase().StreamAddAsync(stream, message).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<IEnumerable<RedisStreamMessages>> PollStreamsLatestMessagesAsync(
        string[] streams,
        string consumerGroup,
        string consumerName,
        TimeSpan pollDelay,
        [EnumeratorCancellation] CancellationToken token
    )
    {
        // Subscription set is fixed for the consumer's lifetime, so materialize positions once
        // instead of rebuilding the StreamPosition[] on every poll iteration.
        var positions = streams.Select(stream => new StreamPosition(stream, StreamPosition.NewMessages)).ToArray();

        var errorDelay = pollDelay;

        while (true)
        {
            var (succeeded, result) = await _TryReadConsumerGroupAsync(consumerGroup, consumerName, positions, token)
                .ConfigureAwait(false);

            yield return result;

            if (succeeded)
            {
                errorDelay = pollDelay;
                await _timeProvider.Delay(pollDelay, token).ConfigureAwait(false);
            }
            else
            {
                // During a Redis outage back off instead of re-hammering at the fixed poll cadence.
                errorDelay = _NextBackoff(errorDelay);
                await _timeProvider.Delay(errorDelay, token).ConfigureAwait(false);
            }
        }

        // ReSharper disable once IteratorNeverReturns
    }

    public async IAsyncEnumerable<IEnumerable<RedisStreamMessages>> PollStreamsPendingMessagesAsync(
        string[] streams,
        string consumerGroup,
        string consumerName,
        TimeSpan pollDelay,
        [EnumeratorCancellation] CancellationToken token
    )
    {
        // Subscription set is fixed for the consumer's lifetime, so materialize positions once
        // instead of rebuilding the StreamPosition[] on every poll iteration.
        var positions = streams.Select(stream => new StreamPosition(stream, StreamPosition.Beginning)).ToArray();

        while (true)
        {
            token.ThrowIfCancellationRequested();

            // Materialize the lazy SelectMany result once: it is both yielded to the consumer and
            // re-inspected by the All() check below, so leaving it deferred would flatten it twice.
            var result = (
                await _TryReadConsumerGroupAsync(consumerGroup, consumerName, positions, token).ConfigureAwait(false)
            ).Streams.ToArray();

            yield return result;

            //Once we consumed our history of pending messages, we can break the loop.
            if (result.All(s => s.Entries.Length < _options.StreamEntriesCount))
            {
                break;
            }

            await _timeProvider.Delay(pollDelay, token).ConfigureAwait(false);
        }
    }

    public async IAsyncEnumerable<IEnumerable<RedisStreamMessages>> PollStreamsStalePendingMessagesAsync(
        string[] streams,
        string consumerGroup,
        string consumerName,
        TimeSpan claimMinIdleTime,
        TimeSpan pollDelay,
        [EnumeratorCancellation] CancellationToken token
    )
    {
        var positions = streams.Select(stream => new StreamPosition(stream, StreamPosition.Beginning)).ToArray();
        var nextStartIds = streams.ToDictionary(stream => (RedisKey)stream, _ => StreamPosition.Beginning);
        var errorDelay = pollDelay;

        while (true)
        {
            var (succeeded, result) = await _TryAutoClaimStalePendingAsync(
                    consumerGroup,
                    consumerName,
                    positions,
                    nextStartIds,
                    claimMinIdleTime,
                    _options.StreamEntriesCount,
                    token
                )
                .ConfigureAwait(false);

            yield return result;

            if (succeeded)
            {
                errorDelay = pollDelay;
                await _timeProvider.Delay(pollDelay, token).ConfigureAwait(false);
            }
            else
            {
                errorDelay = _NextBackoff(errorDelay);
                await _timeProvider.Delay(errorDelay, token).ConfigureAwait(false);
            }
        }

        // ReSharper disable once IteratorNeverReturns
    }

    public async Task Ack(
        string stream,
        string consumerGroup,
        string messageId,
        CancellationToken cancellationToken = default
    )
    {
        await _ConnectAsync(cancellationToken).ConfigureAwait(false);

        await _redis!.GetDatabase().StreamAcknowledgeAsync(stream, consumerGroup, messageId).ConfigureAwait(false);
    }

    public async Task RequeueAndAck(
        string stream,
        string consumerGroup,
        string messageId,
        NameValueEntry[] entries,
        CancellationToken cancellationToken = default
    )
    {
        await _ConnectAsync(cancellationToken).ConfigureAwait(false);

        var database = _redis!.GetDatabase();

        // Preserve at-least-once semantics: if requeue succeeds and ack fails, Redis may redeliver
        // both entries; if requeue fails, the original entry remains pending for stale-claim recovery.
        await database.StreamAddAsync(stream, entries).ConfigureAwait(false);
        await database.StreamAcknowledgeAsync(stream, consumerGroup, messageId).ConfigureAwait(false);
    }

    private async Task<(bool Succeeded, IEnumerable<RedisStreamMessages> Streams)> _TryReadConsumerGroupAsync(
        string consumerGroup,
        string consumerName,
        StreamPosition[] positions,
        CancellationToken token
    )
    {
        try
        {
            token.ThrowIfCancellationRequested();

            List<StreamPosition> createdPositions = [];

            await _ConnectAsync(token).ConfigureAwait(false);

            var database = _redis!.GetDatabase();

            await foreach (
                var position in database
                    .TryGetOrCreateConsumerGroupPositionsAsync(positions, consumerGroup, logger)
                    .ConfigureAwait(false)
                    .WithCancellation(token)
            )
            {
                createdPositions.Add(position);
            }

            if (createdPositions.Count == 0)
            {
                return (Succeeded: true, Streams: []);
            }

            //calculate keys HashSlots to start reading per HashSlot
            var groupedPositions = createdPositions
                .GroupBy(s => _redis.GetHashSlot(s.Key))
                .Select(group =>
                    database.StreamReadGroupAsync(
                        [.. group],
                        consumerGroup,
                        consumerName,
                        _options.StreamEntriesCount,
                        noAck: false
                    )
                );

            var readSet = await Task.WhenAll(groupedPositions).ConfigureAwait(false);

            return (
                Succeeded: true,
                Streams: readSet.SelectMany(set =>
                    set.Select(stream => new RedisStreamMessages(stream.Key, stream.Entries))
                )
            );
        }
        catch (OperationCanceledException)
        {
            // Cancellation is not a read failure; the caller's next Delay(token) ends the poll loop.
            return (Succeeded: true, Streams: []);
        }
        catch (Exception ex)
        {
            logger.LogReadConsumerGroupFailed(ex, consumerGroup);
        }

        return (Succeeded: false, Streams: []);
    }

    private async Task<(bool Succeeded, IEnumerable<RedisStreamMessages> Streams)> _TryAutoClaimStalePendingAsync(
        string consumerGroup,
        string consumerName,
        StreamPosition[] positions,
        Dictionary<RedisKey, RedisValue> nextStartIds,
        TimeSpan claimMinIdleTime,
        int count,
        CancellationToken token
    )
    {
        try
        {
            token.ThrowIfCancellationRequested();

            List<RedisStreamMessages> streams = [];

            await _ConnectAsync(token).ConfigureAwait(false);

            var database = _redis!.GetDatabase();
            var minIdleTimeMilliseconds = _ToRedisMilliseconds(claimMinIdleTime);

            await foreach (
                var position in database
                    .TryGetOrCreateConsumerGroupPositionsAsync(positions, consumerGroup, logger)
                    .ConfigureAwait(false)
                    .WithCancellation(token)
            )
            {
                if (!nextStartIds.TryGetValue(position.Key, out var startId))
                {
                    startId = StreamPosition.Beginning;
                }

                var result = await database
                    .StreamAutoClaimAsync(
                        position.Key,
                        consumerGroup,
                        consumerName,
                        minIdleTimeMilliseconds,
                        startId,
                        count
                    )
                    .ConfigureAwait(false);

                if (result.IsNull)
                {
                    continue;
                }

                nextStartIds[position.Key] = result.NextStartId;

                if (result.ClaimedEntries.Length > 0)
                {
                    streams.Add(new RedisStreamMessages(position.Key, result.ClaimedEntries));
                }
            }

            return (Succeeded: true, Streams: streams);
        }
        catch (OperationCanceledException)
        {
            return (Succeeded: true, Streams: []);
        }
        catch (Exception ex)
        {
            logger.LogAutoClaimConsumerGroupFailed(ex, consumerGroup);
        }

        return (Succeeded: false, Streams: []);
    }

    private static long _ToRedisMilliseconds(TimeSpan value)
    {
        return Math.Max(1, (long)Math.Ceiling(value.TotalMilliseconds));
    }

    private static TimeSpan _NextBackoff(TimeSpan current)
    {
        var floor = TimeSpan.FromMilliseconds(200);
        var ceiling = TimeSpan.FromSeconds(30);
        var doubled = TimeSpan.FromTicks(Math.Max(current.Ticks * 2, floor.Ticks));
        var capped = doubled > ceiling ? ceiling : doubled;
#pragma warning disable CA5394 // Non-security jitter for retry backoff; cryptographic RNG is unnecessary here.
        var jitterMs = Random.Shared.Next(0, (int)Math.Max(1, capped.TotalMilliseconds / 4));
#pragma warning restore CA5394
        return capped + TimeSpan.FromMilliseconds(jitterMs);
    }

    private async Task _ConnectAsync(CancellationToken cancellationToken = default)
    {
        _redis = await connectionsPool.ConnectAsync(cancellationToken).ConfigureAwait(false);
    }
}

internal static partial class RedisStreamManagerLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "ReadConsumerGroupFailed",
        Level = LogLevel.Error,
        Message = "Redis error when trying read consumer group {ConsumerGroup}"
    )]
    public static partial void LogReadConsumerGroupFailed(
        this ILogger logger,
        Exception exception,
        string consumerGroup
    );

    [LoggerMessage(
        EventId = 2,
        EventName = "AutoClaimConsumerGroupFailed",
        Level = LogLevel.Error,
        Message = "Redis error when trying to auto-claim pending messages for consumer group {ConsumerGroup}"
    )]
    public static partial void LogAutoClaimConsumerGroupFailed(
        this ILogger logger,
        Exception exception,
        string consumerGroup
    );
}
