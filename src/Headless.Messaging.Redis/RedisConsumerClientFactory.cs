// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
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
        ConsumerClientRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);

        if (request.Kind is ConsumerSubscriptionKind.EveryInstance)
        {
            throw new NotSupportedException("The Redis transport does not support every-instance subscriptions.");
        }

        var subscriptionName = request.SubscriptionName;
        var concurrency = request.Concurrency;
        var lane = request.Lane;

        cancellationToken.ThrowIfCancellationRequested();

        var client = new RedisConsumerClient(
            subscriptionName,
            concurrency,
            redis,
            redisOptions,
            logger,
            lane,
            messagingOptions.Value.RetryPolicy.DispatchTimeout
        );
        return Task.FromResult<IConsumerClient>(client);
    }
}
