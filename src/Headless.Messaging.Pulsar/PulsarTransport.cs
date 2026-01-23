// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Pulsar;

internal sealed class PulsarTransport(ILogger<PulsarTransport> logger, IConnectionFactory connectionFactory)
    : ITransport
{
    private readonly ILogger _logger = logger;

    public BrokerAddress BrokerAddress => new("Pulsar", connectionFactory.ServersAddress);

    public async Task<OperateResult> SendAsync(TransportMessage message)
    {
        var producer = await connectionFactory.CreateProducerAsync(message.GetName()).AnyContext();

        try
        {
            var headerDic = new Dictionary<string, string?>(message.Headers, StringComparer.Ordinal);
            headerDic.TryGetValue(PulsarHeaders.PulsarKey, out var key);
            var pulsarMessage = producer.NewMessage(message.Body.ToArray(), key, headerDic);
            var messageId = await producer.SendAsync(pulsarMessage).AnyContext();

            if (messageId != null)
            {
                _logger.LogDebug("pulsar topic message [{GetName}] has been published.", message.GetName());

                return OperateResult.Success;
            }

            throw new PublisherSentFailedException("pulsar message persisted failed!");
        }
        catch (Exception ex)
        {
            var wrapperEx = new PublisherSentFailedException(ex.Message, ex);

            return OperateResult.Failed(wrapperEx);
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
