// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Exceptions;
using Headless.Messaging.Internal;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Aws;

internal sealed class AmazonSqsConsumerClientFactory(
    IOptions<AmazonSqsMessagingOptions> amazonSqsOptions,
    ILogger<AmazonSqsConsumerClient> logger
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

        try
        {
            var client = new AmazonSqsConsumerClient(
                groupName,
                groupConcurrent,
                amazonSqsOptions,
                logger,
                MessageLaneCompatibility.ToIntentType(lane)
            );
            return Task.FromResult<IConsumerClient>(client);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new BrokerConnectionException(e);
        }
    }
}
