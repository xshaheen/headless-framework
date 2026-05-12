// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Headless.Abstractions;
using Headless.Domain;
using Headless.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using AccountId = Headless.Primitives.AccountId;
using UserId = Headless.Primitives.UserId;

namespace Headless.EntityFramework.Processors;

public sealed class HeadlessAuditSaveEntryProcessor(IClock clock, ICurrentUser currentUser)
    : IHeadlessSaveEntryProcessor
{
    private static readonly ConditionalWeakTable<
        Type,
        ConcurrentDictionary<Type, bool>
    > _ImplementsGenericInterfaceCache = new();

    private static readonly ConditionalWeakTable<
        Type,
        ConcurrentDictionary<Type, bool>
    >.CreateValueCallback _CreateImplementsInner = static _ => new ConcurrentDictionary<Type, bool>();

    public void Process(EntityEntry entry, HeadlessSaveEntryContext context)
    {
        switch (entry.State)
        {
            case EntityState.Added:
                _TrySetCreateAudit(entry);
                break;
            case EntityState.Modified:
                _TrySetUpdateAudit(entry);
                _TrySetDeleteAudit(entry);
                _TrySetSuspendAudit(entry);
                break;
        }
    }

    private void _TrySetCreateAudit(EntityEntry entry)
    {
        if (entry.Entity is not ICreateAudit entity)
        {
            return;
        }

        if (entity.DateCreated == default)
        {
            ObjectPropertiesHelper.TrySetProperty(entity, x => x.DateCreated, () => clock.UtcNow);
        }

        _TrySetCreateAuditId(entry, currentUser.UserId, currentUser.AccountId);
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

        if (
            entry.Metadata.FindNavigation(nameof(ICreateAudit<,>.CreatedBy)) is { } createdByNavigation
            && entry.Navigation(createdByNavigation.Name).CurrentValue is not null
        )
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

    private void _TrySetUpdateAudit(EntityEntry entry)
    {
        if (entry.Entity is not IUpdateAudit entity)
        {
            return;
        }

        _TrySetUpdateAuditDate(entry, entity);
        _TrySetUpdateAuditId(entry, currentUser.UserId, currentUser.AccountId);
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

        if (ObjectPropertiesHelper.TrySetProperty(entity, x => x.DateUpdated, () => clock.UtcNow))
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

    private void _TrySetDeleteAudit(EntityEntry entry)
    {
        if (entry.Entity is not IDeleteAudit deleteAudit || !entry.Property(nameof(IDeleteAudit.IsDeleted)).IsModified)
        {
            return;
        }

        if (deleteAudit.IsDeleted)
        {
            _TrySetDeleteAuditDate(entry, deleteAudit);
            _TrySetDeleteAuditId(entry, currentUser.UserId, currentUser.AccountId);

            return;
        }

        ObjectPropertiesHelper.TrySetPropertyToNull(deleteAudit, nameof(IDeleteAudit.DateDeleted));

        if (_ImplementsGenericInterface(entry.Entity.GetType(), typeof(IDeleteAudit<>)))
        {
            ObjectPropertiesHelper.TrySetPropertyToNull(deleteAudit, nameof(IDeleteAudit<>.DeletedById));
        }
    }

    private void _TrySetDeleteAuditDate(EntityEntry entry, IDeleteAudit entity)
    {
        if (entity.DateDeleted == null || !entry.Property(nameof(IDeleteAudit.DateDeleted)).IsModified)
        {
            ObjectPropertiesHelper.TrySetProperty(entity, x => x.DateDeleted, () => clock.UtcNow);
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

    private void _TrySetSuspendAudit(EntityEntry entry)
    {
        if (
            entry.Entity is not ISuspendAudit suspendAudit
            || !entry.Property(nameof(ISuspendAudit.IsSuspended)).IsModified
        )
        {
            return;
        }

        if (suspendAudit.IsSuspended)
        {
            _TrySetSuspendAuditDate(entry, suspendAudit);
            _TrySetSuspendAuditId(entry, currentUser.UserId, currentUser.AccountId);

            return;
        }

        ObjectPropertiesHelper.TrySetPropertyToNull(suspendAudit, nameof(ISuspendAudit.DateSuspended));

        if (_ImplementsGenericInterface(entry.Entity.GetType(), typeof(ISuspendAudit<>)))
        {
            ObjectPropertiesHelper.TrySetPropertyToNull(suspendAudit, nameof(ISuspendAudit<>.SuspendedById));
        }
    }

    private void _TrySetSuspendAuditDate(EntityEntry entry, ISuspendAudit entity)
    {
        if (entity.DateSuspended == null || !entry.Property(nameof(ISuspendAudit.DateSuspended)).IsModified)
        {
            ObjectPropertiesHelper.TrySetProperty(entity, x => x.DateSuspended, () => clock.UtcNow);
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

    private static bool _ImplementsGenericInterface(Type type, Type genericInterfaceDefinition)
    {
        var inner = _ImplementsGenericInterfaceCache.GetValue(type, _CreateImplementsInner);

        return inner.GetOrAdd(
            genericInterfaceDefinition,
            static (interfaceDef, entityType) =>
                entityType.GetInterfaces().Exists(x => x.IsGenericType && x.GetGenericTypeDefinition() == interfaceDef),
            type
        );
    }
}
