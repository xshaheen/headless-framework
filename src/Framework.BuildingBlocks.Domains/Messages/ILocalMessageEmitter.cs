// ReSharper disable once CheckNamespace
namespace Framework.BuildingBlocks.Domains;

public interface ILocalMessageEmitter
{
    /// <summary>Events occurred.</summary>
    IReadOnlyList<ILocalMessage> GetLocalMessages();

    /// <summary>Clear events.</summary>
    void ClearLocalMessages();
}
