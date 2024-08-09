// ReSharper disable once CheckNamespace
namespace Framework.BuildingBlocks.Domains;

/// <summary>
/// Defines an aggregate root. It's primary key may not be "Id" or it may have a composite primary key
/// Used also to restrict repositories for example to work only with aggregate roots.
/// </summary>
public interface IAggregateRoot : IEntity;

/// <inheritdoc cref="IAggregateRoot"/>
public abstract class AggregateRoot : Entity, IAggregateRoot, IDistributedMessageEmitter
{
    private List<IDistributedMessage>? _messages;

    public void AddMessage(IDistributedMessage e) => (_messages ??= []).Add(e);

    public void ClearDistributedMessages() => _messages?.Clear();

    public IReadOnlyList<IDistributedMessage> GetDistributedMessages() => _messages ?? [];
}
