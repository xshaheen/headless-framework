// Copyright (c) Mahmoud Shaheen. All rights reserved.

using StackExchange.Redis;

namespace Framework.Messages;

internal interface IRedisStreamManager
{
    Task CreateStreamWithConsumerGroupAsync(string stream, string consumerGroup);
    Task PublishAsync(string stream, NameValueEntry[] message);

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

    Task Ack(string stream, string consumerGroup, string messageId);
}
