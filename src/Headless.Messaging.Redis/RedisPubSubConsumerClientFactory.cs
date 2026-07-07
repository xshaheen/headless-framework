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
    public Task<IConsumerClient> CreateAsync(string groupName, byte groupConcurrent)
    {
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
        catch (Exception e)
        {
            throw new BrokerConnectionException(e);
        }
    }
}
