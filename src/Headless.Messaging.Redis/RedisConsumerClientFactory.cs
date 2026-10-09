// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Redis;

internal sealed class RedisConsumerClientFactory(
    IOptions<RedisMessagingOptions> redisOptions,
    IRedisStreamManager redis,
    ILogger<RedisConsumerClient> logger
) : IConsumerClientFactory
{
    // One per factory, which the container holds as a singleton, so the clients of one process never share a name.
    private readonly RedisConsumerNames _consumerNames = new();

    public Task<IConsumerClient> CreateAsync(
        ConsumerClientRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);

        cancellationToken.ThrowIfCancellationRequested();

        // An every-instance client reads without a consumer group, so it needs no consumer name.
        var consumerName =
            request.Kind is ConsumerSubscriptionKind.Competing
                ? _consumerNames.Acquire(RedisPhysicalAddress.ConsumerGroup(request.Lane, request.SubscriptionName))
                : null;

        var client = new RedisConsumerClient(
            request.SubscriptionName,
            request.Concurrency,
            redis,
            redisOptions,
            logger,
            request.Lane,
            kind: request.Kind,
            consumerName: consumerName
        );
        return Task.FromResult<IConsumerClient>(client);
    }
}
