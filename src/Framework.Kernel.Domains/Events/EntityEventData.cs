// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Domains;

public abstract class EntityEventData<TEntity>(TEntity entity) : ILocalMessage
{
    /// <summary>Related entity with this event.</summary>
    public TEntity Entity { get; } = entity;
}
