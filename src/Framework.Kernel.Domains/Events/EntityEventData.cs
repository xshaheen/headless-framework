// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Domains;

public abstract class EntityEventData<TEntity>(TEntity entity) : ILocalMessage
{
    /// <summary>Related entity with this event.</summary>
    public TEntity Entity { get; } = entity;
}
