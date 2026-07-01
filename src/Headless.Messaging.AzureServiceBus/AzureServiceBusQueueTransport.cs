// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Azure.Messaging.ServiceBus;
using Headless.Messaging.AzureServiceBus.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.AzureServiceBus;

internal sealed class AzureServiceBusQueueTransport(
    ILogger<AzureServiceBusQueueTransport> logger,
    IOptions<AzureServiceBusOptions> busOptions
) : IQueueTransport
{
    private readonly ConcurrentDictionary<string, Lazy<ServiceBusSender>> _senders = new(StringComparer.Ordinal);
    private ServiceBusClient? _client;

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
            var sender = _GetSender(queueName).Value;
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

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync().ConfigureAwait(false);
        }
    }

    private Lazy<ServiceBusSender> _GetSender(string queueName)
    {
        return _senders.GetOrAdd(
            queueName,
            static (name, transport) =>
            {
                var factory = new SenderFactory(transport, name);

                return new Lazy<ServiceBusSender>(factory.Create, LazyThreadSafetyMode.ExecutionAndPublication);
            },
            this
        );
    }

    private ServiceBusSender _CreateSender(string queueName)
    {
        _client ??= busOptions.Value.TokenCredential is not null
            ? new ServiceBusClient(busOptions.Value.Namespace, busOptions.Value.TokenCredential)
            : new ServiceBusClient(busOptions.Value.ConnectionString);

        return _client.CreateSender(queueName);
    }

    private sealed class SenderFactory(AzureServiceBusQueueTransport transport, string queueName)
    {
        public ServiceBusSender Create() => transport._CreateSender(queueName);
    }
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
