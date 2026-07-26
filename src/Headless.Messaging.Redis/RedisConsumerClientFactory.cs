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
) : IConsumerClientFactory
{
    public Task<IConsumerClient> CreateAsync(
        string groupName,
        byte groupConcurrent,
        MessageLane lane,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (lane is not (MessageLane.Bus or MessageLane.Queue))
        {
            throw new ArgumentOutOfRangeException(nameof(lane), lane, message: null);
        }

        var client = new RedisConsumerClient(
            groupName,
            groupConcurrent,
            redis,
            redisOptions,
            logger,
            lane,
            messagingOptions.Value.RetryPolicy.DispatchTimeout
        );
        return Task.FromResult<IConsumerClient>(client);
    }
}
