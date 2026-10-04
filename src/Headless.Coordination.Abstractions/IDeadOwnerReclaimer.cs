// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

/// <summary>
/// Reclaims resources owned by dead node identities. Supplied by consumers
/// and driven by the dead-owner recovery bridge.
/// </summary>
/// <remarks>
/// The bridge owns the membership orchestration: the <c>WatchAsync</c> event path, the periodic
/// liveness-snapshot reconciliation, and the deduplication across both paths. It calls
/// <see cref="ReclaimAsync"/> with the confirmed-dead owners not yet reclaimed: a single owner from
/// the event path, or the whole newly-dead batch from a reconciliation tick. The batch shape lets a
/// consumer collapse the reclaim into one write, for example a single
/// <c>UPDATE … WHERE Owner IN (@owners)</c>, instead of one write per owner under mass node loss.
/// Implementations carry only the domain reclaim action and the cadence at which the reconcile
/// backstop runs. The token passed to <see cref="ReclaimAsync"/> is <see cref="CancellationToken.None"/>
/// by contract, so a reclaim racing host shutdown is not torn mid-write; implementations must not
/// substitute a cancellable token into the write path.
/// </remarks>
[PublicAPI]
public interface IDeadOwnerReclaimer
{
    /// <summary>Gets how often the bridge's reconcile backstop reclaims dead owners from the liveness snapshot.</summary>
    TimeSpan ReconcileInterval { get; }

    /// <summary>
    /// Reclaims resources owned by the dead identities in <paramref name="owners"/>.
    /// </summary>
    /// <param name="owners">
    /// One or more dead owner identities, each in the <c>node@incarnation</c> form, whose resources must
    /// be reassigned or cleaned up. Handle the batch atomically when possible, for example with a single
    /// <c>UPDATE … WHERE Owner IN (@owners)</c>, to minimize write amplification under mass node loss.
    /// </param>
    /// <param name="cancellationToken">
    /// By contract this is <see cref="CancellationToken.None"/>: the bridge intentionally passes an
    /// uncancellable token so a reclaim racing host shutdown is not torn mid-write. Do not substitute a
    /// cancellable token for the write path.
    /// </param>
    /// <returns>A task that represents the asynchronous reclaim operation.</returns>
    Task ReclaimAsync(IReadOnlyCollection<string> owners, CancellationToken cancellationToken);
}
