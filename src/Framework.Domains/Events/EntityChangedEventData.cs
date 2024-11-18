// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Domains;

/// <summary>
/// Used to pass data for an event when an entity (<see cref="IEntity"/>) is changed (created, updated, or deleted).
/// See <see cref="EntityCreatedEventData{TEntity}"/>, <see cref="EntityDeletedEventData{TEntity}"/> and <see cref="EntityUpdatedEventData{TEntity}"/> classes.
/// </summary>
/// <typeparam name="TEntity">Entity type</typeparam>
public sealed class EntityChangedEventData<TEntity>(TEntity entity) : EntityEventData<TEntity>(entity);
