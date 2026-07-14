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

    IAsyncEnumerable<IEnumerable<RedisStreamMessages>> PollStreamsStalePendingMessagesAsync(
        string[] streams,
        string consumerGroup,
        string consumerName,
        TimeSpan claimMinIdleTime,
        TimeSpan pollDelay,
        CancellationToken token
    );

    Task Ack(string stream, string consumerGroup, string messageId, CancellationToken cancellationToken = default);

    Task RequeueAndAck(
        string stream,
        string consumerGroup,
        string messageId,
        NameValueEntry[] entries,
        CancellationToken cancellationToken = default
    );
}

internal readonly record struct RedisStreamMessages(RedisKey Key, StreamEntry[] Entries);
