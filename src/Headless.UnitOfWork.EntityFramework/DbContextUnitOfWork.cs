// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace

namespace Headless.UnitOfWork;

/// <summary>
/// The <see cref="DbContext" /> → <see cref="IUnitOfWork" /> binding that <c>BeginAsync(db)</c>,
/// <c>Enlist(db, transaction)</c>, and <c>RunAsync(db, …)</c> record. With no ambient unit of work, the context
/// is how code that was handed only the context — the save pipeline, a domain-event handler, a repository —
/// reaches the unit that owns its transaction.
/// </summary>
[PublicAPI]
public static class DbContextUnitOfWork
{
    extension(DbContext db)
    {
        /// <summary>
        /// Gets the unit of work bound to this context while it is still <see cref="UnitOfWorkState.Active" />,
        /// or <see langword="null" /> when none is: no unit was begun on the context, or the bound unit reached a
        /// terminal state and was evicted.
        /// </summary>
        /// <remarks>
        /// This is the unit to enlist in from inside a save — <c>db.UnitOfWork()?.Outbox.PublishAsync(…)</c> in
        /// a domain-event handler — and the unit a callee should be handed rather than beginning a second one on
        /// the same context.
        /// </remarks>
        /// <returns>The active bound unit, or <see langword="null" />.</returns>
        /// <exception cref="ArgumentNullException">The context is <see langword="null" />.</exception>
        public IUnitOfWork? UnitOfWork()
        {
            Argument.IsNotNull(db);

            return DbContextUnitOfWorkBinding.TryGet(db, out var unit) ? unit : null;
        }
    }
}
