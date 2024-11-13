// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Kernel.Domains;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Framework.Orm.EntityFramework.Contexts;

public static class ProcessEntriesMessagesBeforeSaveHelper
{
    public static ProcessBeforeSaveReport ProcessEntriesMessagesBeforeSave(this DbContext context)
    {
        var report = new ProcessBeforeSaveReport();

        foreach (var entry in context.ChangeTracker.Entries())
        {
            _ProcessEntryBeforeSave(context, entry);
            _ProcessMessageEmitters(report, entry);
        }

        return report;
    }

    private static void _ProcessMessageEmitters(ProcessBeforeSaveReport report, EntityEntry entry)
    {
        if (entry.Entity is IDistributedMessageEmitter distributedMessageEmitter)
        {
            var messages = distributedMessageEmitter.GetDistributedMessages();

            if (messages.Count > 0)
            {
                report.DistributedEmitters.Add(new(distributedMessageEmitter, messages));
            }
        }

        if (entry.Entity is ILocalMessageEmitter localMessageEmitter)
        {
            var messages = localMessageEmitter.GetLocalMessages();

            if (messages.Count > 0)
            {
                report.LocalEmitters.Add(new(localMessageEmitter, messages));
            }
        }
    }

    private static void _ProcessEntryBeforeSave(DbContext context, EntityEntry entry)
    {
        switch (entry.State)
        {
            case EntityState.Added:
            {
                if (entry.Entity is IHasConcurrencyStamp entity)
                {
                    entity.ConcurrencyStamp ??= Guid.NewGuid().ToString("N");
                }

                _PublishEntityCreatedLocalMessage(entry.Entity);

                break;
            }
            case EntityState.Modified:
            {
                if (entry.Entity is IHasConcurrencyStamp entity)
                {
                    var propertyEntry = context.Entry(entity).Property(x => x.ConcurrencyStamp);

                    if (!string.Equals(propertyEntry.OriginalValue, entity.ConcurrencyStamp, StringComparison.Ordinal))
                    {
                        propertyEntry.OriginalValue = entity.ConcurrencyStamp;
                    }
                    entity.ConcurrencyStamp = Guid.NewGuid().ToString("N");
                }

                var hasModifiedProperties =
                    entry.Properties.Any(x =>
                        x is { IsModified: true, Metadata.ValueGenerated: ValueGenerated.Never or ValueGenerated.OnAdd }
                    ) && entry.Properties.Where(x => x.IsModified).All(x => x.Metadata.IsForeignKey());

                if (hasModifiedProperties)
                {
                    _PublishEntityUpdatedLocalMessage(entry.Entity);
                }

                break;
            }
            case EntityState.Deleted:
            {
                _PublishEntityDeletedLocalMessage(entry.Entity);

                break;
            }
        }
    }

    private static void _PublishEntityCreatedLocalMessage(object entity)
    {
        if (entity is not ILocalMessageEmitter localEmitter)
        {
            return;
        }

        var genericCreatedEventType = typeof(EntityCreatedEventData<>);
        var createdEventType = genericCreatedEventType.MakeGenericType(entity.GetType());
        var createdEventMessage = (ILocalMessage)Activator.CreateInstance(createdEventType, entity)!;
        localEmitter.AddMessage(createdEventMessage);

        _PublishEntityChangedLocalMessage(entity, localEmitter);
    }

    private static void _PublishEntityUpdatedLocalMessage(object entity)
    {
        if (entity is not ILocalMessageEmitter localEmitter)
        {
            return;
        }

        var genericUpdatedEventType = typeof(EntityUpdatedEventData<>);
        var updatedEventType = genericUpdatedEventType.MakeGenericType(entity.GetType());
        var updatedEventMessage = (ILocalMessage)Activator.CreateInstance(updatedEventType, entity)!;
        localEmitter.AddMessage(updatedEventMessage);

        _PublishEntityChangedLocalMessage(entity, localEmitter);
    }

    private static void _PublishEntityDeletedLocalMessage(object entity)
    {
        if (entity is not ILocalMessageEmitter localEmitter)
        {
            return;
        }

        var genericDeletedEventType = typeof(EntityDeletedEventData<>);
        var deletedEventType = genericDeletedEventType.MakeGenericType(entity.GetType());
        var deletedEventMessage = (ILocalMessage)Activator.CreateInstance(deletedEventType, entity)!;
        localEmitter.AddMessage(deletedEventMessage);

        _PublishEntityChangedLocalMessage(entity, localEmitter);
    }

    private static void _PublishEntityChangedLocalMessage(object entity, ILocalMessageEmitter localEmitter)
    {
        var genericUpdatedEventType = typeof(EntityUpdatedEventData<>);
        var updatedEventType = genericUpdatedEventType.MakeGenericType(entity.GetType());
        var updatedEventMessage = (ILocalMessage)Activator.CreateInstance(updatedEventType, entity)!;
        localEmitter.AddMessage(updatedEventMessage);
    }
}

public sealed record ProcessBeforeSaveReport
{
    public List<EmitterDistributedMessages> DistributedEmitters { get; } = [];

    public List<EmitterLocalMessages> LocalEmitters { get; } = [];
}
