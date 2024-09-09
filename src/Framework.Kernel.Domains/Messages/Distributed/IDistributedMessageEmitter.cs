// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Domains;

public interface IDistributedMessageEmitter
{
    void AddMessage(IDistributedMessage message);

    void ClearDistributedMessages();

    IReadOnlyList<IDistributedMessage> GetDistributedMessages();
}
