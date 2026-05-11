// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel.DataAnnotations.Schema;
using Headless.Domain;
using Headless.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using AccountId = Headless.Primitives.AccountId;
using UserId = Headless.Primitives.UserId;

namespace Headless.EntityFramework.Contexts;

public partial class HeadlessEntityModelProcessor
{
    public virtual ProcessBeforeSaveReport ProcessEntries(DbContext db)
    {
        var report = new ProcessBeforeSaveReport();
        var currentUserId = _currentUser.UserId;
        var currentAccountId = _currentUser.AccountId;

        foreach (var entry in db.ChangeTracker.Entries())
        {
            ProcessEntry(entry, currentUserId, currentAccountId);
            CollectMessages(entry, report);
        }

        return report;
    }

    protected virtual void ProcessEntry(EntityEntry entry, UserId? currentUserId, AccountId? currentAccountId)
    {
        switch (entry.State)
        {
            case EntityState.Added:
                _TrySetGuidId(entry);
                _TrySetMultiTenantId(entry);
                _TrySetCreateAudit(entry, currentUserId, currentAccountId);
                _TrySetConcurrencyStamp(entry);
                _TryPublishCreatedLocalMessage(entry);

                break;
            case EntityState.Modified:
                _TrySetUpdateAudit(entry, currentUserId, currentAccountId);
                _TrySetDeleteAudit(entry, currentUserId, currentAccountId);
                _TrySetSuspendAudit(entry, currentUserId, currentAccountId);
                _TryUpdateConcurrencyStamp(entry);
                _TryPublishUpdatedLocalMessage(entry);

                break;
            case EntityState.Deleted:
                _TryPublishDeletedLocalMessage(entry);

                break;
        }
    }

    protected virtual void CollectMessages(EntityEntry entry, ProcessBeforeSaveReport report)
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

    private void _TrySetMultiTenantId(EntityEntry entry)
    {
        if (entry.Entity is not IMultiTenant entity || !string.IsNullOrEmpty(entity.TenantId))
        {
            return;
        }

        if (entry.Property(nameof(IMultiTenant.TenantId)) is { IsModified: true, CurrentValue: not (null or "") })
        {
            return;
        }

        ObjectPropertiesHelper.TrySetProperty(entity, x => x.TenantId, () => _currentTenant.Id);
    }

    private void _TrySetGuidId(EntityEntry entry)
    {
        if (entry.Entity is not IEntity<Guid> entity || entity.Id != Guid.Empty)
        {
            return;
        }

        var idProperty = entry.Property(nameof(IEntity<>.Id)).Metadata.PropertyInfo!;

        if (
            idProperty.GetFirstOrDefaultAttribute<DatabaseGeneratedAttribute>() is
            { DatabaseGeneratedOption: not DatabaseGeneratedOption.None }
        )
        {
            return;
        }

        ObjectPropertiesHelper.TrySetProperty(entity, x => x.Id, _guidGenerator.Create);
    }

    private void _TrySetCreateAudit(EntityEntry entry, UserId? currentUserId, AccountId? currentAccountId)
    {
        if (entry.Entity is ICreateAudit entity)
        {
            _TrySetCreateAuditDate(entity);
            _TrySetCreateAuditId(entry, currentUserId, currentAccountId);
        }
    }

    private void _TrySetCreateAuditDate(ICreateAudit entity)
    {
        if (entity.DateCreated == default)
        {
            ObjectPropertiesHelper.TrySetProperty(entity, x => x.DateCreated, () => _clock.UtcNow);
        }
    }

    private static void _TrySetCreateAuditId(EntityEntry entry, UserId? currentUserId, AccountId? currentAccountId)
    {
        if (currentUserId is null && currentAccountId is null)
        {
            return;
        }

        var byUser = entry.Entity as ICreateAudit<UserId>;
        var byAccount = entry.Entity as ICreateAudit<AccountId>;

        if (byUser is null && byAccount is null)
        {
            return;
        }

        if (entry.Property(nameof(ICreateAudit<>.CreatedById)) is { IsModified: true, CurrentValue: not null })
        {
            return;
        }

        var createdBy = entry.Navigations.FirstOrDefault(p =>
            string.Equals(p.Metadata.Name, nameof(ICreateAudit<,>.CreatedBy), StringComparison.Ordinal)
        );

        if (createdBy?.CurrentValue is not null)
        {
            return;
        }

        if (byUser is not null && byUser.CreatedById == null && currentUserId is not null)
        {
            ObjectPropertiesHelper.TrySetProperty(byUser, x => x.CreatedById, () => currentUserId);

            return;
        }

        if (byAccount is not null && byAccount.CreatedById == null && currentAccountId is not null)
        {
            ObjectPropertiesHelper.TrySetProperty(byAccount, x => x.CreatedById, () => currentAccountId);
        }
    }

