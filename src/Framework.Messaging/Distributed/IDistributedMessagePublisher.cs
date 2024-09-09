using Framework.Kernel.Domains;

// ReSharper disable once CheckNamespace
namespace Framework.Messaging;

public interface IDistributedMessagePublisher
{
    void Publish<T>(T message)
        where T : class, IDistributedMessage;

    Task PublishAsync<T>(T message, CancellationToken cancellationToken = default)
        where T : class, IDistributedMessage;
}
