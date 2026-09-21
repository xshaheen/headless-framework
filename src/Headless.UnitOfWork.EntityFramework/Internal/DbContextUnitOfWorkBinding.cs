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
/// <see cref="HeadlessDbContextUnitOfWorkExtensions" />.
/// </summary>
internal static class DbContextUnitOfWorkBinding
{
    public const string AlreadyBoundMessage =
        "This DbContext already carries an active unit of work. Run the block with RunAsync(db, …) to join it, or pass that unit to the code that needs it (read it with db.UnitOfWork()), instead of beginning a second one on the same context.";

    public const string ConnectionBoundMessage =
        "The connection beneath this DbContext already carries an active unit of work that was begun on the connection itself (a raw-ADO BeginAsync, Enlist, or RunAsync). An EF block can neither own nor join it, because the context would begin a second transaction on that connection. Begin the EF unit of work first and let the raw-ADO code join it with RunAsync(connection, …), or run this work as a raw-ADO block on that unit.";

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
    /// Gets the unit bound to <paramref name="db" /> when it can still host work; a terminal unit, or an owned
    /// unit whose transaction already ended, is ignored and evicted.
    /// </summary>
    public static bool TryGet(DbContext db, out IUnitOfWork unit) => _Binding.TryGet(db, out unit);

    /// <summary>
    /// Throws the catalogued refusal when <paramref name="db" />, or the connection beneath it, already carries
    /// a live unit: two units cannot own one transaction, and the callee is meant to join or be handed it. The
    /// two cases carry different remedies, because only the context-bound unit can be joined from EF.
    /// </summary>
    public static void ThrowIfBound(DbContext db)
    {
        if (TryGet(db, out _))
        {
            throw new InvalidOperationException(AlreadyBoundMessage);
        }

        ThrowIfConnectionBound(db);
    }

    /// <summary>
    /// Throws the EF-specific refusal when the connection beneath <paramref name="db" /> is bound by a unit the
    /// context does not carry: one begun through a raw-ADO provider, which an EF block cannot join.
    /// </summary>
    public static void ThrowIfConnectionBound(DbContext db)
    {
        if (DbConnectionUnitOfWorkBinding.TryGet(db.Database.GetDbConnection(), out _))
        {
            throw new InvalidOperationException(ConnectionBoundMessage);
        }
    }
}
