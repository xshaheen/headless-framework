// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.UnitOfWork;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore;

/// <summary>
/// The <see cref="DbContext" /> → <see cref="IUnitOfWork" /> binding that <c>BeginAsync(db)</c>,
/// <c>Enlist(db, transaction)</c>, and <c>RunAsync(db, …)</c> record. With no ambient unit of work, the context
/// is how code that was handed only the context — the save pipeline, a domain-event handler, a repository —
/// reaches the unit that owns its transaction. Declared in the augmented type's namespace so the accessor is
/// discoverable wherever a <see cref="DbContext" /> is in scope.
/// </summary>
[PublicAPI]
public static class HeadlessDbContextUnitOfWorkExtensions
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
        /// a domain-event handler — and the unit a callee is handed, or joins through <c>RunAsync(db, …)</c>,
        /// rather than beginning a second one on the same context. The same unit is bound to the connection
        /// beneath the context (<c>db.Database.GetDbConnection().UnitOfWork()</c>), so a raw-ADO helper given
        /// that connection reaches it too.
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
