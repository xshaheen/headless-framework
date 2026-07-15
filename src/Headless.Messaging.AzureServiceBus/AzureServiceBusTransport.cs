// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Azure.Messaging.ServiceBus;
using Headless.Messaging.AzureServiceBus.Helpers;
using Headless.Messaging.AzureServiceBus.Producer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.AzureServiceBus;

internal sealed class AzureServiceBusTransport(
    ILogger<AzureServiceBusTransport> logger,
    IOptions<AzureServiceBusMessagingOptions> busOptions
) : IBusTransport, IServiceBusProducerDescriptorFactory
{
    private readonly ILogger _logger = logger;
    private readonly ConcurrentDictionary<string, Lazy<ServiceBusSender>> _senders = new(StringComparer.Ordinal);
    private ServiceBusClient? _client;

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
            var sender = _GetSenderForProducer(producer).Value;

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

    /// <summary>
    /// Gets the <see cref="ServiceBusSender"/> for the specified producer descriptor, creating it on first access.
    /// Thread-safe via <see cref="ConcurrentDictionary{TKey,TValue}"/> and <see cref="Lazy{T}"/> double-init protection.
    /// </summary>
    private Lazy<ServiceBusSender> _GetSenderForProducer(IServiceBusProducerDescriptor producerDescriptor)
    {
        return _senders.GetOrAdd(
            producerDescriptor.TopicPath,
            static (topicPath, transport) =>
            {
                var factory = new SenderFactory(transport, topicPath);

                return new Lazy<ServiceBusSender>(factory.Create, LazyThreadSafetyMode.ExecutionAndPublication);
            },
            this
        );
    }

    private ServiceBusSender _CreateSender(string topicPath)
    {
        _logger.TopicConnectionExists(topicPath);
        _client ??= busOptions.Value.TokenCredential is null
            ? new ServiceBusClient(busOptions.Value.ConnectionString)
            : new ServiceBusClient(busOptions.Value.Namespace, busOptions.Value.TokenCredential);

        return _client.CreateSender(topicPath);
    }

    private sealed class SenderFactory(AzureServiceBusTransport transport, string topicPath)
    {
        public ServiceBusSender Create()
        {
            return transport._CreateSender(topicPath);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync().ConfigureAwait(false);
        }
    }
}

internal static partial class AzureServiceBusTransportLog
{
    [LoggerMessage(
        EventId = 3006,
        Level = LogLevel.Debug,
        Message = "Azure Service Bus message [{GetName}] has been published."
    )]
    public static partial void MessagePublished(this ILogger logger, string getName);

    [LoggerMessage(
        EventId = 3007,
        Level = LogLevel.Trace,
        Message = "Topic {TopicPath} connection already present as a Publish destination."
    )]
    public static partial void TopicConnectionExists(this ILogger logger, string topicPath);
}
