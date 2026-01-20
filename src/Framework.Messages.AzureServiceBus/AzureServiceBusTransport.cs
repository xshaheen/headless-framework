// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Azure.Messaging.ServiceBus;
using Framework.Messages.Helpers;
using Framework.Messages.Internal;
using Framework.Messages.Messages;
using Framework.Messages.Producer;
using Framework.Messages.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Framework.Messages;

internal class AzureServiceBusTransport(
    ILogger<AzureServiceBusTransport> logger,
    IOptions<AzureServiceBusOptions> asbOptions
) : ITransport, IServiceBusProducerDescriptorFactory
{
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

    private readonly ILogger _logger = logger;
    private readonly ConcurrentDictionary<string, ServiceBusSender?> _senders = new(StringComparer.Ordinal);

    private ServiceBusClient? _client;

    /// <summary>
    /// Creates a producer descriptor for the given message. If there's no custom producer configuration for the
    /// message type, one will be created using defaults configured in the AzureServiceBusOptions (e.g. TopicPath).
    /// </summary>
    /// <param name="transportMessage"></param>
    /// <returns></returns>
    public IServiceBusProducerDescriptor CreateProducerForMessage(TransportMessage transportMessage)
    {
        return asbOptions.Value.CustomProducers.SingleOrDefault(p => p.MessageTypeName == transportMessage.GetName())
            ?? new ServiceBusProducerDescriptor(transportMessage.GetName(), asbOptions.Value.TopicPath);
    }

    public BrokerAddress BrokerAddress =>
        ServiceBusHelpers.GetBrokerAddress(asbOptions.Value.ConnectionString, asbOptions.Value.Namespace);

    public async Task<OperateResult> SendAsync(TransportMessage transportMessage)
    {
        try
        {
            var producer = CreateProducerForMessage(transportMessage);
            var sender = _GetSenderForProducer(producer);

            var message = new ServiceBusMessage(transportMessage.Body.ToArray())
            {
                MessageId = transportMessage.GetId(),
                Subject = transportMessage.GetName(),
                CorrelationId = transportMessage.GetCorrelationId(),
            };

            if (asbOptions.Value.EnableSessions)
            {
                transportMessage.Headers.TryGetValue(AzureServiceBusHeaders.SessionId, out var sessionId);
                message.SessionId = string.IsNullOrEmpty(sessionId) ? transportMessage.GetId() : sessionId;
            }

            if (
                transportMessage.Headers.TryGetValue(
                    AzureServiceBusHeaders.ScheduledEnqueueTimeUtc,
                    out var scheduledEnqueueTimeUtcString
                ) && DateTimeOffset.TryParse(scheduledEnqueueTimeUtcString, out var scheduledEnqueueTimeUtc)
            )
            {
                message.ScheduledEnqueueTime = scheduledEnqueueTimeUtc.UtcDateTime;
            }

            foreach (var header in transportMessage.Headers)
            {
                message.ApplicationProperties.Add(header.Key, header.Value);
            }

            await sender.SendMessageAsync(message).AnyContext();

            _logger.LogDebug("Azure Service Bus message [{GetName}] has been published.", transportMessage.GetName());

            return OperateResult.Success;
        }
        catch (Exception ex)
        {
            var wrapperEx = new PublisherSentFailedException(ex.Message, ex);

            return OperateResult.Failed(wrapperEx);
        }
    }

    /// <summary>
    /// Gets the Topic Client for the specified producer. If it does not exist, a new one is created and added to the Topic
    /// Client dictionary.
    /// </summary>
    /// <param name="producerDescriptor"></param>
    /// <returns>
    ///     <see cref="ServiceBusSender" />
    /// </returns>
    private ServiceBusSender _GetSenderForProducer(IServiceBusProducerDescriptor producerDescriptor)
    {
        if (_senders.TryGetValue(producerDescriptor.TopicPath, out var sender) && sender != null)
        {
            _logger.LogTrace(
                "Topic {TopicPath} connection already present as a Publish destination.",
                producerDescriptor.TopicPath
            );

            return sender;
        }

        _connectionLock.Wait();

        try
        {
            _client ??= asbOptions.Value.TokenCredential is null
                ? new ServiceBusClient(asbOptions.Value.ConnectionString)
                : new ServiceBusClient(asbOptions.Value.Namespace, asbOptions.Value.TokenCredential);

            var newSender = _client.CreateSender(producerDescriptor.TopicPath);
            _senders.AddOrUpdate(producerDescriptor.TopicPath, newSender, (_, _) => newSender);

            return newSender;
        }
        finally
        {
            _connectionLock.Release();
        }
    }
}
