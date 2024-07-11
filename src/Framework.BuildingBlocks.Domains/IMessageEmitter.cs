namespace Framework.BuildingBlocks.Domains;

public interface IMessageEmitter
{
    /// <summary>Events occurred.</summary>
    IReadOnlyList<IIntegrationMessage> GetMessages();

    /// <summary>Clear events.</summary>
    void ClearMessages();
}
