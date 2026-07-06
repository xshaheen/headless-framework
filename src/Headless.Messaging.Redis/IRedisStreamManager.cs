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

    IAsyncEnumerable<IEnumerable<RedisStream>> PollStreamsLatestMessagesAsync(
        string[] streams,
        string consumerGroup,
        TimeSpan pollDelay,
        CancellationToken token
    );

    IAsyncEnumerable<IEnumerable<RedisStream>> PollStreamsPendingMessagesAsync(
        string[] streams,
        string consumerGroup,
        TimeSpan pollDelay,
        CancellationToken token
    );

    Task Ack(string stream, string consumerGroup, string messageId, CancellationToken cancellationToken = default);
}
