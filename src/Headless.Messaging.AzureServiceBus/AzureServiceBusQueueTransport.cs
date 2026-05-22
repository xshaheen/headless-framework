// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Azure.Messaging.ServiceBus;
using Headless.Messaging.AzureServiceBus.Helpers;
using Headless.Messaging.Messages;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.AzureServiceBus;

internal sealed class AzureServiceBusQueueTransport(
    ILogger<AzureServiceBusQueueTransport> logger,
    IOptions<AzureServiceBusOptions> busOptions
) : IQueueTransport
{
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly ConcurrentDictionary<string, ServiceBusSender?> _senders = new(StringComparer.Ordinal);
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
            var queueName = transportMessage.GetName();
            var sender = _GetSender(queueName);
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
        _connectionLock.Dispose();

        if (_client is not null)
        {
            await _client.DisposeAsync().ConfigureAwait(false);
        }
    }

    private ServiceBusSender _GetSender(string queueName)
    {
        if (_senders.TryGetValue(queueName, out var sender) && sender is not null)
        {
            return sender;
        }

        _connectionLock.Wait();

        try
        {
            if (_senders.TryGetValue(queueName, out sender) && sender is not null)
            {
                return sender;
            }

            _client ??= busOptions.Value.TokenCredential is not null
                ? new ServiceBusClient(busOptions.Value.Namespace, busOptions.Value.TokenCredential)
                : new ServiceBusClient(busOptions.Value.ConnectionString);

            var newSender = _client.CreateSender(queueName);
            _senders.AddOrUpdate(queueName, newSender, (_, _) => newSender);

            return newSender;
        }
        finally
        {
            _connectionLock.Release();
        }
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
