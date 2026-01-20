// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Domain;

public abstract class EntityEventData<TEntity>(TEntity entity) : ILocalMessage
{
    public string UniqueId { get; } = Guid.CreateVersion7().ToString();

    /// <summary>Related entity with this event.</summary>
    public TEntity Entity { get; } = entity;
}
