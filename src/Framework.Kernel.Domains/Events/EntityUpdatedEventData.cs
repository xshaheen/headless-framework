#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Domains;

/// <summary>This type of event can be used to notify just after the update of an Entity.</summary>
/// <typeparam name="TEntity">Entity type</typeparam>
public sealed class EntityUpdatedEventData<TEntity>(TEntity entity) : EntityEventData<TEntity>(entity);
