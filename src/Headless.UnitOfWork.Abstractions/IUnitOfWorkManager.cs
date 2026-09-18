// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;

namespace Headless.UnitOfWork;

/// <summary>
/// Opens and tracks units of work for one service scope. This is the single entry point application code
/// interacts with; provider packages add resource-bearing overloads on top of it.
/// </summary>
/// <remarks>
/// The manager is a <b>scoped</b> service: <see cref="Current" /> is a plain field on it, one unit-of-work slot
/// per DI scope, with no <see cref="System.Threading.AsyncLocal{T}" /> anywhere. A scope runs one operation; a
/// singleton or hosted service that needs a unit of work creates its own scope
/// (<c>IServiceScopeFactory.CreateScope()</c>). Resolving the manager (or a scoped facade over it) from the root
/// provider is a captive-dependency error that scope validation reports.
/// <para>
/// The developer opens the unit of work explicitly, on the line they choose, by calling
/// <see cref="BeginAsync(UnitOfWorkOptions?, CancellationToken)" /> (resource-less) or a provider extension such
/// as <c>BeginAsync(db)</c>. No middleware, filter, or consumer runtime opens one on the developer's behalf.
/// Nesting follows join-by-default: beginning again on the same resource while a unit is active returns a child
/// handle whose completions transfer to the parent; a resource-bearing begin under a resource-less root opens an
/// independent nested unit; a different resource while a resource-bearing unit is active throws.
/// </para>
/// <para>
/// Disposing the manager while a unit is still active rolls it back, runs its
/// <see cref="IUnitOfWork.OnFailed" /> callbacks with <see cref="UnitOfWorkFailureReason.ScopeDisposed" />, and
/// logs a leak warning — a stranded unit of work is never silent.
/// </para>
/// </remarks>
[PublicAPI]
public interface IUnitOfWorkManager
{
    /// <summary>
    /// Gets the innermost active unit of work in this service scope, or <see langword="null" /> when none is.
    /// </summary>
    /// <remarks>
    /// With nested units, this returns the child while the child is active and the root again once the child
    /// completes. The value is a plain field read — it never depends on the async flow.
    /// </remarks>
    IUnitOfWork? Current { get; }

    /// <summary>
    /// Begins a resource-less unit of work in this scope: a coordination window with no transaction of its own.
    /// </summary>
    /// <remarks>
    /// A resource-less unit collects registrations and scope-local state and drains them on
    /// <see cref="IUnitOfWork.CompleteAsync" />; work enlisting later through a provider (for example a
    /// <c>DbContext</c> save that opens its own transaction) begins an <i>independent nested unit</i> under it
    /// rather than joining. This is the harness shape and the shape of a script that spans several independent
    /// transactional saves but wants one commit-drain edge.
    /// </remarks>
    /// <param name="options">Optional unit-of-work options; currently reserved for future propagation knobs.</param>
    /// <param name="cancellationToken">Propagates the caller's cancellation; ignored once the unit is active.</param>
    /// <returns>The begun unit of work; the caller completes or disposes it.</returns>
    ValueTask<IUnitOfWork> BeginAsync(UnitOfWorkOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Provider primitive: begins a unit of work whose transaction is created by <paramref name="beginResource" />.
    /// </summary>
    /// <remarks>
    /// The slot is claimed <b>synchronously</b> before the first await, so a concurrent begin in the same scope
    /// fails deterministically instead of interleaving two roots; if the resource begin faults, the slot is
    /// released and the fault propagates as-is. Provider extensions (<c>BeginAsync(db)</c>,
    /// <c>BeginAsync(connection)</c>) are thin wrappers over this member. Hidden from IntelliSense because only
    /// provider packages call it.
    /// </remarks>
    /// <param name="beginResource">Factory that begins the resource (and its transaction) and returns it.</param>
    /// <param name="options">Optional unit-of-work options.</param>
    /// <param name="cancellationToken">Forwarded to <paramref name="beginResource" />.</param>
    /// <returns>The begun unit of work owning the resource returned by <paramref name="beginResource" />.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    ValueTask<IUnitOfWork> BeginAsync(
        Func<CancellationToken, ValueTask<IUnitOfWorkResource>> beginResource,
        UnitOfWorkOptions? options,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Provider primitive: enlists an already-begun resource in observed mode — the caller owns the transaction
    /// and commits it themselves.
    /// </summary>
    /// <remarks>
    /// Observed mode is the advanced seam: <see cref="IUnitOfWorkResource.CommitAsync" /> and
    /// <see cref="IUnitOfWorkResource.RollbackAsync" /> on the enlisted resource are expected to be no-ops, the
    /// unit's <see cref="IUnitOfWork.CompleteAsync" /> drains without committing, and a dispose without a
    /// complete or an explicit <see cref="IUnitOfWork.RollbackAsync" /> is treated as rolled back. Hidden from
    /// IntelliSense because only provider packages call it.
    /// </remarks>
    /// <param name="resource">The resource exposing the caller-owned transaction.</param>
    /// <param name="options">Optional unit-of-work options.</param>
    /// <returns>The enlisted unit of work.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    IUnitOfWork Enlist(IUnitOfWorkResource resource, UnitOfWorkOptions? options = null);

    /// <summary>
    /// Adopts a unit of work owned by another scope's manager into this scope's slot for a duration the caller
    /// owns; disposing the returned handle restores the previous slot.
    /// </summary>
    /// <remarks>
    /// Swap-and-restore: while the adoption handle is live, <see cref="Current" /> returns
    /// <paramref name="unitOfWork" /> so everything resolved in this scope (domain-event handlers, the outbox
    /// dispatcher) sees the same unit. Re-entrant when the slot already holds that same unit (a handler that
    /// re-enters the save pipeline on the same context adopts what is already there — a no-op). Adopting while
    /// the slot holds a <i>different</i> active unit throws the concurrent-begin message. Internal
    /// plumbing for the EF save pipeline; hidden from IntelliSense.
    /// </remarks>
    /// <param name="unitOfWork">The foreign-scope unit to make current in this scope.</param>
    /// <returns>A handle whose disposal restores the slot this adoption replaced.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    IDisposable Adopt(IUnitOfWork unitOfWork);
}
