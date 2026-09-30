// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Exceptions;
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
        ConsumerClientRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);

        if (request.Kind is ConsumerSubscriptionKind.EveryInstance)
        {
            throw new NotSupportedException("The Amazon SQS transport does not support every-instance subscriptions.");
        }

        var subscriptionName = request.SubscriptionName;
        var concurrency = request.Concurrency;
        var lane = request.Lane;

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var client = new AmazonSqsConsumerClient(subscriptionName, concurrency, amazonSqsOptions, logger, lane);
            return Task.FromResult<IConsumerClient>(client);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new BrokerConnectionException(e);
        }
    }
}
