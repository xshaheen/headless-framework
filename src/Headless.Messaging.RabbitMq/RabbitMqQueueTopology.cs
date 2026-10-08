// Copyright (c) Mahmoud Shaheen. All rights reserved.

using RabbitMQ.Client;

namespace Headless.Messaging.RabbitMq;

/// <summary>
/// Declares the exchanges and the shared (Bus identity and Queue message) queues the transport and its consumers use,
/// so a publisher and a consumer always ask the broker for the same queue with the same arguments.
/// </summary>
/// <remarks>
/// With <see cref="RabbitMqMessagingOptions.AutoProvision"/> off every declare is passive: it only proves the exchange
/// or queue exists, and binding stays with whoever manages the topology. A redeclare whose arguments differ from the
/// broker's queue fails with <c>PRECONDITION_FAILED</c>, which is why both sides build the arguments here.
/// </remarks>
internal static class RabbitMqQueueTopology
{
    internal const string DeadLetterExchangeSuffix = ".dlx";
    internal const string DeadLetterQueueSuffix = ".dlq";

    /// <summary>Returns the dead-letter exchange the queues behind <paramref name="laneExchange"/> dead-letter to.</summary>
    public static string DeadLetterExchange(string laneExchange)
    {
        var exchange = laneExchange + DeadLetterExchangeSuffix;
        RabbitMqValidation.ValidateExchangeName(exchange);
        return exchange;
    }

    /// <summary>Returns the queue that keeps what <paramref name="queueName"/> dead-letters.</summary>
    public static string DeadLetterQueue(string queueName)
    {
        var queue = queueName + DeadLetterQueueSuffix;
        RabbitMqValidation.ValidateQueueName(queue);
        return queue;
    }

    /// <summary>The <c>x-arguments</c> of a shared queue.</summary>
    public static Dictionary<string, object?> BuildQueueArguments(
        RabbitMqMessagingOptions options,
        string laneExchange,
        string queueName
    )
    {
        var queueArguments = options.QueueArguments;
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            { "x-message-ttl", queueArguments.MessageTTL },
        };

        if (!string.IsNullOrEmpty(queueArguments.QueueMode))
        {
            arguments.Add("x-queue-mode", queueArguments.QueueMode);
        }

        if (!string.IsNullOrEmpty(queueArguments.QueueType))
        {
            arguments.Add("x-queue-type", queueArguments.QueueType);
        }

        if (queueArguments.DeliveryLimit is { } deliveryLimit)
        {
            arguments.Add("x-delivery-limit", deliveryLimit);
        }

        if (queueArguments.EnableDeadLettering)
        {
            // Every queue behind a lane shares one dead-letter exchange, so the routing key names the queue: the original
            // routing key would send a Bus message to the dead-letter queue of every identity bound to it.
            arguments.Add("x-dead-letter-exchange", DeadLetterExchange(laneExchange));
            arguments.Add("x-dead-letter-routing-key", queueName);
        }

        return arguments;
    }

    /// <summary>Declares a lane exchange, or proves it exists when auto-provisioning is off.</summary>
    public static Task DeclareExchangeAsync(
        IChannel channel,
        RabbitMqMessagingOptions options,
        string exchange,
        string exchangeType,
        CancellationToken cancellationToken
    )
    {
        return options.AutoProvision
            ? channel.ExchangeDeclareAsync(
                exchange,
                exchangeType,
                durable: true,
                autoDelete: false,
                arguments: null,
                passive: false,
                noWait: false,
                cancellationToken: cancellationToken
            )
            : channel.ExchangeDeclarePassiveAsync(exchange, cancellationToken);
    }

    /// <summary>
    /// Declares a shared queue, its dead-letter topology when enabled, and (when <paramref name="routingKey"/> is set)
    /// its binding to <paramref name="laneExchange"/>; with auto-provisioning off it only proves the queue exists.
    /// </summary>
    public static async Task DeclareQueueAsync(
        IChannel channel,
        RabbitMqMessagingOptions options,
        string laneExchange,
        string queueName,
        string? routingKey,
        CancellationToken cancellationToken
    )
    {
        if (!options.AutoProvision)
        {
            await channel.QueueDeclarePassiveAsync(queueName, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (options.QueueArguments.EnableDeadLettering)
        {
            await _DeclareDeadLetterTopologyAsync(channel, options, laneExchange, queueName, cancellationToken)
                .ConfigureAwait(false);
        }

        await channel
            .QueueDeclareAsync(
                queueName,
                options.QueueOptions.Durable,
                options.QueueOptions.Exclusive,
                options.QueueOptions.AutoDelete,
                BuildQueueArguments(options, laneExchange, queueName),
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        if (routingKey is not null)
        {
            await channel
                .QueueBindAsync(queueName, laneExchange, routingKey, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Binds a shared queue to its lane exchange; with auto-provisioning off the binding is not this app's.</summary>
    public static Task BindQueueAsync(
        IChannel channel,
        RabbitMqMessagingOptions options,
        string laneExchange,
        string queueName,
        string routingKey,
        CancellationToken cancellationToken
    )
    {
        return options.AutoProvision
            ? channel.QueueBindAsync(queueName, laneExchange, routingKey, cancellationToken: cancellationToken)
            : Task.CompletedTask;
    }

    // Declared before the queue, so nothing the queue dead-letters can reach an exchange with no queue behind it.
    private static async Task _DeclareDeadLetterTopologyAsync(
        IChannel channel,
        RabbitMqMessagingOptions options,
        string laneExchange,
        string queueName,
        CancellationToken cancellationToken
    )
    {
        var deadLetterExchange = DeadLetterExchange(laneExchange);
        var deadLetterQueue = DeadLetterQueue(queueName);

        await channel
            .ExchangeDeclareAsync(
                deadLetterExchange,
                ExchangeType.Direct,
                durable: true,
                autoDelete: false,
                arguments: null,
                passive: false,
                noWait: false,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        // The dead-letter queue keeps what it holds: no TTL, no delivery limit, and no dead-lettering of its own. It
        // keeps the queue type, so a replicated quorum queue does not dead-letter into a single-node classic one.
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(options.QueueArguments.QueueType))
        {
            arguments.Add("x-queue-type", options.QueueArguments.QueueType);
        }

        await channel
            .QueueDeclareAsync(
                deadLetterQueue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        await channel
            .QueueBindAsync(deadLetterQueue, deadLetterExchange, queueName, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }
}
