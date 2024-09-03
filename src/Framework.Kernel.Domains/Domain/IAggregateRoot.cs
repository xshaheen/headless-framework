// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Domains;

/// <summary>
/// Defines an aggregate root. It's primary key may not be "Id" or it may have a composite primary key
/// Used also to restrict repositories for example to work only with aggregate roots.
/// </summary>
public interface IAggregateRoot : IEntity;

/// <inheritdoc cref="IAggregateRoot"/>
public abstract class AggregateRoot : Entity, IAggregateRoot, IDistributedMessageEmitter, ILocalMessageEmitter
{
    private List<ILocalMessage>? _localMessages;
    private List<IDistributedMessage>? _distributedMessages;

    public void AddMessage(IDistributedMessage e) => (_distributedMessages ??= []).Add(e);

    public void ClearDistributedMessages() => _distributedMessages?.Clear();

    public IReadOnlyList<IDistributedMessage> GetDistributedMessages() => _distributedMessages ?? [];

    public void AddMessage(ILocalMessage e) => (_localMessages ??= []).Add(e);

    public IReadOnlyList<ILocalMessage> GetLocalMessages() => _localMessages ?? [];

    public void ClearLocalMessages() => _localMessages?.Clear();
}
