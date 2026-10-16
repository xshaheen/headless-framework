// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Headless.Domain;
using Headless.EntityFramework.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Headless.EntityFramework;

/// <summary>
/// Terminal save-entry processor that enqueues lifecycle domain events
/// (<c>EntityCreatedEventData</c>, <c>EntityUpdatedEventData</c>, <c>EntityDeletedEventData</c>,
/// <c>EntityChangedEventData</c>) on entities that implement <c>IDomainEventEmitter</c>.
/// </summary>
/// <remarks>
/// This processor runs last (terminal stage) so all preceding processors can mutate entities before
/// domain events are collected. Events are published by the save pipeline within the active
/// transaction before the database write. A completed local drain is retained across persistence
/// retries; failed handler entry has no per-handler checkpoint and must remain replay-safe. Entities that do not implement <c>IDomainEventEmitter</c> are silently skipped.
/// <para>
/// <c>EntityUpdatedEventData</c> is only emitted when at least one non-generated, non-foreign-key
/// property changed to avoid spurious update events on concurrency-stamp-only saves.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class HeadlessLocalEventSaveEntryProcessor : IHeadlessSaveEntryProcessor
{
    private static readonly ConditionalWeakTable<Type, Func<object, object>> _CreatedFactories = [];
    private static readonly ConditionalWeakTable<Type, Func<object, object>> _UpdatedFactories = [];
    private static readonly ConditionalWeakTable<Type, Func<object, object>> _DeletedFactories = [];
    private static readonly ConditionalWeakTable<Type, Func<object, object>> _ChangedFactories = [];

    private static readonly ConditionalWeakTable<Type, Func<object, object>>.CreateValueCallback _CreatedFactory =
        static type => _CompileEventFactory(typeof(EntityCreatedEventData<>), type);

    private static readonly ConditionalWeakTable<Type, Func<object, object>>.CreateValueCallback _UpdatedFactory =
        static type => _CompileEventFactory(typeof(EntityUpdatedEventData<>), type);

    private static readonly ConditionalWeakTable<Type, Func<object, object>>.CreateValueCallback _DeletedFactory =
        static type => _CompileEventFactory(typeof(EntityDeletedEventData<>), type);

    private static readonly ConditionalWeakTable<Type, Func<object, object>>.CreateValueCallback _ChangedFactory =
        static type => _CompileEventFactory(typeof(EntityChangedEventData<>), type);

    // The payload types are what the dispatcher resolves handlers for, so they are built once per entity type.
    private static readonly ConditionalWeakTable<Type, LifecycleEventTypes> _EventTypes = [];

    private static readonly ConditionalWeakTable<Type, LifecycleEventTypes>.CreateValueCallback _CreateEventTypes =
        static type =>
            new(
                typeof(EntityCreatedEventData<>).MakeGenericType(type),
                typeof(EntityUpdatedEventData<>).MakeGenericType(type),
                typeof(EntityDeletedEventData<>).MakeGenericType(type),
                typeof(EntityChangedEventData<>).MakeGenericType(type)
            );

    /// <summary>
    /// Enqueues the appropriate lifecycle domain event on the entity based on its current
    /// <see cref="EntityState"/>. No-ops for entities that do not implement <c>IDomainEventEmitter</c>.
    /// </summary>
    /// <remarks>
    /// With the built-in dispatcher, an event type that no handler is registered for is not enqueued: nobody would
    /// observe it, and enqueueing it would make the save open a transaction to dispatch nothing.
    /// </remarks>
    /// <param name="entry">The tracked entity entry to inspect.</param>
    /// <param name="context">The per-save scratchpad that knows which event types have handlers.</param>
    public void Process(EntityEntry entry, HeadlessSaveEntryContext context)
    {
        if (entry.Entity is not IDomainEventEmitter emitter)
        {
            return;
        }

        switch (entry.State)
        {
            case EntityState.Added:
                _Publish(emitter, context, _CreatedFactories, _CreatedFactory, static types => types.Created);
                break;
            case EntityState.Modified:
                // The property scan allocates a PropertyEntry per property, so it runs only when an update or change
                // event could reach a handler.
                if (!_HasUpdateHandler(emitter, context) || !_HasDomainModifiedProperties(entry))
                {
                    return;
                }

                _Publish(emitter, context, _UpdatedFactories, _UpdatedFactory, static types => types.Updated);
                break;
            case EntityState.Deleted:
                _Publish(emitter, context, _DeletedFactories, _DeletedFactory, static types => types.Deleted);
                break;
            default:
                return;
        }

        _Publish(emitter, context, _ChangedFactories, _ChangedFactory, static types => types.Changed);
    }

    private static bool _HasUpdateHandler(IDomainEventEmitter entity, HeadlessSaveEntryContext context)
    {
        if (context.DomainEventHandlerFilter is null)
        {
            return true;
        }

        var types = _EventTypes.GetValue(entity.GetType(), _CreateEventTypes);

        return context.HasDomainEventHandler(types.Updated) || context.HasDomainEventHandler(types.Changed);
    }

    private static void _Publish(
        IDomainEventEmitter entity,
        HeadlessSaveEntryContext context,
        ConditionalWeakTable<Type, Func<object, object>> factories,
        ConditionalWeakTable<Type, Func<object, object>>.CreateValueCallback createFactory,
        Func<LifecycleEventTypes, Type> selectEventType
    )
    {
        var entityType = entity.GetType();

        if (
            context.DomainEventHandlerFilter is not null
            && !context.HasDomainEventHandler(selectEventType(_EventTypes.GetValue(entityType, _CreateEventTypes)))
        )
        {
            return;
        }

        entity.AddDomainEvent(factories.GetValue(entityType, createFactory)(entity));
    }

    private static Func<object, object> _CompileEventFactory(Type genericEventType, Type entityType)
    {
        var eventType = genericEventType.MakeGenericType(entityType);
        var ctor = eventType.GetConstructor([entityType])!;
        var param = Expression.Parameter(typeof(object));
        var converted = Expression.Convert(param, entityType);
        var newExpr = Expression.New(ctor, converted);
        var cast = Expression.Convert(newExpr, typeof(object));

        return Expression.Lambda<Func<object, object>>(cast, param).Compile();
    }

    private static bool _HasDomainModifiedProperties(EntityEntry entry)
    {
        return entry.Properties.Any(_IsDomainModified);
    }

    private static bool _IsDomainModified(PropertyEntry property)
    {
        return property is { IsModified: true, Metadata.ValueGenerated: ValueGenerated.Never or ValueGenerated.OnAdd }
            && !property.Metadata.IsForeignKey();
    }

    private sealed record LifecycleEventTypes(Type Created, Type Updated, Type Deleted, Type Changed);
}
