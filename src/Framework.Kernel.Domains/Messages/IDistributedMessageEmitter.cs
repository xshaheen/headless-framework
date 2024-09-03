// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Domains;

public interface IDistributedMessageEmitter
{
    void AddMessage(IDistributedMessage e);

    /// <summary>Events occurred.</summary>
    IReadOnlyList<IDistributedMessage> GetDistributedMessages();

    /// <summary>Clear events.</summary>
    void ClearDistributedMessages();
}
