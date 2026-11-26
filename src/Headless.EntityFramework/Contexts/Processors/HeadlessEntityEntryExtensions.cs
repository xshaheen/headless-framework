// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Domain;
using AccountId = Headless.Primitives.AccountId;
using UserId = Headless.Primitives.UserId;

namespace Microsoft.EntityFrameworkCore.ChangeTracking;

/// <summary>Extension methods for <see cref="EntityEntry"/>.</summary>
[PublicAPI]
public static class HeadlessEntityEntryExtensions
{
    /// <summary>
    /// Inserts the entry's entity without a creator: the save pipeline stamps <c>ICreateAudit.CreatedAt</c> as usual
    /// but leaves <c>CreatedById</c> as the entity holds it, instead of filling it from <c>ICurrentUser</c>.
    /// </summary>
    /// <typeparam name="TEntry">The entry type, kept so the call chains after <c>DbSet&lt;T&gt;.Add</c>.</typeparam>
    /// <param name="entry">The entry of an entity that implements <c>ICreateAudit&lt;UserId&gt;</c> or
    /// <c>ICreateAudit&lt;AccountId&gt;</c>.</param>
    /// <returns>The same <paramref name="entry"/>.</returns>
    /// <remarks>
    /// Use it for rows that no signed-in user authored, such as a bulk import that runs inside a request. The mark
    /// belongs to the entity instance, not to the context or the current scope, so it holds through any number of
    /// saves and an explicit unit of work, and it never changes how other entities of the same save are stamped. Update,
    /// delete, and restore stamps are unaffected.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="entry"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The entity has no <c>CreatedById</c> to leave unset.</exception>
    public static TEntry SkipCreatedByIdStamp<TEntry>(this TEntry entry)
        where TEntry : EntityEntry
    {
        Argument.IsNotNull(entry);
        Argument.IsTrue(
            entry.Entity is ICreateAudit<UserId> or ICreateAudit<AccountId>,
            $"{entry.Metadata.DisplayName()} does not implement ICreateAudit<UserId> or ICreateAudit<AccountId>.",
            nameof(entry)
        );

        Headless.EntityFramework.HeadlessCreatedByIdStampSkips.Add(entry.Entity);

        return entry;
    }
}
