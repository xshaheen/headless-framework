// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
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

        // SQS has no idle deletion, so a per-process queue and its SNS subscription would outlive a crashed process. The
        // capability declaration refuses the consumer at startup; the factory repeats it for callers outside the core.
        if (request.Kind is ConsumerSubscriptionKind.EveryInstance)
        {
            throw new NotSupportedException(
                $"Consumer '{request.SubscriptionName}' requires every-instance Bus delivery, which transport provider "
                    + "'Amazon SQS' does not support: SQS has no idle deletion, so a crashed process would leave its "
                    + "queue and SNS subscription behind. Remove EveryInstance from the consumer, or select a transport "
                    + "that supports every-instance subscriptions."
            );
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
