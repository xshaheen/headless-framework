// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Domain;

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

    public void AddMessage(IDistributedMessage message) => (_distributedMessages ??= []).Add(message);

    public void ClearDistributedMessages() => _distributedMessages?.Clear();

    public IReadOnlyList<IDistributedMessage> GetDistributedMessages() => _distributedMessages ?? [];

    public void AddMessage(ILocalMessage messages) => (_localMessages ??= []).Add(messages);

    public IReadOnlyList<ILocalMessage> GetLocalMessages() => _localMessages ?? [];

    public void ClearLocalMessages() => _localMessages?.Clear();
}