    private void _TrySetUpdateAudit(EntityEntry entry, UserId? currentUserId, AccountId? currentAccountId)
    {
        if (entry.Entity is not IUpdateAudit entity)
        {
            return;
        }

        _TrySetUpdateAuditDate(entry, entity);
        _TrySetUpdateAuditId(entry, currentUserId, currentAccountId);
    }

    private void _TrySetUpdateAuditDate(EntityEntry entry, IUpdateAudit entity)
    {
        var propertyEntry = entry.Property(nameof(IUpdateAudit.DateUpdated));

        if (
            entity.DateUpdated != null
            && propertyEntry.IsModified
            && propertyEntry.CurrentValue != propertyEntry.OriginalValue
        )
        {
            return;
        }

        if (ObjectPropertiesHelper.TrySetProperty(entity, x => x.DateUpdated, () => _clock.UtcNow))
        {
            propertyEntry.IsModified = true;
        }
    }

    private static void _TrySetUpdateAuditId(EntityEntry entry, UserId? currentUserId, AccountId? currentAccountId)
    {
        if (currentUserId is null && currentAccountId is null)
        {
            return;
        }

        var byUser = entry.Entity as IUpdateAudit<UserId>;
        var byAccount = entry.Entity as IUpdateAudit<AccountId>;

        if (byUser is null && byAccount is null)
        {
            return;
        }

        var propertyEntry = entry.Property(nameof(IUpdateAudit<>.UpdatedById));

        if (propertyEntry.IsModified && propertyEntry.CurrentValue != propertyEntry.OriginalValue)
        {
            return;
        }

        if (byUser is not null && byUser.UpdatedById is null && currentUserId is not null)
        {
            if (ObjectPropertiesHelper.TrySetProperty(byUser, x => x.UpdatedById, () => currentUserId))
            {
                propertyEntry.IsModified = true;
            }

            return;
        }

        if (byAccount is not null && byAccount.UpdatedById is null && currentAccountId is not null)
        {
            if (ObjectPropertiesHelper.TrySetProperty(byAccount, x => x.UpdatedById, () => currentAccountId))
            {
                propertyEntry.IsModified = true;
            }
        }
    }

    private bool _TrySetDeleteAudit(EntityEntry entry, UserId? currentUserId, AccountId? currentAccountId)
    {
        if (entry.Entity is not IDeleteAudit deleteAudit || !entry.Property(nameof(IDeleteAudit.IsDeleted)).IsModified)
        {
            return false;
        }

        if (deleteAudit.IsDeleted)
        {
            _TrySetDeleteAuditDate(entry, deleteAudit);
            _TrySetDeleteAuditId(entry, currentUserId, currentAccountId);

            return true;
        }

        ObjectPropertiesHelper.TrySetPropertyToNull(deleteAudit, nameof(IDeleteAudit.DateDeleted));

        if (_ImplementsGenericInterface(entry.Entity.GetType(), typeof(IDeleteAudit<>)))
        {
            ObjectPropertiesHelper.TrySetPropertyToNull(deleteAudit, nameof(IDeleteAudit<>.DeletedById));
        }

        return true;
    }

    private void _TrySetDeleteAuditDate(EntityEntry entry, IDeleteAudit entity)
    {
        if (entity.DateDeleted == null || !entry.Property(nameof(IDeleteAudit.DateDeleted)).IsModified)
        {
            ObjectPropertiesHelper.TrySetProperty(entity, x => x.DateDeleted, () => _clock.UtcNow);
        }
    }

