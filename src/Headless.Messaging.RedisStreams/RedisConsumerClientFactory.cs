// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.RedisStreams;

internal sealed class RedisConsumerClientFactory(
    IOptions<MessagingRedisOptions> redisOptions,
    IRedisStreamManager redis,
    ILogger<RedisConsumerClient> logger
) : IIntentAwareConsumerClientFactory
{
    public Task<IConsumerClient> CreateAsync(string groupName, byte groupConcurrent)
    {
        return CreateAsync(groupName, groupConcurrent, IntentType.Queue);
    }

    public Task<IConsumerClient> CreateAsync(string groupName, byte groupConcurrent, IntentType intentType)
    {
        _ = intentType;
        var client = new RedisConsumerClient(groupName, groupConcurrent, redis, redisOptions, logger);
        return Task.FromResult<IConsumerClient>(client);
    }
}
