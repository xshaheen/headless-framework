// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;

namespace Headless.UnitOfWork;

/// <summary>
/// Opens units of work. This is the single entry point application code interacts with; provider packages add
/// resource-bearing overloads on top of it.
/// </summary>
/// <remarks>
/// The factory is a <b>singleton</b> that holds no state about the units it opens: there is no ambient
/// "current" unit, no per-scope slot, and no <see cref="System.Threading.AsyncLocal{T}" /> anywhere. A unit of
/// work is the handle <see cref="BeginAsync(UnitOfWorkOptions?, CancellationToken)" /> returns, and anything that
/// must take part in it — an enlisted publish, an enlisted job write, a save on a context the unit was begun on —
/// receives that handle explicitly, as an argument or through the resource it was begun on. Because nothing is
/// ambient, any service may take the factory, including singletons and hosted services.
/// <para>
/// The developer opens the unit of work explicitly, on the line they choose, by calling
/// <see cref="BeginAsync(UnitOfWorkOptions?, CancellationToken)" /> (resource-less) or a provider extension such
/// as <c>BeginAsync(db)</c>. No middleware, filter, or consumer runtime opens one on the developer's behalf, and
/// no begin joins another: two calls open two independent units, and a resource that already carries a live unit
/// refuses a second begin — the callee is handed the unit instead.
/// </para>
/// <para>
/// A unit left neither completed nor disposed is the caller's leak: the factory does not observe scope disposal,
/// so <c>await using</c> is the whole contract. Disposing without <see cref="IUnitOfWork.CompleteAsync" /> rolls
/// the unit back, never silently.
/// </para>
/// </remarks>
[PublicAPI]
public interface IUnitOfWorkFactory
{
    /// <summary>
    /// Begins a resource-less unit of work: a coordination window with no transaction of its own.
    /// </summary>
    /// <remarks>
    /// A resource-less unit collects registrations and unit-local state and drains them on
    /// <see cref="IUnitOfWork.CompleteAsync" />. This is the harness shape and the shape of a script that spans
    /// several independent transactional saves but wants one commit-drain edge; the only storage that can join
    /// it is one that captures rows on the unit itself.
    /// </remarks>
    /// <param name="options">Optional unit-of-work options; currently reserved for future propagation knobs.</param>
    /// <param name="cancellationToken">Propagates the caller's cancellation; ignored once the unit is active.</param>
    /// <returns>The begun unit of work; the caller completes or disposes it.</returns>
    ValueTask<IUnitOfWork> BeginAsync(UnitOfWorkOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Provider primitive: begins a unit of work whose transaction is created by <paramref name="beginResource" />.
    /// </summary>
    /// <remarks>
    /// If the resource begin faults, the fault propagates as-is. Provider extensions (<c>BeginAsync(db)</c>,
    /// <c>BeginAsync(connection)</c>) are thin wrappers over this member that also bind the unit to the resource
    /// it was begun on. Hidden from IntelliSense because only provider packages call it.
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
}
