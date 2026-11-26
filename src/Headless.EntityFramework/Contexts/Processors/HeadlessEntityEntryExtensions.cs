// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Domain;
using Headless.EntityFramework;
using AccountId = Headless.Primitives.AccountId;
using UserId = Headless.Primitives.UserId;

namespace Microsoft.EntityFrameworkCore.ChangeTracking;

/// <summary>
/// Extension methods for <see cref="EntityEntry"/> that keep the save pipeline from filling an audit actor id from
/// <c>ICurrentUser</c>, for changes no signed-in user made, such as a bulk import that runs inside a request.
/// </summary>
/// <remarks>
/// Each mark belongs to the entity instance, not to the context or the current scope, and never changes how other
/// entities of the same save are stamped. The matching timestamp is still stamped. A value the save sets explicitly,
/// for example through <c>Update(now, byId, by)</c>, still wins.
/// </remarks>
[PublicAPI]
public static class HeadlessEntityEntryExtensions
{
    /// <summary>
    /// Inserts the entry's entity without a creator: the save stamps <c>CreatedAt</c> but leaves <c>CreatedById</c> as
    /// the entity holds it.
    /// </summary>
    /// <typeparam name="TEntry">The entry type, kept so the call chains after <c>DbSet&lt;T&gt;.Add</c>.</typeparam>
    /// <param name="entry">The entry of an entity that implements <c>ICreateAudit&lt;UserId&gt;</c> or
    /// <c>ICreateAudit&lt;AccountId&gt;</c>.</param>
    /// <returns>The same <paramref name="entry"/>.</returns>
    /// <remarks>
    /// An entity is created once, so the mark holds for every insert of this instance, through any number of failed
    /// saves and an explicit unit of work.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="entry"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The entity has no <c>CreatedById</c>.</exception>
    public static TEntry SkipCreatedByIdStamp<TEntry>(this TEntry entry)
        where TEntry : EntityEntry
    {
        Argument.IsNotNull(entry);
        Argument.IsTrue(
            entry.Entity is ICreateAudit<UserId> or ICreateAudit<AccountId>,
            _NotImplemented(entry, "ICreateAudit"),
            nameof(entry)
        );

        HeadlessActorStampSkips.Add(entry.Entity, HeadlessActorStamps.CreatedById);

        return entry;
    }

    /// <summary>
    /// Records the next update of the entry's entity with no updater: the next save that stamps <c>UpdatedAt</c>
    /// records a null <c>UpdatedById</c>.
    /// </summary>
    /// <typeparam name="TEntry">The entry type, kept so the call chains.</typeparam>
    /// <param name="entry">The entry of an entity that implements <c>IUpdateAudit&lt;UserId&gt;</c> or
    /// <c>IUpdateAudit&lt;AccountId&gt;</c>.</param>
    /// <returns>The same <paramref name="entry"/>.</returns>
    /// <remarks>
    /// The mark covers one save: the first save that stamps the update removes it, whether that save commits or
    /// throws. A save that is called again after a failure stamps the current user unless you mark the entry again.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="entry"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The entity has no <c>UpdatedById</c>.</exception>
    public static TEntry SkipUpdatedByIdStamp<TEntry>(this TEntry entry)
        where TEntry : EntityEntry
    {
        Argument.IsNotNull(entry);
        Argument.IsTrue(
            entry.Entity is IUpdateAudit<UserId> or IUpdateAudit<AccountId>,
            _NotImplemented(entry, "IUpdateAudit"),
            nameof(entry)
        );

        HeadlessActorStampSkips.Add(entry.Entity, HeadlessActorStamps.UpdatedById);

        return entry;
    }

    /// <summary>
    /// Records the next soft delete of the entry's entity with no deleter: the next save that stamps <c>DeletedAt</c>
    /// records a null <c>DeletedById</c>.
    /// </summary>
    /// <typeparam name="TEntry">The entry type, kept so the call chains.</typeparam>
    /// <param name="entry">The entry of an entity that implements <c>IDeleteAudit&lt;UserId&gt;</c> or
    /// <c>IDeleteAudit&lt;AccountId&gt;</c>.</param>
    /// <returns>The same <paramref name="entry"/>.</returns>
    /// <remarks>
    /// The mark covers one save: the first save that stamps the deletion removes it, whether that save commits or
    /// throws. A soft delete is also an update, so mark the entry with <see cref="SkipUpdatedByIdStamp{TEntry}"/> too
    /// to leave <c>UpdatedById</c> null.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="entry"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The entity has no <c>DeletedById</c>.</exception>
    public static TEntry SkipDeletedByIdStamp<TEntry>(this TEntry entry)
        where TEntry : EntityEntry
    {
        Argument.IsNotNull(entry);
        _EnsureDeleteAudit(entry);
        HeadlessActorStampSkips.Add(entry.Entity, HeadlessActorStamps.DeletedById);

        return entry;
    }

    /// <summary>
    /// Records the next restore of the entry's entity with no restorer: the next save that stamps <c>RestoredAt</c>
    /// records a null <c>RestoredById</c>.
    /// </summary>
    /// <typeparam name="TEntry">The entry type, kept so the call chains.</typeparam>
    /// <param name="entry">The entry of an entity that implements <c>IDeleteAudit&lt;UserId&gt;</c> or
    /// <c>IDeleteAudit&lt;AccountId&gt;</c>.</param>
    /// <returns>The same <paramref name="entry"/>.</returns>
    /// <remarks>
    /// The mark covers one save: the first save that stamps the restoration removes it, whether that save commits or
    /// throws. A restore is also an update, so mark the entry with <see cref="SkipUpdatedByIdStamp{TEntry}"/> too to
    /// leave <c>UpdatedById</c> null.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="entry"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The entity has no <c>RestoredById</c>.</exception>
    public static TEntry SkipRestoredByIdStamp<TEntry>(this TEntry entry)
        where TEntry : EntityEntry
    {
        Argument.IsNotNull(entry);
        _EnsureDeleteAudit(entry);
        HeadlessActorStampSkips.Add(entry.Entity, HeadlessActorStamps.RestoredById);

        return entry;
    }

    private static void _EnsureDeleteAudit(EntityEntry entry)
    {
        Argument.IsTrue(
            entry.Entity is IDeleteAudit<UserId> or IDeleteAudit<AccountId>,
            _NotImplemented(entry, "IDeleteAudit"),
            nameof(entry)
        );
    }

    private static string _NotImplemented(EntityEntry entry, string audit)
    {
        return $"{entry.Metadata.DisplayName()} does not implement {audit}<UserId> or {audit}<AccountId>.";
    }
}
