// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Exceptions;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Aws;

internal sealed class AmazonSqsConsumerClientFactory(
    IOptions<AmazonSqsMessagingOptions> amazonSqsOptions,
    ILogger<AmazonSqsConsumerClient> logger
) : IIntentAwareConsumerClientFactory
{
    public Task<IConsumerClient> CreateAsync(
        string groupName,
        byte groupConcurrent,
        CancellationToken cancellationToken = default
    )
    {
        return CreateAsync(groupName, groupConcurrent, IntentType.Bus, cancellationToken);
    }

    public Task<IConsumerClient> CreateAsync(
        string groupName,
        byte groupConcurrent,
        IntentType intentType,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var client = new AmazonSqsConsumerClient(groupName, groupConcurrent, amazonSqsOptions, logger, intentType);
            return Task.FromResult<IConsumerClient>(client);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new BrokerConnectionException(e);
        }
    }
}
