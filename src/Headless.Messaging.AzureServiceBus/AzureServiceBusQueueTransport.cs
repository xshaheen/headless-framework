// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.AzureServiceBus.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.AzureServiceBus;

internal sealed class AzureServiceBusQueueTransport(
    ILogger<AzureServiceBusQueueTransport> logger,
    IOptions<AzureServiceBusMessagingOptions> busOptions,
    IAzureServiceBusClientPool clientPool
) : IQueueTransport
{
    public BrokerAddress BrokerAddress =>
        ServiceBusHelpers.GetBrokerAddress(busOptions.Value.ConnectionString, busOptions.Value.Namespace);

    public async Task<OperateResult> SendAsync(
        TransportMessage transportMessage,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var queueName = transportMessage.Name;
            var sender = clientPool.GetSender(queueName);
            var message = AzureServiceBusMessageBuilder.Build(transportMessage, busOptions.Value.EnableSessions);

            await sender.SendMessageAsync(message, cancellationToken).ConfigureAwait(false);

            logger.QueueMessageEnqueued(queueName);

            return OperateResult.Success;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return OperateResult.Failed(new PublisherSentFailedException(ex.Message, ex));
        }
    }

    // The shared client/sender pool owns connection lifetime and is disposed by the container.
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal static partial class AzureServiceBusQueueTransportLog
{
    [LoggerMessage(
        EventId = 3020,
        Level = LogLevel.Information,
        Message = "Azure Service Bus queue message enqueued to {QueueName}."
    )]
    public static partial void QueueMessageEnqueued(this ILogger logger, string queueName);
}
