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

    public BrokerAddress BrokerAddress => new("pulsar", connectionFactory.ServersAddress);

    public async Task<OperateResult> SendAsync(TransportMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var producer = await connectionFactory.CreateProducerAsync(message.GetName()).ConfigureAwait(false);
            var headerDic = new Dictionary<string, string?>(message.Headers, StringComparer.Ordinal);
            headerDic.TryGetValue(PulsarHeaders.PulsarKey, out var key);
            var pulsarMessage = producer.NewMessage(message.Body.ToArray(), key, headerDic);
            var messageId = await producer.SendAsync(pulsarMessage).ConfigureAwait(false);

            if (messageId != null)
            {
                var messageName = message.GetName();
                _logger.MessagePublished(messageName);

                return OperateResult.Success;
            }

            throw new PublisherSentFailedException("pulsar message persisted failed!");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var wrapperEx = new PublisherSentFailedException(ex.Message, ex);

            return OperateResult.Failed(wrapperEx);
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal static partial class PulsarTransportLog
{
    [LoggerMessage(
        EventId = 3002,
        Level = LogLevel.Debug,
        Message = "pulsar topic message [{GetName}] has been published."
    )]
    public static partial void MessagePublished(this ILogger logger, string getName);
}
