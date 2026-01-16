// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Foundatio.Messaging;
using Framework.Abstractions;
using Framework.Domains.Messages;

namespace Framework.Messaging;

public sealed class FoundatioMessageBusAdapter(IFoundatioMessageBus bus, IGuidGenerator guidGenerator) : IMessageBus
{
    public Task PublishAsync<T>(
        T message,
        PublishMessageOptions? options = null,
        CancellationToken cancellationToken = default
    )
        where T : class
    {
        MessageOptions messageOptions;

        if (options is null)
        {
            messageOptions = new MessageOptions { UniqueId = guidGenerator.Create().ToString() };
        }
        else
        {
            var uniqueId = options.UniqueId.ToString();

            messageOptions = new MessageOptions
            {
                UniqueId = uniqueId,
                CorrelationId = options.CorrelationId?.ToString() ?? uniqueId,
                Properties = options.Headers,
                DeliveryDelay = null,
            };
        }

        return bus.PublishAsync(typeof(T), message, messageOptions, cancellationToken);
    }

    public Task SubscribeAsync<T>(
        Func<IMessageSubscribeMedium<T>, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default
    )
        where T : class
    {
        return bus.SubscribeAsync(
            (Func<IMessage<T>, CancellationToken, Task>)(
                (message, token) =>
                {
                    var uniqueId = Guid.Parse(message.UniqueId);
                    _ = Guid.TryParse(message.CorrelationId, out var correlationId);

                    var medium = new MessageSubscribeMedium<T>
                    {
                        MessageKey = MessageName.GetFrom<T>(),
                        Type = message.Type,
                        UniqueId = uniqueId,
                        CorrelationId = correlationId,
                        Properties = message.Properties,
                        Payload = message.Body,
                    };

                    return handler(medium, token);
                }
            ),
            cancellationToken
        );
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