    private static void _TrySetDeleteAuditId(EntityEntry entry, UserId? currentUserId, AccountId? currentAccountId)
    {
        if (currentUserId is null && currentAccountId is null)
        {
            return;
        }

        var byUser = entry.Entity as IDeleteAudit<UserId>;
        var byAccount = entry.Entity as IDeleteAudit<AccountId>;

        if (byUser is null && byAccount is null)
        {
            return;
        }

        var propertyEntry = entry.Property(nameof(IDeleteAudit<>.DeletedById));

        if (propertyEntry.IsModified && propertyEntry.CurrentValue != propertyEntry.OriginalValue)
        {
            return;
        }

        if (byUser is not null && byUser.DeletedById is null && currentUserId is not null)
        {
            ObjectPropertiesHelper.TrySetProperty(byUser, x => x.DeletedById, () => currentUserId);
        }

        if (byAccount is not null && byAccount.DeletedById is null && currentAccountId is not null)
        {
            ObjectPropertiesHelper.TrySetProperty(byAccount, x => x.DeletedById, () => currentAccountId);
        }
    }

    private bool _TrySetSuspendAudit(EntityEntry entry, UserId? currentUserId, AccountId? currentAccountId)
    {
        if (
            entry.Entity is not ISuspendAudit suspendAudit
            || !entry.Property(nameof(ISuspendAudit.IsSuspended)).IsModified
        )
        {
            return false;
        }

        if (suspendAudit.IsSuspended)
        {
            _TrySetSuspendAuditDate(entry, suspendAudit);
            _TrySetSuspendAuditId(entry, currentUserId, currentAccountId);

            return true;
        }

        ObjectPropertiesHelper.TrySetPropertyToNull(suspendAudit, nameof(ISuspendAudit.DateSuspended));

        if (_ImplementsGenericInterface(entry.Entity.GetType(), typeof(ISuspendAudit<>)))
        {
            ObjectPropertiesHelper.TrySetPropertyToNull(suspendAudit, nameof(ISuspendAudit<>.SuspendedById));
        }

        return true;
    }

    private void _TrySetSuspendAuditDate(EntityEntry entry, ISuspendAudit entity)
    {
        if (entity.DateSuspended == null || !entry.Property(nameof(ISuspendAudit.DateSuspended)).IsModified)
        {
            ObjectPropertiesHelper.TrySetProperty(entity, x => x.DateSuspended, () => _clock.UtcNow);
        }
    }

    private static void _TrySetSuspendAuditId(EntityEntry entry, UserId? currentUserId, AccountId? currentAccountId)
    {
        if (currentUserId is null && currentAccountId is null)
        {
            return;
        }

        var byUser = entry.Entity as ISuspendAudit<UserId>;
        var byAccount = entry.Entity as ISuspendAudit<AccountId>;

        if (byUser is null && byAccount is null)
        {
            return;
        }

        var propertyEntry = entry.Property(nameof(ISuspendAudit<>.SuspendedById));

        if (propertyEntry.IsModified && propertyEntry.CurrentValue != propertyEntry.OriginalValue)
        {
            return;
        }

        if (byUser is not null && byUser.SuspendedById is null && currentUserId is not null)
        {
            ObjectPropertiesHelper.TrySetProperty(byUser, x => x.SuspendedById, () => currentUserId);
        }

        if (byAccount is not null && byAccount.SuspendedById is null && currentAccountId is not null)
        {
            ObjectPropertiesHelper.TrySetProperty(byAccount, x => x.SuspendedById, () => currentAccountId);
        }
    }

    private static void _TrySetConcurrencyStamp(EntityEntry entry)
    {
        if (entry.Entity is IHasConcurrencyStamp { ConcurrencyStamp: null } entity)
        {
            ObjectPropertiesHelper.TrySetProperty(entity, x => x.ConcurrencyStamp, () => Guid.NewGuid().ToString("N"));
        }
    }

    private static void _TryUpdateConcurrencyStamp(EntityEntry entry)
    {
        if (entry.Entity is IHasConcurrencyStamp entity)
        {
            ObjectPropertiesHelper.TrySetProperty(entity, x => x.ConcurrencyStamp, () => Guid.NewGuid().ToString("N"));
        }
    }

    private static bool _ImplementsGenericInterface(Type type, Type genericInterfaceDefinition)
    {
        return type.GetInterfaces()
            .Any(x => x.IsGenericType && x.GetGenericTypeDefinition() == genericInterfaceDefinition);
    }
}
