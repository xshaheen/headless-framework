// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Domains;

public interface ILocalMessageEmitter
{
    void AddMessage(ILocalMessage e);

    /// <summary>Events occurred.</summary>
    IReadOnlyList<ILocalMessage> GetLocalMessages();

    /// <summary>Clear events.</summary>
    void ClearLocalMessages();
}
