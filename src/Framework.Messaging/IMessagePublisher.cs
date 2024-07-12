using Framework.BuildingBlocks.Domains;

namespace Framework.Messaging;

public interface IMessagePublisher
{
    void Publish(IIntegrationMessage emittedMessage);

    Task PublishAsync(IIntegrationMessage message, CancellationToken cancellationToken = default);
}
