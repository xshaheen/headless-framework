using DotNetCore.CAP;
using Framework.BuildingBlocks.Domains;

namespace Framework.Messaging.Cap;

public sealed class CapMessagePublisher(ICapPublisher publisher) : IMessagePublisher
{
    public void Publish(IIntegrationMessage emittedMessage)
    {
        publisher.Publish(name: emittedMessage.MessageKey, contentObj: emittedMessage, callbackName: null);
    }

    public async Task PublishAsync(IIntegrationMessage message, CancellationToken cancellationToken = default)
    {
        await publisher.PublishAsync(
            name: message.MessageKey,
            contentObj: message,
            callbackName: null,
            cancellationToken
        );
    }
}
