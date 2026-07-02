// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Transport;

namespace Headless.Messaging.Redis;

internal sealed class RedisConsumerClientFactorySelector(
    IEnumerable<RedisConsumerClientFactory> queueFactories,
    IEnumerable<RedisPubSubConsumerClientFactory> busFactories
) : IIntentAwareConsumerClientFactory
{
    private readonly RedisConsumerClientFactory? _queueFactory = queueFactories.LastOrDefault();
    private readonly RedisPubSubConsumerClientFactory? _busFactory = busFactories.LastOrDefault();

    public Task<IConsumerClient> CreateAsync(string groupName, byte groupConcurrent)
    {
        if (_busFactory is not null)
        {
            return _busFactory.CreateAsync(groupName, groupConcurrent);
        }

        if (_queueFactory is not null)
        {
            return _queueFactory.CreateAsync(groupName, groupConcurrent);
        }

        throw new InvalidOperationException("Headless.Messaging.Redis has no configured consumer client factory.");
    }

    public Task<IConsumerClient> CreateAsync(string groupName, byte groupConcurrent, IntentType intentType)
    {
#pragma warning disable CA2000 // The selected factory transfers IConsumerClient ownership to the caller.
        return intentType switch
        {
            IntentType.Bus when _busFactory is not null => _busFactory.CreateAsync(groupName, groupConcurrent),
            IntentType.Queue when _queueFactory is not null => _queueFactory.CreateAsync(groupName, groupConcurrent),
            IntentType.Bus => throw new InvalidOperationException(
                "Headless.Messaging.Redis was not configured for Redis Pub/Sub bus delivery. Call UseRedisPubSub(...)."
            ),
            IntentType.Queue => throw new InvalidOperationException(
                "Headless.Messaging.Redis was not configured for Redis Streams queue delivery. Call UseRedis(...)."
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(intentType), intentType, message: null),
        };
#pragma warning restore CA2000
    }
}
