// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.UnitOfWork;
using Headless.UnitOfWork.Internal;
using Microsoft.EntityFrameworkCore;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace

namespace Headless.UnitOfWork;

/// <summary>
/// The <see cref="DbContext" /> → <see cref="IUnitOfWork" /> binding: <c>BeginAsync(db)</c>, <c>Enlist(db, transaction)</c>,
/// and <c>RunAsync(db, …)</c> record the unit so the save pipeline, a domain-event handler, or a nested
/// <c>RunAsync(db, …)</c> reaches the unit that owns the context's transaction. Exposed to consumers through
/// <see cref="DbContextUnitOfWork" />.
/// </summary>
internal static class DbContextUnitOfWorkBinding
{
    public const string AlreadyBoundMessage =
        "This DbContext already carries an active unit of work. Run the block with RunAsync(db, …) to join it, or pass that unit to the code that needs it (read it with db.UnitOfWork()), instead of beginning a second one on the same context.";

    private static readonly UnitOfWorkBinding<DbContext> _Binding = new();

    /// <summary>
    /// Records the unit bound to <paramref name="db" />, and to the connection beneath it, so a block run on
    /// that connection (<c>RunAsync(connection, …)</c> from a raw-ADO helper) joins the same unit.
    /// </summary>
    public static void Bind(DbContext db, IUnitOfWork unit)
    {
        _Binding.Bind(db, unit);
        DbConnectionUnitOfWorkBinding.Bind(db.Database.GetDbConnection(), unit);
    }

    /// <summary>
    /// Gets the unit bound to <paramref name="db" /> when it is still <see cref="UnitOfWorkState.Active" />;
    /// a terminal unit is ignored and evicted.
    /// </summary>
    public static bool TryGet(DbContext db, out IUnitOfWork unit) => _Binding.TryGet(db, out unit);

    /// <summary>
    /// Throws the catalogued refusal when <paramref name="db" />, or the connection beneath it, already carries
    /// a live unit: two units cannot own one transaction, and the callee is meant to join or be handed it.
    /// </summary>
    public static void ThrowIfBound(DbContext db)
    {
        if (TryGet(db, out _))
        {
            throw new InvalidOperationException(AlreadyBoundMessage);
        }

        DbConnectionUnitOfWorkBinding.ThrowIfBound(db.Database.GetDbConnection());
    }
}
