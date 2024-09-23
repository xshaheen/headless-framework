using Framework.Kernel.Domains;

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Messaging;

public interface ILocalMessagePublisher
{
    void Publish<T>(T message)
        where T : class, ILocalMessage;

    Task PublishAsync<T>(T message, CancellationToken abortToken = default)
        where T : class, ILocalMessage;
}
