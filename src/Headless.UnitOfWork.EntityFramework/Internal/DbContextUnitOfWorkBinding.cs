// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Checks;
using Headless.UnitOfWork;
using Headless.UnitOfWork.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

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
    /// Drops the unit bound to <paramref name="db" /> when the context is disposed, and the same unit's binding of
    /// the connection beneath it when EF owns that connection. A disposed context cannot host the unit's work, and a
    /// pooled instance is leased again: a handle its caller never completed would otherwise make every later lease
    /// refuse to begin or save with "already carries an active unit of work". The unit itself is left alone, so
    /// whoever still holds the handle can finish it. A connection the application supplied may still carry the unit
    /// for raw-ADO work after the context is gone, so its binding stays.
    /// </summary>
    public static void Unbind(DbContext db)
    {
        if (_Binding.Unbind(db) is not { } unit)
        {
            return;
        }

        var configuredConnection = RelationalOptionsExtension.Extract(db.GetService<IDbContextOptions>()).Connection;

        if (configuredConnection is null)
        {
            DbConnectionUnitOfWorkBinding.Unbind(db.Database.GetDbConnection(), unit);
        }
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
        if (_Binding.TryGet(db, out unit))
        {
            return true;
        }

        if (
            !DbConnectionUnitOfWorkBinding.TryGet(db.Database.GetDbConnection(), out var connectionUnit)
            || connectionUnit.Resource is not EfUnitOfWorkResource { IsTransactionCompleted: false } resource
        )
        {
            return false;
        }

        _JoinConnectionUnit(db, connectionUnit, resource.Transaction);
        unit = connectionUnit;

        return true;
    }

    /// <summary>
    /// Resolves the unit for <c>RunAsync</c>. A stale owned unit is abandoned before a new begin, but an
    /// observed unit that still awaits completion goes to the runner, which refuses to join its ended transaction.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The connection carries a raw-ADO unit or an EF unit on a different transaction than this context.
    /// </exception>
    public static async ValueTask<IUnitOfWork?> TryGetForRunAsync(DbContext db)
    {
        if (await _Binding.TryGetAsync(db).ConfigureAwait(false) is { } own)
        {
            return own;
        }

        var connectionUnit = await DbConnectionUnitOfWorkBinding
            .TryGetAsync(db.Database.GetDbConnection())
            .ConfigureAwait(false);

        if (connectionUnit is null)
        {
            return null;
        }

        if (connectionUnit.Resource is not EfUnitOfWorkResource resource)
        {
            throw new InvalidOperationException(ConnectionBoundMessage);
        }

        if (!resource.IsTransactionCompleted)
        {
            _JoinConnectionUnit(db, connectionUnit, resource.Transaction);
        }

        return connectionUnit;
    }

    /// <summary>
    /// Whether <paramref name="db" /> reaches its unit only through the connection it shares with the context
    /// the unit was bound to, rather than through a binding of its own. The save pipeline replays its own save
    /// without re-running the handlers that saved through such a sibling, so it needs to tell the two apart.
    /// Leaves transaction adoption to the binding lookup used to join the unit.
    /// </summary>
    public static bool IsJoinedThroughConnection(DbContext db)
    {
        return !_Binding.TryGet(db, out _)
            && DbConnectionUnitOfWorkBinding.TryGet(db.Database.GetDbConnection(), out var connectionUnit)
            && connectionUnit.Resource is EfUnitOfWorkResource;
    }

    /// <summary>
    /// Whether a context that joined <paramref name="unit" /> through the shared connection still tracks an
    /// entity. The unit's rollback undoes such a context's rows but not its tracker, and the replay guard resets
    /// only the context the block was run on, so a replayed block would read the failed attempt's edits back from
    /// that tracker: a relative update would apply twice and an absolute one would be skipped as already saved.
    /// A sibling that tracked nothing (a no-tracking read, a set-based update) leaves nothing to replay over.
    /// </summary>
    public static bool HasJoinedContextWithTrackedEntities(IUnitOfWork unit)
    {
        // A unit the block already ended accepts no state lookup, and is never replayed.
        if (
            unit.State is not UnitOfWorkState.Active
            || unit.Resource is not EfUnitOfWorkResource { IsTransactionCompleted: false } resource
        )
        {
            return false;
        }

        return unit.GetOrAdd(resource.Transaction, static (_, transaction) => new SharedConnectionContexts(transaction))
            .AnyTracksEntities();
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
            throw new InvalidOperationException(_BoundConnectionMessage(connectionUnit));
        }
    }

    /// <summary><see cref="ThrowIfBound" /> for a begin: a stale unit is abandoned, not left to the background, first.</summary>
    public static async ValueTask ThrowIfBoundAsync(DbContext db)
    {
        if (await _Binding.TryGetAsync(db).ConfigureAwait(false) is not null)
        {
            throw new InvalidOperationException(AlreadyBoundMessage);
        }

        if (
            await DbConnectionUnitOfWorkBinding.TryGetAsync(db.Database.GetDbConnection()).ConfigureAwait(false) is
            { } connectionUnit
        )
        {
            throw new InvalidOperationException(_BoundConnectionMessage(connectionUnit));
        }
    }

    private static string _BoundConnectionMessage(IUnitOfWork connectionUnit)
    {
        return connectionUnit.Resource is EfUnitOfWorkResource ? SharedConnectionBoundMessage : ConnectionBoundMessage;
    }

    private static void _JoinConnectionUnit(DbContext db, IUnitOfWork connectionUnit, DbTransaction transaction)
    {
        var current = db.Database.CurrentTransaction;

        if (current is not null && !ReferenceEquals(current.GetDbTransaction(), transaction))
        {
            throw new InvalidOperationException(ForeignTransactionMessage);
        }

        connectionUnit
            .GetOrAdd(transaction, static (_, transaction) => new SharedConnectionContexts(transaction))
            .Join(db, current);
    }

    /// <summary>
    /// Tracks every joined context for replay. When the unit ends, releases only the transaction wrappers
    /// created by the framework for those contexts.
    /// </summary>
    private sealed class SharedConnectionContexts(DbTransaction transaction) : IDisposable
    {
        private readonly Lock _gate = new();

        private readonly Dictionary<DbContext, IDbContextTransaction?> _contexts = new(
            ReferenceEqualityComparer.Instance
        );

        private bool _disposed;

        public void Join(DbContext db, IDbContextTransaction? current)
        {
            lock (_gate)
            {
                Ensure.NotDisposed(_disposed, this);

                if (current is null)
                {
                    _contexts[db] = db.Database.UseTransaction(transaction);
                }
                else
                {
                    // A pre-existing wrapper belongs to the caller. Keep any framework-owned wrapper from an earlier join.
                    _contexts.TryAdd(db, value: null);
                }
            }
        }

        public bool AnyTracksEntities()
        {
            DbContext[] contexts;

            lock (_gate)
            {
                contexts = [.. _contexts.Keys];
            }

            foreach (var db in contexts)
            {
                try
                {
                    if (db.ChangeTracker.Entries().Any())
                    {
                        return true;
                    }
                }
                catch (ObjectDisposedException)
                {
                    // A context disposed inside the attempt cannot serve the replay, which builds its own.
                }
            }

            return false;
        }

        public void Dispose()
        {
            KeyValuePair<DbContext, IDbContextTransaction?>[] contexts;

            lock (_gate)
            {
                _disposed = true;
                contexts = [.. _contexts];
                _contexts.Clear();
            }

            foreach (var (db, adopted) in contexts)
            {
                if (adopted is null)
                {
                    continue;
                }

                try
                {
                    // The context may have replaced the wrapper while keeping the same underlying transaction.
                    if (ReferenceEquals(db.Database.CurrentTransaction, adopted))
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
