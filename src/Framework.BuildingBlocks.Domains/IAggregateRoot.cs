namespace Framework.BuildingBlocks.Domains;

/// <summary>
/// Defines an aggregate root. It's primary key may not be "Id" or it may have a composite primary key
/// Used also to restrict repositories for example to work only with aggregate roots.
/// </summary>
public interface IAggregateRoot : IEntity;

/// <inheritdoc cref="IAggregateRoot"/>
public abstract class AggregateRoot : Entity, IAggregateRoot, IMessageEmitter
{
    private List<IIntegrationMessage>? _messages;

    public void AddMessage(IIntegrationMessage e) => (_messages ??= []).Add(e);

    public void ClearMessages() => _messages?.Clear();

    public IReadOnlyList<IIntegrationMessage> GetMessages() => _messages ?? [];
}
