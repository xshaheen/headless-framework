// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Headless.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Headless.EntityFramework.Contexts.Processors;

/// <summary>
/// Terminal save-entry processor that enqueues lifecycle domain events
/// (<c>EntityCreatedEventData</c>, <c>EntityUpdatedEventData</c>, <c>EntityDeletedEventData</c>,
/// <c>EntityChangedEventData</c>) on entities that implement <c>IDomainEventEmitter</c>.
/// </summary>
/// <remarks>
/// This processor runs last (terminal stage) so all preceding processors can mutate entities before
/// domain events are collected. Events are published by the save pipeline within the active
/// transaction before the database write, with an at-most-once guard across execution-strategy
/// retries. Entities that do not implement <c>IDomainEventEmitter</c> are silently skipped.
/// <para>
/// <c>EntityUpdatedEventData</c> is only emitted when at least one non-generated, non-foreign-key
/// property changed to avoid spurious update events on concurrency-stamp-only saves.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class HeadlessLocalEventSaveEntryProcessor : IHeadlessSaveEntryProcessor
{
    private static readonly ConditionalWeakTable<Type, Func<object, IDomainEvent>> _CreatedFactories = [];
    private static readonly ConditionalWeakTable<Type, Func<object, IDomainEvent>> _UpdatedFactories = [];
    private static readonly ConditionalWeakTable<Type, Func<object, IDomainEvent>> _DeletedFactories = [];
    private static readonly ConditionalWeakTable<Type, Func<object, IDomainEvent>> _ChangedFactories = [];

    private static readonly ConditionalWeakTable<Type, Func<object, IDomainEvent>>.CreateValueCallback _CreatedFactory =
        static type => _CompileEventFactory(typeof(EntityCreatedEventData<>), type);

    private static readonly ConditionalWeakTable<Type, Func<object, IDomainEvent>>.CreateValueCallback _UpdatedFactory =
        static type => _CompileEventFactory(typeof(EntityUpdatedEventData<>), type);

    private static readonly ConditionalWeakTable<Type, Func<object, IDomainEvent>>.CreateValueCallback _DeletedFactory =
        static type => _CompileEventFactory(typeof(EntityDeletedEventData<>), type);

    private static readonly ConditionalWeakTable<Type, Func<object, IDomainEvent>>.CreateValueCallback _ChangedFactory =
        static type => _CompileEventFactory(typeof(EntityChangedEventData<>), type);

    /// <summary>
    /// Enqueues the appropriate lifecycle domain event on the entity based on its current
    /// <see cref="EntityState"/>. No-ops for entities that do not implement <c>IDomainEventEmitter</c>.
    /// </summary>
    /// <param name="entry">The tracked entity entry to inspect.</param>
    /// <param name="context">The per-save scratchpad (not used by this processor).</param>
    public void Process(EntityEntry entry, HeadlessSaveEntryContext context)
    {
        switch (entry.State)
        {
            case EntityState.Added:
                _TryPublishCreatedLocalMessage(entry);
                break;
            case EntityState.Modified:
                _TryPublishUpdatedLocalMessage(entry);
                break;
            case EntityState.Deleted:
                _TryPublishDeletedLocalMessage(entry);
                break;
        }
    }

    private static void _TryPublishCreatedLocalMessage(EntityEntry entry)
    {
        if (entry.Entity is IDomainEventEmitter localEmitter)
        {
            _PublishEntityCreated(localEmitter);
            _PublishEntityChanged(localEmitter);
        }
    }

    private static void _TryPublishUpdatedLocalMessage(EntityEntry entry)
    {
        if (entry.Entity is IDomainEventEmitter localEmitter && _HasDomainModifiedProperties(entry))
        {
            _PublishEntityUpdated(localEmitter);
            _PublishEntityChanged(localEmitter);
        }
    }

    private static void _TryPublishDeletedLocalMessage(EntityEntry entry)
    {
        if (entry.Entity is IDomainEventEmitter localEmitter)
        {
            _PublishEntityDeleted(localEmitter);
            _PublishEntityChanged(localEmitter);
        }
    }

    private static void _PublishEntityCreated(IDomainEventEmitter entity)
    {
        var factory = _CreatedFactories.GetValue(entity.GetType(), _CreatedFactory);
        entity.AddDomainEvent(factory(entity));
    }

    private static void _PublishEntityUpdated(IDomainEventEmitter entity)
    {
        var factory = _UpdatedFactories.GetValue(entity.GetType(), _UpdatedFactory);
        entity.AddDomainEvent(factory(entity));
    }

    private static void _PublishEntityDeleted(IDomainEventEmitter entity)
    {
        var factory = _DeletedFactories.GetValue(entity.GetType(), _DeletedFactory);
        entity.AddDomainEvent(factory(entity));
    }

    private static void _PublishEntityChanged(IDomainEventEmitter entity)
    {
        var factory = _ChangedFactories.GetValue(entity.GetType(), _ChangedFactory);
        entity.AddDomainEvent(factory(entity));
    }

    private static Func<object, IDomainEvent> _CompileEventFactory(Type genericEventType, Type entityType)
    {
        var eventType = genericEventType.MakeGenericType(entityType);
        var ctor = eventType.GetConstructor([entityType])!;
        var param = Expression.Parameter(typeof(object));
        var converted = Expression.Convert(param, entityType);
        var newExpr = Expression.New(ctor, converted);
        var cast = Expression.Convert(newExpr, typeof(IDomainEvent));

        return Expression.Lambda<Func<object, IDomainEvent>>(cast, param).Compile();
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
}
