using DotNetCore.CAP;
using Framework.Kernel.Domains;

namespace Framework.Messaging;

public sealed class CapDistributedMessagePublisher(ICapPublisher publisher) : IDistributedMessagePublisher
{
    public void Publish<T>(T message)
        where T : class, IDistributedMessage
    {
        publisher.Publish(name: message.TypeKey, contentObj: message, callbackName: null);
    }

    public Task PublishAsync<T>(T message, CancellationToken cancellationToken = default)
        where T : class, IDistributedMessage
    {
        return publisher.PublishAsync(
            name: message.TypeKey,
            contentObj: message,
            callbackName: null,
            cancellationToken
        );
    }
}
