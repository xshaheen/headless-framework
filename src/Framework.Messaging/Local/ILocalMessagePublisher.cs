using Framework.Kernel.Domains;

// ReSharper disable once CheckNamespace
namespace Framework.Messaging;

public interface ILocalMessagePublisher
{
    void Publish<T>(T message)
        where T : class, ILocalMessage;

    Task PublishAsync<T>(T message, CancellationToken abortToken = default)
        where T : class, ILocalMessage;
}
