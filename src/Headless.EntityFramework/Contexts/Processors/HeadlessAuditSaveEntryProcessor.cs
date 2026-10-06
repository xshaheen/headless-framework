// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Context;
using Headless.Domain;
using Headless.EntityFramework.Contexts;
using Headless.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using AccountId = Headless.Primitives.AccountId;
using UserId = Headless.Primitives.UserId;

namespace Headless.EntityFramework;

/// <summary>
/// Save-entry processor that stamps audit timestamps and actor identifiers on entities that implement
/// the Headless audit interfaces.
/// </summary>
/// <remarks>
/// On <c>Added</c> entries it sets <c>ICreateAudit.CreatedAt</c> (if not already set) and
/// <c>CreatedById</c> (resolved from <c>ICurrentUser</c>, skipped if already set or the user is
/// anonymous). On <c>Modified</c> entries it stamps <c>IUpdateAudit.UpdatedAt</c> and <c>UpdatedById</c>, and
/// on an <c>IsDeleted</c> transition it stamps the matching delete or restore fields. Fields of the opposite
/// transition are kept as history. A non-null value the save already set explicitly (for example through a
/// transition method) wins over the stamp. An update or transition with no actor, given or resolved from
/// <c>ICurrentUser</c>, records a null actor id rather than keep the previous actor.
/// </remarks>
[PublicAPI]
public sealed class HeadlessAuditSaveEntryProcessor(TimeProvider timeProvider, ICurrentUser currentUser)
    : IHeadlessSaveEntryProcessor
{
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
                // stampers share one at-most-once resolution instead of paying for up to two. Resolution stays
                // lazy inside the id stampers: an audited entity whose branches do not fire (an IDeleteAudit
                // modified on an unrelated property, a non-generic IUpdateAudit) performs zero claim scans.
                if (entry.Entity is IUpdateAudit or IDeleteAudit)
                {
                    var actor = new ActorPair(currentUser);

                    _TrySetUpdateAudit(entry, ref actor);
                    _TrySetDeleteAudit(entry, ref actor);
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
        if (entry.Entity is not IUpdateAudit)
        {
            return;
        }

        // Every modified save is a new update, so the previous updater is replaced, not kept: with no actor the update
        // records null rather than attribute this change to the previous updater.
        _StampTransition(
            entry,
            nameof(IUpdateAudit.UpdatedAt),
            nameof(IUpdateAudit<>.UpdatedById),
            new ActorKind(entry.Entity is IUpdateAudit<UserId>, entry.Entity is IUpdateAudit<AccountId>),
            StampMode.Transition,
            ref actor
        );
    }

    private void _TrySetDeleteAudit(EntityEntry entry, ref ActorPair actor)
    {
        if (entry.Entity is not IDeleteAudit)
        {
            return;
        }

        var kind = new ActorKind(entry.Entity is IDeleteAudit<UserId>, entry.Entity is IDeleteAudit<AccountId>);

        switch (_GetTransition(entry.Property(nameof(IDeleteAudit.IsDeleted))))
        {
            case FlagTransition.Raised:
                _StampTransition(
                    entry,
                    nameof(IDeleteAudit.DeletedAt),
                    nameof(IDeleteAudit<>.DeletedById),
                    kind,
                    StampMode.Transition,
                    ref actor
                );
                break;
            case FlagTransition.Lowered:
                _StampTransition(
                    entry,
                    nameof(IDeleteAudit.RestoredAt),
                    nameof(IDeleteAudit<>.RestoredById),
                    kind,
                    StampMode.Transition,
                    ref actor
                );
                break;
            case FlagTransition.MarkedWhileRaised:
                _StampTransition(
                    entry,
                    nameof(IDeleteAudit.DeletedAt),
                    nameof(IDeleteAudit<>.DeletedById),
                    kind,
                    StampMode.FillMissing,
                    ref actor
                );
                break;
        }
    }

    private static FlagTransition _GetTransition(PropertyEntry flag)
    {
        if (!flag.IsModified)
        {
            return FlagTransition.None;
        }

        var isRaised = flag.CurrentValue is true;
        var wasRaised = flag.OriginalValue is true;

        if (isRaised != wasRaised)
        {
            return isRaised ? FlagTransition.Raised : FlagTransition.Lowered;
        }

        // Attaching a detached entity with Update() marks every property modified with no original snapshot, so the
        // transition is unknown. Fill only what is missing for the current state rather than overwrite history.
        return isRaised ? FlagTransition.MarkedWhileRaised : FlagTransition.None;
    }

    /// <summary>
    /// Stamps the timestamp and actor id of one audit change. <see cref="StampMode.Transition"/> replaces a value left
    /// over from an earlier change unless this save set it explicitly; <see cref="StampMode.FillMissing"/> only fills a
    /// missing value.
    /// </summary>
    private void _StampTransition(
        EntityEntry entry,
        string timestampName,
        string actorIdName,
        ActorKind kind,
        StampMode mode,
        ref ActorPair actor
    )
    {
        var timestamp = entry.Property(timestampName);
        var timestampSetBySave = _IsExplicitlySet(timestamp);

        if (mode is StampMode.FillMissing ? timestamp.CurrentValue is null : !timestampSetBySave)
        {
            timestamp.CurrentValue = _getUtcNow();
        }

        if (!kind.IsUser && !kind.IsAccount)
        {
            return;
        }

        var actorId = entry.Property(actorIdName);

        if (mode is StampMode.FillMissing ? actorId.CurrentValue is not null : _IsExplicitlySet(actorId))
        {
            return;
        }

        // A transition method that recorded the time also wrote the actor it was given. An id it left unchanged is
        // that same actor, not a leftover, so it is kept.
        if (mode is StampMode.Transition && timestampSetBySave && actorId.CurrentValue is not null)
        {
            return;
        }

        var (currentUserId, currentAccountId) = actor.Resolve();
        object? current = kind.IsUser ? currentUserId : null;
        current ??= kind.IsAccount ? currentAccountId : null;

        if (current is null && mode is StampMode.FillMissing)
        {
            return;
        }

        // A transition with no resolved actor records null rather than keep the actor of the previous transition,
        // which would attribute this one to the wrong account. EF fixes up an unchanged navigation from the new
        // foreign key value on the final change detection.
        if (!Equals(actorId.CurrentValue, current))
        {
            actorId.CurrentValue = current;
        }
    }

    private static bool _IsExplicitlySet(PropertyEntry property)
    {
        return property.IsModified
            && property.CurrentValue is not null
            && !Equals(property.CurrentValue, property.OriginalValue);
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

    private readonly record struct ActorKind(bool IsUser, bool IsAccount);

    private enum StampMode
    {
        // An update, delete, or restore: replace leftovers, and record null when no actor resolves.
        Transition = 0,

        // A flag already raised with no known transition: fill only missing values.
        FillMissing = 1,
    }

    private enum FlagTransition
    {
        None = 0,
        Raised = 1,
        Lowered = 2,
        MarkedWhileRaised = 3,
    }
}
