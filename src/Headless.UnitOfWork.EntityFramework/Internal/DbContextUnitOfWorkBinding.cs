// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.UnitOfWork;
using Headless.UnitOfWork.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace

namespace Headless.UnitOfWork;

/// <summary>
/// The <see cref="DbContext" /> → <see cref="IUnitOfWork" /> binding: <c>BeginAsync(db)</c>, <c>Enlist(db, transaction)</c>,
/// and <c>RunAsync(db, …)</c> record the unit so the save pipeline, a domain-event handler, or a nested
/// <c>RunAsync(db, …)</c> reaches the unit that owns the context's transaction. Exposed to consumers through
/// <see cref="HeadlessDbContextUnitOfWorkExtensions" />.
/// </summary>
/// <remarks>
/// A second context built over the connection of a context that carries an EF unit joins that unit on lookup:
/// it adopts the unit's transaction (<c>UseTransaction</c>) and hands back the owner's handle, which is how two
/// <see cref="DbContext" /> types over one database commit atomically. The join is derived from the connection
/// binding on every lookup rather than recorded against the second context, so a pooled or reused context never
/// carries a unit it no longer shares a connection with; the adopted transaction is released when the unit ends.
/// </remarks>
internal static class DbContextUnitOfWorkBinding
{
    public const string AlreadyBoundMessage =
        "This DbContext already carries an active unit of work. Run the block with RunAsync(db, …) to join it, or pass that unit to the code that needs it (read it with db.UnitOfWork()), instead of beginning a second one on the same context.";

    public const string SharedConnectionBoundMessage =
        "The connection beneath this DbContext already carries an active unit of work begun on another DbContext over the same connection. Run the block with RunAsync(db, …) to join it, or read it with db.UnitOfWork(), instead of beginning a second one on the same connection.";

    public const string ConnectionBoundMessage =
        "The connection beneath this DbContext already carries an active unit of work that was begun on the connection itself (a raw-ADO BeginAsync, Enlist, or RunAsync). An EF block can neither own nor join it, because the context would begin a second transaction on that connection. Begin the EF unit of work first and let the raw-ADO code join it with RunAsync(connection, …), or run this work as a raw-ADO block on that unit.";

    public const string ForeignTransactionMessage =
        "The connection beneath this DbContext carries an active unit of work, but the context already uses a different transaction, so it cannot join that unit. Do not begin a transaction on a context that shares a unit of work's connection; let it join the unit through RunAsync(db, …) or db.UnitOfWork().";

    private static readonly UnitOfWorkBinding<DbContext> _Binding = new();

    /// <summary>
    /// Records the unit bound to <paramref name="db" />, and to the connection beneath it, so a block run on
    /// that connection (<c>RunAsync(connection, …)</c> from a raw-ADO helper, or <c>RunAsync(other, …)</c> on
    /// another context over the same connection) joins the same unit.
    /// </summary>
    public static void Bind(DbContext db, IUnitOfWork unit)
    {
        _Binding.Bind(db, unit);
        DbConnectionUnitOfWorkBinding.Bind(db.Database.GetDbConnection(), unit);
    }

    /// <summary>
    /// Gets the unit that owns <paramref name="db" />'s transaction: the unit bound to the context, or the EF unit
    /// bound to the connection beneath it, which the context joins by adopting its transaction. A terminal unit,
    /// or an owned unit whose transaction already ended, is ignored and evicted.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The connection carries an EF unit but the context already uses a different transaction.
    /// </exception>
    public static bool TryGet(DbContext db, out IUnitOfWork unit)
    {
        return _Binding.TryGet(db, out unit) || _TryJoinConnectionUnit(db, out unit);
    }

    /// <summary>
    /// Throws the catalogued refusal when <paramref name="db" />, or the connection beneath it, already carries
    /// a live unit: two units cannot own one transaction, and the callee is meant to join or be handed it. The
    /// cases carry different remedies, because only an EF unit can be joined from EF. Never adopts a transaction:
    /// a refused begin must leave the context as it found it.
    /// </summary>
    public static void ThrowIfBound(DbContext db)
    {
        if (_Binding.TryGet(db, out _))
        {
            throw new InvalidOperationException(AlreadyBoundMessage);
        }

        if (DbConnectionUnitOfWorkBinding.TryGet(db.Database.GetDbConnection(), out var connectionUnit))
        {
            throw new InvalidOperationException(
                connectionUnit.Resource is EfUnitOfWorkResource ? SharedConnectionBoundMessage : ConnectionBoundMessage
            );
        }
    }

    /// <summary>
    /// Throws the EF-specific refusal when the connection beneath <paramref name="db" /> is bound by a unit the
    /// context cannot join: one begun through a raw-ADO provider.
    /// </summary>
    public static void ThrowIfConnectionBound(DbContext db)
    {
        if (DbConnectionUnitOfWorkBinding.TryGet(db.Database.GetDbConnection(), out _))
        {
            throw new InvalidOperationException(ConnectionBoundMessage);
        }
    }

    private static bool _TryJoinConnectionUnit(DbContext db, out IUnitOfWork unit)
    {
        unit = null!;

        // Only an EF unit is joinable from here: a raw-ADO unit is refused by the callers with its own remedy.
        if (
            !DbConnectionUnitOfWorkBinding.TryGet(db.Database.GetDbConnection(), out var connectionUnit)
            || connectionUnit.Resource is not EfUnitOfWorkResource { IsTransactionCompleted: false } resource
        )
        {
            return false;
        }

        var transaction = resource.Transaction;
        var current = db.Database.CurrentTransaction;

        if (current is null)
        {
            // Registered before adopting, so a unit that ends between the two still releases the context.
            connectionUnit
                .GetOrAdd(transaction, static (_, transaction) => new SharedConnectionContexts(transaction))
                .Add(db);
#pragma warning disable MA0045 // The lookup backs the synchronous db.UnitOfWork() accessor and SaveChanges; adopting a transaction on an already-open connection does no I/O.
            db.Database.UseTransaction(transaction);
#pragma warning restore MA0045
        }
        else if (!ReferenceEquals(current.GetDbTransaction(), transaction))
        {
            throw new InvalidOperationException(ForeignTransactionMessage);
        }

        unit = connectionUnit;

        return true;
    }

    /// <summary>
    /// The contexts that adopted a unit's transaction through a shared connection. Unit-local state, so it is
    /// disposed once the unit reaches a terminal state — after the owner committed or rolled back — and releases
    /// each context's reference to the finished transaction, leaving a context that outlives the unit free to
    /// begin fresh.
    /// </summary>
    private sealed class SharedConnectionContexts(DbTransaction transaction) : IDisposable
    {
        private readonly Lock _gate = new();
        private readonly List<DbContext> _contexts = [];

        public void Add(DbContext db)
        {
            lock (_gate)
            {
                _contexts.Add(db);
            }
        }

        public void Dispose()
        {
            DbContext[] contexts;

            lock (_gate)
            {
                contexts = [.. _contexts];
                _contexts.Clear();
            }

            foreach (var db in contexts)
            {
                try
                {
                    // Only release the transaction this unit handed out; a context that moved on keeps its own.
                    if (ReferenceEquals(db.Database.CurrentTransaction?.GetDbTransaction(), transaction))
                    {
                        db.Database.UseTransaction(transaction: null);
                    }
                }
                catch (ObjectDisposedException)
                {
                    // The context's scope ended before the unit did: there is no reference left to release.
                }
            }
        }
    }
}
