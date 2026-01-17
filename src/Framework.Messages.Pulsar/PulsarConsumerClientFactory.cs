// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Messages.Exceptions;
using Framework.Messages.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pulsar.Client.Api;

namespace Framework.Messages;

internal sealed class PulsarConsumerClientFactory : IConsumerClientFactory
{
    private readonly IConnectionFactory _connection;
    private readonly IOptions<PulsarOptions> _pulsarOptions;

    public PulsarConsumerClientFactory(
        IConnectionFactory connection,
        ILoggerFactory loggerFactory,
        IOptions<PulsarOptions> pulsarOptions
    )
    {
        _connection = connection;
        _pulsarOptions = pulsarOptions;

        if (_pulsarOptions.Value.EnableClientLog)
            PulsarClient.Logger = loggerFactory.CreateLogger<PulsarClient>();
    }

    public Task<IConsumerClient> CreateAsync(string groupName, byte groupConcurrent)
    {
        try
        {
            var client = _connection.RentClient();
            var consumerClient = new PulsarConsumerClient(_pulsarOptions, client, groupName, groupConcurrent);
            return Task.FromResult<IConsumerClient>(consumerClient);
        }
        catch (Exception e)
        {
            throw new BrokerConnectionException(e);
        }
    }
}
