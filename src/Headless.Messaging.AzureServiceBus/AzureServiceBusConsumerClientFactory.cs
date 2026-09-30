// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Azure.Messaging.ServiceBus;
using Headless.Checks;
using Headless.Messaging.Exceptions;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.AzureServiceBus;

internal sealed class AzureServiceBusConsumerClientFactory(
    ILoggerFactory loggerFactory,
    IOptions<AzureServiceBusMessagingOptions> asbOptions,
    IServiceProvider serviceProvider,
    IAzureServiceBusClientPool clientPool
) : IConsumerClientFactory, IAsyncDisposable
{
    // Bounds the subscription cleanup on shutdown; whatever it cannot delete in time, Azure deletes once idle.
    private static readonly TimeSpan _CleanupTimeout = TimeSpan.FromSeconds(30);

    // Azure Resource Manager naming rules for Service Bus subscriptions: 1-50 letters, digits, '.', '-', or '_',
    // starting and ending with a letter or digit.
    private static readonly BusNameRules _SubscriptionRules = new(
        maxLength: 50,
        isAllowed: static c => c is '.' or '-' or '_',
        alphanumericBoundaries: true
    );

    private readonly ILogger _logger = loggerFactory.CreateLogger<AzureServiceBusConsumerClientFactory>();
    private readonly ConcurrentDictionary<string, byte> _everyInstanceSubscriptions = new(StringComparer.Ordinal);
    private int _disposed;

    /// <summary>Returns the topic subscription a Bus consumer identity reads through.</summary>
    internal static string BusSubscriptionName(string identity) => BusNameBuilder.Build(identity, _SubscriptionRules);

    /// <summary>
    /// Returns the topic subscription a Bus client of <paramref name="request"/> reads through: the identity's shared
    /// subscription, or one named from the identity and the process's instance id for an every-instance request.
    /// </summary>
    internal static string BusSubscriptionName(ConsumerClientRequest request) =>
        BusNameBuilder.Build(request, _SubscriptionRules);

    public async Task<IConsumerClient> CreateAsync(
        ConsumerClientRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);

        var everyInstance = request.Kind is ConsumerSubscriptionKind.EveryInstance;

        // The capability declaration already refuses this at startup; the factory repeats it with the reason, for a
        // caller that creates clients without the messaging core.
        if (everyInstance && !asbOptions.Value.AutoProvision)
        {
            throw new InvalidOperationException(
                $"Every-instance consumer '{request.SubscriptionName}' needs AutoProvision on the Azure Service Bus "
                    + "transport, because each process creates and deletes a subscription of its own. Enable "
                    + "AutoProvision with Manage rights on the namespace, or remove EveryInstance from the consumer."
            );
        }

        // A Bus consumer identity becomes an Azure subscription name. Queue subscription names are framework-local
        // handler selectors; their broker entity names are validated on SubscribeAsync.
        var subscriptionName = request.SubscriptionName;
        if (request.Lane == MessageLane.Bus)
        {
            subscriptionName = BusSubscriptionName(request);
            AzureServiceBusConsumerClient.CheckValidSubscriptionName(subscriptionName);
        }

        if (everyInstance)
        {
            // Deleted when the host disposes this factory, not when a client is disposed: the core disposes clients on
            // every rebuild too, and a rebuilt client resumes the same subscription with what it held meanwhile.
            _everyInstanceSubscriptions.TryAdd(subscriptionName, 0);
        }

        AzureServiceBusConsumerClient? client = null;

        try
        {
            client = new AzureServiceBusConsumerClient(
                loggerFactory.CreateLogger<AzureServiceBusConsumerClient>(),
                subscriptionName,
                request.Concurrency,
                asbOptions,
                serviceProvider,
                clientPool,
                request.Lane,
                request.Kind
            );

            await client.ConnectAsync(cancellationToken).ConfigureAwait(false);

            return client;
        }
        catch (OperationCanceledException)
        {
            if (client is not null)
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }

            throw;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            if (client is not null)
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }

            throw new BrokerConnectionException(e);
        }
    }

    /// <summary>
    /// Deletes the every-instance subscriptions this process created. The host disposes the factory once, after the
    /// messaging core stopped every consumer client, so only a graceful stop deletes them.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0 || _everyInstanceSubscriptions.IsEmpty)
        {
            return;
        }

        var topicPath = asbOptions.Value.TopicPath;
        using var timeout = new CancellationTokenSource(_CleanupTimeout);

        foreach (var subscriptionName in _everyInstanceSubscriptions.Keys)
        {
            try
            {
                await clientPool
                    .GetAdministrationClient()
                    .DeleteSubscriptionAsync(topicPath, subscriptionName, timeout.Token)
                    .ConfigureAwait(false);

                _logger.EveryInstanceSubscriptionDeleted(topicPath, subscriptionName);
            }
            catch (ServiceBusException e) when (e.Reason is ServiceBusFailureReason.MessagingEntityNotFound)
            {
                // Never created, as for the topology-only client, or already deleted by Azure after its idle period.
            }
            catch (Exception e)
            {
                _logger.EveryInstanceSubscriptionNotDeleted(
                    e,
                    topicPath,
                    subscriptionName,
                    AzureServiceBusConsumerClient.EveryInstanceAutoDeleteOnIdle
                );
            }
        }

        _everyInstanceSubscriptions.Clear();
    }
}

internal static partial class AzureServiceBusConsumerClientFactoryLog
{
    [LoggerMessage(
        EventId = 3013,
        Level = LogLevel.Information,
        Message = "Azure Service Bus topic {TopicPath} deleted every-instance subscription: {SubscriptionName}"
    )]
    public static partial void EveryInstanceSubscriptionDeleted(
        this ILogger logger,
        string topicPath,
        string subscriptionName
    );

    [LoggerMessage(
        EventId = 3014,
        Level = LogLevel.Warning,
        Message = "Azure Service Bus could not delete every-instance subscription {SubscriptionName} on topic {TopicPath}; Azure deletes it after {AutoDeleteOnIdle} without a receiver"
    )]
    public static partial void EveryInstanceSubscriptionNotDeleted(
        this ILogger logger,
        Exception exception,
        string topicPath,
        string subscriptionName,
        TimeSpan autoDeleteOnIdle
    );
}
