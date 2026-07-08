// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Configuration;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Redis;

internal sealed class RedisConsumerClientFactory(
    IOptions<RedisMessagingOptions> redisOptions,
    IOptions<MessagingOptions> messagingOptions,
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
        if (intentType == IntentType.Bus)
        {
            throw new NotSupportedException(
                "Headless.Messaging.Redis is a queue transport provider and cannot create bus consumers."
            );
        }

        var client = new RedisConsumerClient(
            groupName,
            groupConcurrent,
            redis,
            redisOptions,
            logger,
            messagingOptions.Value.RetryPolicy.DispatchTimeout
        );
        return Task.FromResult<IConsumerClient>(client);
    }
}
