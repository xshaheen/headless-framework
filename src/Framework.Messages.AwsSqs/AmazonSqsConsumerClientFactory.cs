// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Messages.Exceptions;
using Framework.Messages.Transport;
using Microsoft.Extensions.Options;

namespace Framework.Messages;

internal sealed class AmazonSqsConsumerClientFactory(IOptions<AmazonSqsOptions> amazonSqsOptions)
    : IConsumerClientFactory
{
    public Task<IConsumerClient> CreateAsync(string groupName, byte groupConcurrent)
    {
        try
        {
            var client = new AmazonSqsConsumerClient(groupName, groupConcurrent, amazonSqsOptions);
            return Task.FromResult<IConsumerClient>(client);
        }
        catch (Exception e)
        {
            throw new BrokerConnectionException(e);
        }
    }
}
