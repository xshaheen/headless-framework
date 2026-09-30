// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Transport;

namespace Headless.Messaging.RabbitMq;

internal static class RabbitMqPhysicalAddress
{
    private const string _BusQueuePrefix = "bus.";

    // RabbitMQ accepts queue names up to 255 bytes; the framework keeps them to the characters RabbitMqValidation allows.
    private static readonly BusNameRules _BusQueueRules = new(
        maxLength: 255 - _BusQueuePrefix.Length,
        isAllowed: static c => c is '.' or '-' or '_'
    );

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
    public static string Queue(MessageLane lane, string subscriptionName, string logicalName)
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

    private static string _Lane(MessageLane lane) =>
        lane switch
        {
            MessageLane.Bus => "bus",
            MessageLane.Queue => "queue",
            _ => throw new ArgumentOutOfRangeException(nameof(lane), lane, message: null),
        };
}
