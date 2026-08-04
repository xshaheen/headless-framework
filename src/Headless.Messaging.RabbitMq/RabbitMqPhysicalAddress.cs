// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.RabbitMq;

internal static class RabbitMqPhysicalAddress
{
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

    public static string Queue(MessageLane lane, string subscriberGroup, string logicalName)
    {
        var queue = lane switch
        {
            MessageLane.Bus => $"bus.{subscriberGroup}",
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
