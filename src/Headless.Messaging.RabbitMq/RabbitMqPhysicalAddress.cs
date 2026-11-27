// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Transport;

namespace Headless.Messaging.RabbitMq;

/// <summary>The names the RabbitMQ transport derives: exchanges, routing keys, and queues.</summary>
/// <remarks>
/// The base exchange and every queue take the host's isolation prefix (<c>Headless:Prefix</c>) in front, joined with
/// <c>.</c>, which a prefix never contains, so environments that share one virtual host never share an exchange or a
/// queue. Lane and dead-letter exchanges derive from the base exchange. Dead-letter queues have their own namespace
/// and include the live queue's name. Routing keys are scoped by their exchange and stay as they are.
/// </remarks>
internal static class RabbitMqPhysicalAddress
{
    private const string _BusQueuePrefix = "bus.";
    private const string _DeadLetterQueuePrefix = "headless_dlq.";

    // RabbitMQ accepts queue and exchange names up to 255 bytes.
    private const int _MaxNameLength = 255;

    // The longest suffix a name derived from the base exchange adds: the Queue lane's dead-letter exchange.
    private const int _LongestExchangeSuffix = 6 + 4; // ".queue" + ".dlx"

    // The framework keeps queue names to the characters RabbitMqValidation allows.
    private static readonly BusNameRules _BusQueueRules = _BusRules(hostPrefixLength: 0);

    /// <summary>
    /// Returns the base exchange this host publishes and binds through: the configured one, with the host prefix in
    /// front when one is set.
    /// </summary>
    /// <exception cref="InvalidOperationException">The prefix makes an exchange derived from it longer than RabbitMQ allows.</exception>
    public static string BaseExchange(string hostPrefix, string configuredExchange)
    {
        if (hostPrefix.Length == 0)
        {
            return configuredExchange;
        }

        var exchange = $"{hostPrefix}.{configuredExchange}";
        if (exchange.Length + _LongestExchangeSuffix > _MaxNameLength)
        {
            throw new InvalidOperationException(
                $"The RabbitMQ exchange '{configuredExchange}' with the host prefix '{hostPrefix}' (Headless:Prefix) "
                    + $"derives exchange names longer than {_MaxNameLength} characters. Shorten ExchangeName or the prefix."
            );
        }

        return exchange;
    }

    public static string Exchange(string baseExchange, MessageLane lane)
    {
        var exchange = $"{baseExchange}.{_Lane(lane)}";
        RabbitMqValidation.ValidateExchangeName(exchange);
        return exchange;
    }

    public static string ExchangeType(MessageLane lane) =>
        lane switch
        {
            MessageLane.Bus => RabbitMqMessagingOptions.ExchangeType,
            MessageLane.Queue => RabbitMQ.Client.ExchangeType.Direct,
            _ => throw new ArgumentOutOfRangeException(nameof(lane), lane, message: null),
        };

    public static string RoutingKey(MessageLane lane, string logicalName)
    {
        var routingKey = $"{_Lane(lane)}.{logicalName}";
        RabbitMqValidation.ValidateMessageName(routingKey);
        return routingKey;
    }

    /// <summary>
    /// Returns the queue a consumer reads: one per consumer identity on the Bus lane, and one per message name on the
    /// Queue lane.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The host prefix makes a Queue-lane queue longer than RabbitMQ allows.
    /// </exception>
    public static string Queue(string hostPrefix, MessageLane lane, string subscriptionName, string logicalName)
    {
        if (hostPrefix.Length == 0)
        {
            var queue = lane switch
            {
                MessageLane.Bus => _BusQueuePrefix + BusNameBuilder.Build(subscriptionName, _BusQueueRules),
                MessageLane.Queue => $"queue.{logicalName}",
                _ => throw new ArgumentOutOfRangeException(nameof(lane), lane, message: null),
            };
            RabbitMqValidation.ValidateQueueName(queue);
            return queue;
        }

        // A Bus queue is named by the identity's hash past the length limit, so it always fits; a Queue-lane queue is
        // the message name publishers and consumers agree on, so one the prefix pushes past the limit fails startup
        // rather than being renamed into something another environment could share.
        var prefixed = lane switch
        {
            MessageLane.Bus => $"{hostPrefix}.{_BusQueuePrefix}"
                + BusNameBuilder.Build(subscriptionName, _BusRules(hostPrefix.Length + 1)),
            MessageLane.Queue => $"{hostPrefix}.queue.{logicalName}",
            _ => throw new ArgumentOutOfRangeException(nameof(lane), lane, message: null),
        };

        if (lane is MessageLane.Queue && prefixed.Length > _MaxNameLength)
        {
            throw new InvalidOperationException(
                $"The RabbitMQ queue for message '{logicalName}' with the host prefix '{hostPrefix}' (Headless:Prefix) "
                    + $"is longer than {_MaxNameLength} characters. Shorten the message name or the prefix."
            );
        }

        RabbitMqValidation.ValidateQueueName(prefixed);
        return prefixed;
    }

    // No live queue starts with this segment: its first segment is a lane or a host prefix, which cannot contain '_'.
    // A full live name always contains a dot, so shortened dead-letter names cannot collide with the readable form.
    public static string DeadLetterQueue(string queueName) =>
        _DeadLetterQueuePrefix
        + (queueName.Length <= _MaxNameLength - _DeadLetterQueuePrefix.Length ? queueName : queueName.ToSha256());

    private static BusNameRules _BusRules(int hostPrefixLength) =>
        new(
            maxLength: _MaxNameLength - _BusQueuePrefix.Length - hostPrefixLength,
            isAllowed: static c => c is '.' or '-' or '_'
        );

    private static string _Lane(MessageLane lane) =>
        lane switch
        {
            MessageLane.Bus => "bus",
            MessageLane.Queue => "queue",
            _ => throw new ArgumentOutOfRangeException(nameof(lane), lane, message: null),
        };
}
