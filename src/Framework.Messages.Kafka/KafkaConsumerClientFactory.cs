// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Messages.Exceptions;
using Framework.Messages.Transport;
using Microsoft.Extensions.Options;

namespace Framework.Messages;

public class KafkaConsumerClientFactory(IOptions<KafkaOptions> kafkaOptions, IServiceProvider serviceProvider)
    : IConsumerClientFactory
{
    public virtual Task<IConsumerClient> CreateAsync(string groupName, byte groupConcurrent)
    {
        try
        {
            return Task.FromResult<IConsumerClient>(
                new KafkaConsumerClient(groupName, groupConcurrent, kafkaOptions, serviceProvider)
            );
        }
        catch (Exception e)
        {
            throw new BrokerConnectionException(e);
        }
    }
}
