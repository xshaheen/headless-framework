// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Azure.Messaging.ServiceBus;
using Headless.Messaging.AzureServiceBus.Helpers;
using Headless.Messaging.AzureServiceBus.Producer;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.AzureServiceBus;

internal class AzureServiceBusTransport(
    ILogger<AzureServiceBusTransport> logger,
    IOptions<AzureServiceBusOptions> busOptions
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
        return busOptions.Value.CustomProducers.SingleOrDefault(p => p.MessageTypeName == transportMessage.GetName())
            ?? new ServiceBusProducerDescriptor(transportMessage.GetName(), busOptions.Value.TopicPath);
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
            var sender = _GetSenderForProducer(producer);

            var message = new ServiceBusMessage(transportMessage.Body.ToArray())
            {
                MessageId = transportMessage.GetId(),
                Subject = transportMessage.GetName(),
                CorrelationId = transportMessage.GetCorrelationId(),
            };

            if (busOptions.Value.EnableSessions)
            {
                transportMessage.Headers.TryGetValue(AzureServiceBusHeaders.SessionId, out var sessionId);
                message.SessionId = string.IsNullOrEmpty(sessionId) ? transportMessage.GetId() : sessionId;
            }

            if (
                transportMessage.Headers.TryGetValue(
                    AzureServiceBusHeaders.ScheduledEnqueueTimeUtc,
                    out var scheduledEnqueueTimeUtcString
                )
                && DateTimeOffset.TryParse(
                    scheduledEnqueueTimeUtcString,
                    CultureInfo.InvariantCulture,
                    out var scheduledEnqueueTimeUtc
                )
            )
            {
                message.ScheduledEnqueueTime = scheduledEnqueueTimeUtc;
            }

            foreach (var header in transportMessage.Headers)
            {
                message.ApplicationProperties.Add(header.Key, header.Value);
            }

            await sender.SendMessageAsync(message, cancellationToken).ConfigureAwait(false);

            _logger.LogDebug("Azure Service Bus message [{GetName}] has been published.", transportMessage.GetName());

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
            _client ??= busOptions.Value.TokenCredential is null
                ? new ServiceBusClient(busOptions.Value.ConnectionString)
                : new ServiceBusClient(busOptions.Value.Namespace, busOptions.Value.TokenCredential);

            var newSender = _client.CreateSender(producerDescriptor.TopicPath);
            _senders.AddOrUpdate(producerDescriptor.TopicPath, newSender, (_, _) => newSender);

            return newSender;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _connectionLock.Dispose();

        if (_client is not null)
        {
            await _client.DisposeAsync();
        }
    }
}
