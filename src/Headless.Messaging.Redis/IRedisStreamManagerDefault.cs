// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Headless.Messaging.Redis;

internal sealed class RedisStreamManager(
    IRedisConnectionPool connectionsPool,
    IOptions<MessagingRedisOptions> options,
    ILogger<RedisStreamManager> logger
) : IRedisStreamManager
{
    private readonly MessagingRedisOptions _options = options.Value;
    private IConnectionMultiplexer? _redis;

    public async Task CreateStreamWithConsumerGroupAsync(string stream, string consumerGroup)
    {
        await _ConnectAsync().ConfigureAwait(false);

        //The object returned from GetDatabase is a cheap pass - thru object, and does not need to be stored
        var database = _redis!.GetDatabase();

        await database.TryGetOrCreateStreamConsumerGroupAsync(stream, consumerGroup).ConfigureAwait(false);
    }

    public async Task PublishAsync(string stream, NameValueEntry[] message)
    {
        await _ConnectAsync().ConfigureAwait(false);

        //The object returned from GetDatabase is a cheap pass - thru object, and does not need to be stored
        await _redis!.GetDatabase().StreamAddAsync(stream, message).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<IEnumerable<RedisStream>> PollStreamsLatestMessagesAsync(
        string[] streams,
        string consumerGroup,
        TimeSpan pollDelay,
        [EnumeratorCancellation] CancellationToken token
    )
    {
        // Subscription set is fixed for the consumer's lifetime, so materialize positions once
        // instead of rebuilding the StreamPosition[] on every poll iteration.
        var positions = streams.Select(stream => new StreamPosition(stream, StreamPosition.NewMessages)).ToArray();

        while (true)
        {
            var result = await _TryReadConsumerGroupAsync(consumerGroup, positions, token).ConfigureAwait(false);

            yield return result;

            token.WaitHandle.WaitOne(pollDelay);
        }

        // ReSharper disable once IteratorNeverReturns
    }

    public async IAsyncEnumerable<IEnumerable<RedisStream>> PollStreamsPendingMessagesAsync(
        string[] streams,
        string consumerGroup,
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
                await _TryReadConsumerGroupAsync(consumerGroup, positions, token).ConfigureAwait(false)
            ).ToArray();

            yield return result;

            //Once we consumed our history of pending messages, we can break the loop.
            if (result.All(s => s.Entries.Length < _options.StreamEntriesCount))
            {
                break;
            }

            token.WaitHandle.WaitOne(pollDelay);
        }
    }

    public async Task Ack(string stream, string consumerGroup, string messageId)
    {
        await _ConnectAsync().ConfigureAwait(false);

        await _redis!.GetDatabase().StreamAcknowledgeAsync(stream, consumerGroup, messageId).ConfigureAwait(false);
    }

    private async Task<IEnumerable<RedisStream>> _TryReadConsumerGroupAsync(
        string consumerGroup,
        StreamPosition[] positions,
        CancellationToken token
    )
    {
        try
        {
            token.ThrowIfCancellationRequested();

            List<StreamPosition> createdPositions = [];

            await _ConnectAsync().ConfigureAwait(false);

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
                return [];
            }

            //calculate keys HashSlots to start reading per HashSlot
            var groupedPositions = createdPositions
                .GroupBy(s => _redis.GetHashSlot(s.Key))
                .Select(group =>
                    database.StreamReadGroupAsync(
                        [.. group],
                        consumerGroup,
                        consumerGroup,
                        (int)_options.StreamEntriesCount
                    )
                );

            var readSet = await Task.WhenAll(groupedPositions).ConfigureAwait(false);

            return readSet.SelectMany(set => set);
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch (Exception ex)
        {
            logger.LogReadConsumerGroupFailed(ex, consumerGroup);
        }

        return [];
    }

    private async Task _ConnectAsync()
    {
        _redis = await connectionsPool.ConnectAsync().ConfigureAwait(false);
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
}
