using Framework.Kernel.Domains;

namespace Framework.Messaging;

public interface IDistributedMessagePublisher
{
    void Publish<T>(T message)
        where T : class, IDistributedMessage;

    Task PublishAsync<T>(T message, CancellationToken abortToken = default)
        where T : class, IDistributedMessage;
}
