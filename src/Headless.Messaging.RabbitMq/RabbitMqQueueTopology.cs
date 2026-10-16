// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace Headless.Messaging.RabbitMq;

/// <summary>
/// Declares the exchanges and the shared (Bus identity and Queue message) queues the transport and its consumers use,
/// so a publisher and a consumer always ask the broker for the same queue with the same arguments.
/// </summary>
/// <remarks>
/// The <c>Ensure*</c> methods create, for <c>resources setup</c> and runtime subscriptions; the <c>Require*</c> methods
/// only prove an object exists and fail with <see cref="StatefulResourceException"/> naming <c>resources setup</c> when it
/// does not. A redeclare whose arguments differ from the broker's queue fails with
/// <c>PRECONDITION_FAILED</c>, which is why every side builds the arguments here.
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

    /// <summary>Declares a lane exchange: what <c>resources setup</c> and a runtime subscription do.</summary>
    public static Task EnsureExchangeAsync(
        IChannel channel,
        string exchange,
        string exchangeType,
        CancellationToken cancellationToken
    )
    {
        return channel.ExchangeDeclareAsync(
            exchange,
            exchangeType,
            durable: true,
            autoDelete: false,
            arguments: null,
            passive: false,
            noWait: false,
            cancellationToken: cancellationToken
        );
    }

    /// <summary>Proves a lane exchange exists, creating nothing.</summary>
    /// <exception cref="StatefulResourceException">The exchange does not exist.</exception>
    public static Task RequireExchangeAsync(IChannel channel, string exchange, CancellationToken cancellationToken)
    {
        return RequireAsync(
            "exchange",
            exchange,
            () => channel.ExchangeDeclarePassiveAsync(exchange, cancellationToken)
        );
    }

    /// <summary>
    /// Declares a shared queue, its dead-letter topology when enabled, and (when <paramref name="routingKey"/> is set)
    /// its binding to <paramref name="laneExchange"/>: what <c>resources setup</c> and a runtime subscription do.
    /// </summary>
    public static async Task EnsureQueueAsync(
        IChannel channel,
        RabbitMqMessagingOptions options,
        string laneExchange,
        string queueName,
        string? routingKey,
        CancellationToken cancellationToken
    )
    {
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
            await BindQueueAsync(channel, laneExchange, queueName, routingKey, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Proves a shared queue exists, creating nothing, and returns its message and consumer counts.</summary>
    /// <exception cref="StatefulResourceException">The queue does not exist.</exception>
    public static Task<QueueDeclareOk> RequireQueueAsync(
        IChannel channel,
        string queueName,
        CancellationToken cancellationToken
    )
    {
        return RequireAsync("queue", queueName, () => channel.QueueDeclarePassiveAsync(queueName, cancellationToken));
    }

    /// <summary>Binds a shared queue to its lane exchange under <paramref name="routingKey"/>.</summary>
    public static Task BindQueueAsync(
        IChannel channel,
        string laneExchange,
        string queueName,
        string routingKey,
        CancellationToken cancellationToken
    )
    {
        return channel.QueueBindAsync(queueName, laneExchange, routingKey, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Runs a passive declare or a call on an existing object of the <paramref name="kind"/> named
    /// <paramref name="name"/>, turning the broker's <c>NOT_FOUND</c> into an error that names the fix. The broker closes
    /// the channel on any refused call, so run it on a channel of its own.
    /// </summary>
    /// <exception cref="StatefulResourceException">The broker reports the object does not exist.</exception>
    public static Task RequireAsync(string kind, string name, Func<Task> call)
    {
        return RequireAsync(
            kind,
            name,
            async () =>
            {
                await call().ConfigureAwait(false);

                return true;
            }
        );
    }

    /// <inheritdoc cref="RequireAsync(string, string, Func{Task})" />
    public static async Task<T> RequireAsync<T>(string kind, string name, Func<Task<T>> call)
    {
        try
        {
            return await call().ConfigureAwait(false);
        }
        catch (OperationInterruptedException ex) when (ex.ShutdownReason?.ReplyCode == Constants.NotFound)
        {
            throw new StatefulResourceException(
                $"RabbitMQ {kind} '{name}' does not exist. Run 'resources setup' (dotnet <app>.dll resources setup) in "
                    + "the deployment, or call AddHeadlessResourceSetupOnStartup() in development.",
                ex
            );
        }
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
