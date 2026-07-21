// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Transport;

namespace Headless.Messaging.Redis;

internal sealed class RedisConsumerClientFactorySelector(
    IEnumerable<RedisConsumerClientFactory> queueFactories,
    IEnumerable<RedisPubSubConsumerClientFactory> busFactories
) : IConsumerClientFactory
{
    private readonly RedisConsumerClientFactory? _queueFactory = queueFactories.LastOrDefault();
    private readonly RedisPubSubConsumerClientFactory? _busFactory = busFactories.LastOrDefault();

    public Task<IConsumerClient> CreateAsync(
        string groupName,
        byte groupConcurrent,
        MessageLane lane,
        CancellationToken cancellationToken = default
    )
    {
#pragma warning disable CA2000 // The selected factory transfers IConsumerClient ownership to the caller.
        return lane switch
        {
            MessageLane.Bus when _busFactory is not null => _busFactory.CreateAsync(
                groupName,
                groupConcurrent,
                lane,
                cancellationToken
            ),
            MessageLane.Queue when _queueFactory is not null => _queueFactory.CreateAsync(
                groupName,
                groupConcurrent,
                lane,
                cancellationToken
            ),
            MessageLane.Bus => throw new InvalidOperationException(
                "Headless.Messaging.Redis was not configured for Redis Pub/Sub bus delivery. Call UseRedisPubSub(...)."
            ),
            MessageLane.Queue => throw new InvalidOperationException(
                "Headless.Messaging.Redis was not configured for Redis Streams queue delivery. Call UseRedis(...)."
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(lane), lane, message: null),
        };
#pragma warning restore CA2000
    }
}
