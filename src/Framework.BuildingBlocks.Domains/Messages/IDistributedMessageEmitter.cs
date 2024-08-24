// ReSharper disable once CheckNamespace
namespace Framework.BuildingBlocks.Domains;

public interface IDistributedMessageEmitter
{
    void AddMessage(IDistributedMessage e);

    /// <summary>Events occurred.</summary>
    IReadOnlyList<IDistributedMessage> GetDistributedMessages();

    /// <summary>Clear events.</summary>
    void ClearDistributedMessages();
}
