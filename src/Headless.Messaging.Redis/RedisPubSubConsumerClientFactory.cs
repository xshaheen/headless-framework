// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Exceptions;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Redis;

internal sealed class RedisPubSubConsumerClientFactory(
    IRedisPubSubConnectionProvider connectionProvider,
    IOptions<RedisPubSubMessagingOptions> options,
    ILogger<RedisPubSubConsumerClient> logger,
    TimeProvider timeProvider
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

        if (lane == MessageLane.Queue)
        {
            throw new NotSupportedException(
                "Headless.Messaging.Redis Pub/Sub is a bus transport provider and cannot create queue consumers."
            );
        }

        if (lane != MessageLane.Bus)
        {
            throw new ArgumentOutOfRangeException(nameof(lane), lane, message: null);
        }

        try
        {
            return Task.FromResult<IConsumerClient>(
                new RedisPubSubConsumerClient(
                    groupName,
                    groupConcurrent,
                    connectionProvider,
                    options,
                    logger,
                    timeProvider
                )
            );
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new BrokerConnectionException(e);
        }
    }
}
