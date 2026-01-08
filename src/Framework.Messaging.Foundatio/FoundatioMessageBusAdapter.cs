// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Foundatio.Messaging;
using Framework.Abstractions;
using Framework.Domains.Messages;

namespace Framework.Messaging;

public sealed class FoundatioMessageBusAdapter(IFoundatioMessageBus bus, IGuidGenerator guidGenerator) : IMessageBus
{
    public Task SubscribeAsync<T>(
        Func<IMessageSubscribeMedium<T>, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default
    )
        where T : class
    {
        return bus.SubscribeAsync<IMessage<T>>((msg, token) => handler(_MapMessage(msg), token), cancellationToken);
    }

    public Task PublishAsync<T>(
        T message,
        PublishMessageOptions? options = null,
        CancellationToken cancellationToken = default
    )
        where T : class
    {
        return bus.PublishAsync(typeof(T), message, _MapOptions(options), cancellationToken);
    }

    public void Dispose() { }

    #region Helpers

    private static MessageSubscribeMedium<T> _MapMessage<T>(IMessage<T> msg)
        where T : class
    {
        return new()
        {
            MessageKey = MessageName.GetFrom<T>(),
            Type = msg.Type,
            UniqueId = msg.UniqueId,
            CorrelationId = msg.CorrelationId,
            Properties = msg.Properties,
            Payload = msg.Body,
        };
    }

    private MessageOptions _MapOptions(PublishMessageOptions? options)
    {
        return options is null
            ? new MessageOptions { UniqueId = guidGenerator.Create().ToString() }
            : new MessageOptions
            {
                UniqueId = options.UniqueId,
                CorrelationId = options.CorrelationId,
                Properties = options.Properties,
                DeliveryDelay = null,
            };
    }

    #endregion
}
