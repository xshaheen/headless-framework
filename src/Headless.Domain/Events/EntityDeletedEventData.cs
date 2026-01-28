// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Headless.Domain;

/// <summary>This type of event can be used to notify just after the deletion of an Entity.</summary>
/// <typeparam name="TEntity">Entity type</typeparam>
public sealed class EntityDeletedEventData<TEntity>(TEntity entity) : EntityEventData<TEntity>(entity);
