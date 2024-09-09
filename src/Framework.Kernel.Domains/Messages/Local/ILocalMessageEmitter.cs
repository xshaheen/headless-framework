// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Domains;

public interface ILocalMessageEmitter
{
    void AddMessage(ILocalMessage messages);

    void ClearLocalMessages();

    IReadOnlyList<ILocalMessage> GetLocalMessages();
}
