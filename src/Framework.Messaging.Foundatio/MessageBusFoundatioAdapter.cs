// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Foundatio.Messaging;
using IFoundatioMessageBus = Foundatio.Messaging.IMessageBus;
using IFrameworkMessageBus = Framework.Messaging.IMessageBus;

namespace Framework.Messaging;

public sealed class MessageBusFoundatioAdapter(IFoundatioMessageBus foundatio) : IFrameworkMessageBus
{
    public Task SubscribeAsync<TPayload>(
        Func<IMessageSubscribeMedium<TPayload>, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default
    )
        where TPayload : class
    {
        return foundatio.SubscribeAsync(handler, cancellationToken);
    }

    public Task PublishAsync<T>(
        T message,
        PublishMessageOptions? options = null,
        CancellationToken cancellationToken = default
    )
        where T : class
    {
        var foundatioOptions = _MapToFoundatioOptions(options);

        return foundatio.PublishAsync(typeof(T), message, foundatioOptions, cancellationToken);
    }

    public void Dispose()
    {
        foundatio.Dispose();
    }

    private static MessageOptions? _MapToFoundatioOptions(PublishMessageOptions? options)
    {
        return options is null
            ? null
            : new MessageOptions
            {
                UniqueId = options.UniqueId,
                CorrelationId = options.CorrelationId,
                Properties = options.Properties,
                DeliveryDelay = null,
            };
    }
}
