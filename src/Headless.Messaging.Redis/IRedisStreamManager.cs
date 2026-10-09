// Copyright (c) Mahmoud Shaheen. All rights reserved.

using StackExchange.Redis;

namespace Headless.Messaging.Redis;

internal interface IRedisStreamManager
{
    Task CreateStreamWithConsumerGroupAsync(
        string stream,
        string consumerGroup,
        CancellationToken cancellationToken = default
    );

    Task PublishAsync(string stream, NameValueEntry[] message, CancellationToken cancellationToken = default);

    IAsyncEnumerable<IEnumerable<RedisStreamMessages>> PollStreamsLatestMessagesAsync(
        string[] streams,
        string consumerGroup,
        string consumerName,
        TimeSpan pollDelay,
        CancellationToken token
    );

    IAsyncEnumerable<IEnumerable<RedisStreamMessages>> PollStreamsPendingMessagesAsync(
        string[] streams,
        string consumerGroup,
        string consumerName,
        TimeSpan pollDelay,
        CancellationToken token
    );

    /// <summary>
    /// Polls the groups' pending entries with XAUTOCLAIM, claiming for <paramref name="consumerName"/> each entry pending
    /// longer than <paramref name="claimMinIdleTime"/>, and periodically deletes the group's idle consumers that hold no
    /// pending entry.
    /// </summary>
    IAsyncEnumerable<IEnumerable<RedisStreamMessages>> PollStreamsStalePendingMessagesAsync(
        string[] streams,
        string consumerGroup,
        string consumerName,
        TimeSpan claimMinIdleTime,
        TimeSpan pollDelay,
        CancellationToken token
    );

    /// <summary>
    /// Returns, for each stream, the position just past its newest entry, or the start of an absent stream, so a read
    /// from these positions returns only entries added after this call.
    /// </summary>
    Task<StreamPosition[]> GetStreamTailPositionsAsync(string[] streams, CancellationToken cancellationToken = default);

    /// <summary>
    /// Polls <paramref name="startPositions"/> with group-less XREAD, advancing each stream past the entries it
    /// yields, so the reads need no consumer group and leave nothing on the server.
    /// </summary>
    IAsyncEnumerable<IEnumerable<RedisStreamMessages>> PollStreamsFromAsync(
        StreamPosition[] startPositions,
        TimeSpan pollDelay,
        CancellationToken token
    );

    Task Ack(string stream, string consumerGroup, string messageId, CancellationToken cancellationToken = default);
}

internal readonly record struct RedisStreamMessages(RedisKey Key, StreamEntry[] Entries);
