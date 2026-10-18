// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Context;
using Headless.Domain;
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
    /// <summary>Stamps audit fields on the entry based on its current <see cref="EntityState"/>.</summary>
    /// <param name="entry">The tracked entity entry to audit.</param>
    /// <param name="context">The per-save scratchpad (tenant id unused by this processor).</param>
    public void Process(EntityEntry entry, HeadlessSaveEntryContext context)
    {
        switch (entry.State)
        {
            case EntityState.Added:
                _TrySetCreateAudit(entry, context);
                break;
            case EntityState.Modified:
                // ICurrentUser implementations re-scan and re-parse claims on every UserId/AccountId read, so every
                // stamper of the save shares one resolution, cached on the save context. Resolution stays lazy inside
                // the id stampers: a save whose stampers all bail out performs zero claim scans.
                if (entry.Entity is IUpdateAudit or IDeleteAudit)
                {
                    var actor = new ActorPair(context, currentUser);

                    _TrySetUpdateAudit(entry, actor);
                    _TrySetDeleteAudit(entry, actor);
                }

                break;
        }
    }

    private void _TrySetCreateAudit(EntityEntry entry, HeadlessSaveEntryContext context)
    {
        if (entry.Entity is not ICreateAudit entity)
        {
            return;
        }

        if (entity.CreatedAt == default)
        {
            HeadlessEntryStamping.SetValue(entry, nameof(ICreateAudit.CreatedAt), timeProvider.GetUtcNow());
        }

        _TrySetCreateAuditId(entry, new ActorPair(context, currentUser));
    }

    private static void _TrySetCreateAuditId(EntityEntry entry, ActorPair actor)
    {
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

        if (byUser is not null && byUser.CreatedById == null && actor.Resolve().UserId is { } currentUserId)
        {
            HeadlessEntryStamping.SetValue(entry, nameof(ICreateAudit<>.CreatedById), currentUserId);

            return;
        }

        if (byAccount is not null && byAccount.CreatedById == null && actor.Resolve().AccountId is { } currentAccountId)
        {
            HeadlessEntryStamping.SetValue(entry, nameof(ICreateAudit<>.CreatedById), currentAccountId);
        }
    }

    private void _TrySetUpdateAudit(EntityEntry entry, ActorPair actor)
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
            actor
        );
    }

    private void _TrySetDeleteAudit(EntityEntry entry, ActorPair actor)
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
                    actor
                );
                break;
            case FlagTransition.Lowered:
                _StampTransition(
                    entry,
                    nameof(IDeleteAudit.RestoredAt),
                    nameof(IDeleteAudit<>.RestoredById),
                    kind,
                    StampMode.Transition,
                    actor
                );
                break;
            case FlagTransition.MarkedWhileRaised:
                _StampTransition(
                    entry,
                    nameof(IDeleteAudit.DeletedAt),
                    nameof(IDeleteAudit<>.DeletedById),
                    kind,
                    StampMode.FillMissing,
                    actor
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
        ActorPair actor
    )
    {
        var timestamp = entry.Property(timestampName);
        var timestampSetBySave = _IsExplicitlySet(timestamp);

        if (mode is StampMode.FillMissing ? timestamp.CurrentValue is null : !timestampSetBySave)
        {
            timestamp.CurrentValue = timeProvider.GetUtcNow();
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
    /// Lazy access to the current actor's identifier pair, resolved at most once per save through the save context.
    /// An entry whose id stampers all bail out before calling <see cref="Resolve"/> never touches
    /// <see cref="ICurrentUser"/>.
    /// </summary>
    private readonly struct ActorPair(HeadlessSaveEntryContext context, ICurrentUser currentUser)
    {
        public (UserId? UserId, AccountId? AccountId) Resolve() => context.GetActor(currentUser);
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
