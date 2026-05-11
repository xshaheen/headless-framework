// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Linq.Expressions;
using Headless.Domain;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Headless.EntityFramework.Contexts;

public partial class HeadlessEntityModelProcessor
{
    private static readonly ConcurrentDictionary<Type, Func<object, ILocalMessage>> _CreatedFactories = new();
    private static readonly ConcurrentDictionary<Type, Func<object, ILocalMessage>> _UpdatedFactories = new();
    private static readonly ConcurrentDictionary<Type, Func<object, ILocalMessage>> _DeletedFactories = new();
    private static readonly ConcurrentDictionary<Type, Func<object, ILocalMessage>> _ChangedFactories = new();

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
        var factory = _CreatedFactories.GetOrAdd(
            entity.GetType(),
            static type => _CompileEventFactory(typeof(EntityCreatedEventData<>), type)
        );
        entity.AddMessage(factory(entity));
    }

    private static void _PublishEntityUpdated(ILocalMessageEmitter entity)
    {
        var factory = _UpdatedFactories.GetOrAdd(
            entity.GetType(),
            static type => _CompileEventFactory(typeof(EntityUpdatedEventData<>), type)
        );
        entity.AddMessage(factory(entity));
    }

    private static void _PublishEntityDeleted(ILocalMessageEmitter entity)
    {
        var factory = _DeletedFactories.GetOrAdd(
            entity.GetType(),
            static type => _CompileEventFactory(typeof(EntityDeletedEventData<>), type)
        );
        entity.AddMessage(factory(entity));
    }

    private static void _PublishEntityChanged(ILocalMessageEmitter entity)
    {
        var factory = _ChangedFactories.GetOrAdd(
            entity.GetType(),
            static type => _CompileEventFactory(typeof(EntityChangedEventData<>), type)
        );
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
        return property.IsModified
            && property.Metadata.ValueGenerated is ValueGenerated.Never or ValueGenerated.OnAdd
            && !property.Metadata.IsForeignKey();
    }
}
