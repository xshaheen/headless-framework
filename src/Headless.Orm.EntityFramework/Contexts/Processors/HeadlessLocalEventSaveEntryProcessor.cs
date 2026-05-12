// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Headless.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Headless.EntityFramework.Processors;

public sealed class HeadlessLocalEventSaveEntryProcessor : IHeadlessSaveEntryProcessor
{
    private static readonly ConditionalWeakTable<Type, Func<object, ILocalMessage>> _CreatedFactories = new();
    private static readonly ConditionalWeakTable<Type, Func<object, ILocalMessage>> _UpdatedFactories = new();
    private static readonly ConditionalWeakTable<Type, Func<object, ILocalMessage>> _DeletedFactories = new();
    private static readonly ConditionalWeakTable<Type, Func<object, ILocalMessage>> _ChangedFactories = new();

    private static readonly ConditionalWeakTable<
        Type,
        Func<object, ILocalMessage>
    >.CreateValueCallback _CreatedFactory = static type => _CompileEventFactory(typeof(EntityCreatedEventData<>), type);

    private static readonly ConditionalWeakTable<
        Type,
        Func<object, ILocalMessage>
    >.CreateValueCallback _UpdatedFactory = static type => _CompileEventFactory(typeof(EntityUpdatedEventData<>), type);

    private static readonly ConditionalWeakTable<
        Type,
        Func<object, ILocalMessage>
    >.CreateValueCallback _DeletedFactory = static type => _CompileEventFactory(typeof(EntityDeletedEventData<>), type);

    private static readonly ConditionalWeakTable<
        Type,
        Func<object, ILocalMessage>
    >.CreateValueCallback _ChangedFactory = static type => _CompileEventFactory(typeof(EntityChangedEventData<>), type);

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
        if (entry.Entity is ILocalMessageEmitter localEmitter)
        {
            _PublishEntityCreated(localEmitter);
            _PublishEntityChanged(localEmitter);
        }
    }

    private static void _TryPublishUpdatedLocalMessage(EntityEntry entry)
    {
        if (entry.Entity is ILocalMessageEmitter localEmitter && _HasDomainModifiedProperties(entry))
        {
            _PublishEntityUpdated(localEmitter);
            _PublishEntityChanged(localEmitter);
        }
    }

    private static void _TryPublishDeletedLocalMessage(EntityEntry entry)
    {
        if (entry.Entity is ILocalMessageEmitter localEmitter)
        {
            _PublishEntityDeleted(localEmitter);
            _PublishEntityChanged(localEmitter);
        }
    }

    private static void _PublishEntityCreated(ILocalMessageEmitter entity)
    {
        var factory = _CreatedFactories.GetValue(entity.GetType(), _CreatedFactory);
        entity.AddMessage(factory(entity));
    }

    private static void _PublishEntityUpdated(ILocalMessageEmitter entity)
    {
        var factory = _UpdatedFactories.GetValue(entity.GetType(), _UpdatedFactory);
        entity.AddMessage(factory(entity));
    }

    private static void _PublishEntityDeleted(ILocalMessageEmitter entity)
    {
        var factory = _DeletedFactories.GetValue(entity.GetType(), _DeletedFactory);
        entity.AddMessage(factory(entity));
    }

    private static void _PublishEntityChanged(ILocalMessageEmitter entity)
    {
        var factory = _ChangedFactories.GetValue(entity.GetType(), _ChangedFactory);
        entity.AddMessage(factory(entity));
    }

    private static Func<object, ILocalMessage> _CompileEventFactory(Type genericEventType, Type entityType)
    {
        var eventType = genericEventType.MakeGenericType(entityType);
        var ctor = eventType.GetConstructor([entityType])!;
        var param = Expression.Parameter(typeof(object));
        var converted = Expression.Convert(param, entityType);
        var newExpr = Expression.New(ctor, converted);
        var cast = Expression.Convert(newExpr, typeof(ILocalMessage));

        return Expression.Lambda<Func<object, ILocalMessage>>(cast, param).Compile();
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
