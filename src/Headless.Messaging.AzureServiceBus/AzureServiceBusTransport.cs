// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.AzureServiceBus.Helpers;
using Headless.Messaging.AzureServiceBus.Producer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.AzureServiceBus;

internal sealed class AzureServiceBusTransport(
    ILogger<AzureServiceBusTransport> logger,
    IOptions<AzureServiceBusMessagingOptions> busOptions,
    IAzureServiceBusClientPool clientPool
) : IBusTransport, IServiceBusProducerDescriptorFactory
{
    private readonly ILogger _logger = logger;

    /// <summary>
    /// Creates a producer descriptor for the given message. If there's no custom producer configuration for the
    /// message type, one will be created using defaults configured in the AzureServiceBusMessagingOptions (e.g. TopicPath).
    /// </summary>
    /// <param name="transportMessage"></param>
    /// <returns></returns>
    public IServiceBusProducerDescriptor CreateProducerForMessage(TransportMessage transportMessage)
    {
        return busOptions.Value.CustomProducers.SingleOrDefault(p =>
                string.Equals(p.MessageTypeName, transportMessage.Name, StringComparison.Ordinal)
            ) ?? new ServiceBusProducerDescriptor(transportMessage.Name, busOptions.Value.TopicPath);
    }

    public BrokerAddress BrokerAddress =>
        ServiceBusHelpers.GetBrokerAddress(busOptions.Value.ConnectionString, busOptions.Value.Namespace);

    public async Task<OperateResult> SendAsync(
        TransportMessage transportMessage,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var producer = CreateProducerForMessage(transportMessage);
            var sender = clientPool.GetSender(producer.TopicPath);

            var message = AzureServiceBusMessageBuilder.Build(
                transportMessage,
                busOptions.Value.EnableSessions || producer.EnableSessions
            );

            await sender.SendMessageAsync(message, cancellationToken).ConfigureAwait(false);

            var messageName = transportMessage.Name;
            _logger.MessagePublished(messageName);

            return OperateResult.Success;
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

    // The shared client/sender pool owns connection lifetime and is disposed by the container.
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal static partial class AzureServiceBusTransportLog
{
    [LoggerMessage(
        EventId = 3006,
        Level = LogLevel.Debug,
        Message = "Azure Service Bus message [{GetName}] has been published."
    )]
    public static partial void MessagePublished(this ILogger logger, string getName);
}
