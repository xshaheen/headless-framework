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

namespace Headless.EntityFramework.Contexts.Processors;

/// <summary>
/// Save-entry processor that stamps audit timestamps and actor identifiers on entities that implement
/// the Headless audit interfaces.
/// </summary>
/// <remarks>
/// On <c>Added</c> entries it sets <c>ICreateAudit.CreatedAt</c> (if not already set) and
/// <c>CreatedById</c> (resolved from <c>ICurrentUser</c>, skipped if already set or the user is
/// anonymous). On <c>Modified</c> entries it updates <c>IUpdateAudit.UpdatedAt</c> and
/// <c>UpdatedById</c>, and reconciles delete/suspend audit fields when <c>IsDeleted</c> or
/// <c>IsSuspended</c> transitions are detected.
/// </remarks>
[PublicAPI]
public sealed class HeadlessAuditSaveEntryProcessor(TimeProvider timeProvider, ICurrentUser currentUser)
    : IHeadlessSaveEntryProcessor
{
    private static readonly ConditionalWeakTable<
        Type,
        ConcurrentDictionary<Type, bool>
    > _ImplementsGenericInterfaceCache = [];

    private static readonly ConditionalWeakTable<
        Type,
        ConcurrentDictionary<Type, bool>
    >.CreateValueCallback _CreateImplementsInner = static _ => new ConcurrentDictionary<Type, bool>();

    // Method group captured once instead of a `() => timeProvider.GetUtcNow()` lambda per stamped entity:
    // that lambda closes over `this`, so it allocates on every save. Stays a factory rather than an eager
    // value so the clock is read only when the target property actually exists.
    private readonly Func<DateTimeOffset> _getUtcNow = timeProvider.GetUtcNow;

    /// <summary>Stamps audit fields on the entry based on its current <see cref="EntityState"/>.</summary>
    /// <param name="entry">The tracked entity entry to audit.</param>
    /// <param name="context">The per-save scratchpad (tenant id unused by this processor).</param>
    public void Process(EntityEntry entry, HeadlessSaveEntryContext context)
    {
        switch (entry.State)
        {
            case EntityState.Added:
                _TrySetCreateAudit(entry);
                break;
            case EntityState.Modified:
                // ICurrentUser implementations re-scan and re-parse claims on every UserId/AccountId read, so the
                // stampers share one at-most-once resolution instead of paying for up to three. Resolution stays
                // lazy inside the id stampers: an audited entity whose branches do not fire (an IDeleteAudit
                // modified on an unrelated property, a non-generic IUpdateAudit) performs zero claim scans.
                if (entry.Entity is IUpdateAudit or IDeleteAudit or ISuspendAudit)
                {
                    var actor = new ActorPair(currentUser);

                    _TrySetUpdateAudit(entry, ref actor);
                    _TrySetDeleteAudit(entry, ref actor);
                    _TrySetSuspendAudit(entry, ref actor);
                }

                break;
        }
    }

    private void _TrySetCreateAudit(EntityEntry entry)
    {
        if (entry.Entity is not ICreateAudit entity)
        {
            return;
        }

        if (entity.CreatedAt == default)
        {
            ObjectPropertiesHelper.TrySetProperty(entity, nameof(ICreateAudit.CreatedAt), _getUtcNow);
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
            ObjectPropertiesHelper.TrySetPropertyValue(byUser, nameof(ICreateAudit<>.CreatedById), currentUserId);

            return;
        }

        if (byAccount is not null && byAccount.CreatedById == null && currentAccountId is not null)
        {
            ObjectPropertiesHelper.TrySetPropertyValue(byAccount, nameof(ICreateAudit<>.CreatedById), currentAccountId);
        }
    }

    private void _TrySetUpdateAudit(EntityEntry entry, ref ActorPair actor)
    {
        if (entry.Entity is not IUpdateAudit entity)
        {
            return;
        }

        _TrySetUpdateAuditDate(entry, entity);
        _TrySetUpdateAuditId(entry, ref actor);
    }

    private void _TrySetUpdateAuditDate(EntityEntry entry, IUpdateAudit entity)
    {
        var propertyEntry = entry.Property(nameof(IUpdateAudit.UpdatedAt));

        if (
            entity.UpdatedAt != null
            && propertyEntry.IsModified
            && !Equals(propertyEntry.CurrentValue, propertyEntry.OriginalValue)
        )
        {
            return;
        }

        if (ObjectPropertiesHelper.TrySetProperty(entity, nameof(IUpdateAudit.UpdatedAt), _getUtcNow))
        {
            propertyEntry.IsModified = true;
        }
    }

    private static void _TrySetUpdateAuditId(EntityEntry entry, ref ActorPair actor)
    {
        var byUser = entry.Entity as IUpdateAudit<UserId>;
        var byAccount = entry.Entity as IUpdateAudit<AccountId>;

        if (byUser is null && byAccount is null)
        {
            return;
        }

        var (currentUserId, currentAccountId) = actor.Resolve();

        if (currentUserId is null && currentAccountId is null)
        {
            return;
        }

        var propertyEntry = entry.Property(nameof(IUpdateAudit<>.UpdatedById));

        if (propertyEntry.IsModified && !Equals(propertyEntry.CurrentValue, propertyEntry.OriginalValue))
        {
            return;
        }

        if (byUser is not null && byUser.UpdatedById is null && currentUserId is not null)
        {
            if (ObjectPropertiesHelper.TrySetPropertyValue(byUser, nameof(IUpdateAudit<>.UpdatedById), currentUserId))
            {
                propertyEntry.IsModified = true;
            }

            return;
        }

        if (byAccount is not null && byAccount.UpdatedById is null && currentAccountId is not null)
        {
            if (
                ObjectPropertiesHelper.TrySetPropertyValue(
                    byAccount,
                    nameof(IUpdateAudit<>.UpdatedById),
                    currentAccountId
                )
            )
            {
                propertyEntry.IsModified = true;
            }
        }
    }

    private void _TrySetDeleteAudit(EntityEntry entry, ref ActorPair actor)
    {
        if (entry.Entity is not IDeleteAudit deleteAudit || !entry.Property(nameof(IDeleteAudit.IsDeleted)).IsModified)
        {
            return;
        }

        if (deleteAudit.IsDeleted)
        {
            _TrySetDeleteAuditDate(entry, deleteAudit);
            _TrySetDeleteAuditId(entry, ref actor);

            return;
        }

        ObjectPropertiesHelper.TrySetPropertyToNull(deleteAudit, nameof(IDeleteAudit.DeletedAt));

        if (_ImplementsGenericInterface(entry.Entity.GetType(), typeof(IDeleteAudit<>)))
        {
            ObjectPropertiesHelper.TrySetPropertyToNull(deleteAudit, nameof(IDeleteAudit<>.DeletedById));
        }
    }

    private void _TrySetDeleteAuditDate(EntityEntry entry, IDeleteAudit entity)
    {
        if (entity.DeletedAt == null || !entry.Property(nameof(IDeleteAudit.DeletedAt)).IsModified)
        {
            ObjectPropertiesHelper.TrySetProperty(entity, nameof(IDeleteAudit.DeletedAt), _getUtcNow);
        }
    }

    private static void _TrySetDeleteAuditId(EntityEntry entry, ref ActorPair actor)
    {
        var byUser = entry.Entity as IDeleteAudit<UserId>;
        var byAccount = entry.Entity as IDeleteAudit<AccountId>;

        if (byUser is null && byAccount is null)
        {
            return;
        }

        var (currentUserId, currentAccountId) = actor.Resolve();

        if (currentUserId is null && currentAccountId is null)
        {
            return;
        }

        var propertyEntry = entry.Property(nameof(IDeleteAudit<>.DeletedById));

        if (propertyEntry.IsModified && !Equals(propertyEntry.CurrentValue, propertyEntry.OriginalValue))
        {
            return;
        }

        if (byUser is not null && byUser.DeletedById is null && currentUserId is not null)
        {
            ObjectPropertiesHelper.TrySetPropertyValue(byUser, nameof(IDeleteAudit<>.DeletedById), currentUserId);
        }

        if (byAccount is not null && byAccount.DeletedById is null && currentAccountId is not null)
        {
            ObjectPropertiesHelper.TrySetPropertyValue(byAccount, nameof(IDeleteAudit<>.DeletedById), currentAccountId);
        }
    }

    private void _TrySetSuspendAudit(EntityEntry entry, ref ActorPair actor)
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
            _TrySetSuspendAuditId(entry, ref actor);

            return;
        }

        ObjectPropertiesHelper.TrySetPropertyToNull(suspendAudit, nameof(ISuspendAudit.SuspendedAt));

        if (_ImplementsGenericInterface(entry.Entity.GetType(), typeof(ISuspendAudit<>)))
        {
            ObjectPropertiesHelper.TrySetPropertyToNull(suspendAudit, nameof(ISuspendAudit<>.SuspendedById));
        }
    }

    private void _TrySetSuspendAuditDate(EntityEntry entry, ISuspendAudit entity)
    {
        if (entity.SuspendedAt == null || !entry.Property(nameof(ISuspendAudit.SuspendedAt)).IsModified)
        {
            ObjectPropertiesHelper.TrySetProperty(entity, nameof(ISuspendAudit.SuspendedAt), _getUtcNow);
        }
    }

    private static void _TrySetSuspendAuditId(EntityEntry entry, ref ActorPair actor)
    {
        var byUser = entry.Entity as ISuspendAudit<UserId>;
        var byAccount = entry.Entity as ISuspendAudit<AccountId>;

        if (byUser is null && byAccount is null)
        {
            return;
        }

        var (currentUserId, currentAccountId) = actor.Resolve();

        if (currentUserId is null && currentAccountId is null)
        {
            return;
        }

        var propertyEntry = entry.Property(nameof(ISuspendAudit<>.SuspendedById));

        if (propertyEntry.IsModified && !Equals(propertyEntry.CurrentValue, propertyEntry.OriginalValue))
        {
            return;
        }

        if (byUser is not null && byUser.SuspendedById is null && currentUserId is not null)
        {
            ObjectPropertiesHelper.TrySetPropertyValue(byUser, nameof(ISuspendAudit<>.SuspendedById), currentUserId);
        }

        if (byAccount is not null && byAccount.SuspendedById is null && currentAccountId is not null)
        {
            ObjectPropertiesHelper.TrySetPropertyValue(
                byAccount,
                nameof(ISuspendAudit<>.SuspendedById),
                currentAccountId
            );
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

    /// <summary>
    /// Stack-only at-most-once resolution of the current actor's identifier pair, shared by ref across the
    /// modified-entry stampers. Resolution happens on the first <see cref="Resolve"/> call — an entry whose id
    /// stampers all bail out before consuming the pair never touches <see cref="ICurrentUser"/> at all.
    /// </summary>
    private struct ActorPair(ICurrentUser currentUser)
    {
        private ICurrentUser? _pending = currentUser;
        private UserId? _userId;
        private AccountId? _accountId;

        public (UserId? UserId, AccountId? AccountId) Resolve()
        {
            if (_pending is { } user)
            {
                _userId = user.UserId;
                _accountId = user.AccountId;
                _pending = null;
            }

            return (_userId, _accountId);
        }
    }
}
