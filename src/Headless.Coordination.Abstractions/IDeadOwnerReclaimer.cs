// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

/// <summary>
/// Domain-specific sink that reclaims resources owned by a dead node identity. Supplied by each consumer
/// (Jobs, Messaging) and driven by the shared dead-owner recovery bridge.
/// </summary>
/// <remarks>
/// The bridge owns the membership orchestration — the <c>WatchAsync</c> event path, the periodic
/// liveness-snapshot reconcile, and the cross-path dedup — and calls <see cref="ReclaimAsync"/> with the set of
/// confirmed-dead <c>node@incarnation</c> owners not yet reclaimed: a single owner from the event path, the whole
/// newly-dead batch from a reconcile tick. The batch shape lets a consumer collapse the reclaim into one write
/// (e.g. one <c>UPDATE … WHERE Owner IN (@owners)</c>) instead of one per owner under mass node loss.
/// Implementations carry only the domain reclaim action and the cadence at which the reconcile backstop runs. The
/// token passed to <see cref="ReclaimAsync"/> is <see cref="CancellationToken.None"/> by contract so a reclaim
/// racing host shutdown is not torn mid-write; implementations must not re-thread a cancellable token into the write.
/// </remarks>
[PublicAPI]
public interface IDeadOwnerReclaimer
{
    /// <summary>How often the bridge's reconcile backstop reclaims dead owners from the liveness snapshot.</summary>
    TimeSpan ReconcileInterval { get; }

    /// <summary>
    /// Reclaims resources owned by <paramref name="owners"/> (each a <c>node@incarnation</c> string).
    /// </summary>
    /// <param name="owners">
    /// One or more dead <c>node@incarnation</c> owner identities whose resources must be reassigned or
    /// cleaned up. Implementations should handle the batch atomically when possible (for example a single
    /// <c>UPDATE … WHERE Owner IN (@owners)</c>) to minimise write amplification under mass node loss.
    /// </param>
    /// <param name="cancellationToken">
    /// By contract this is <see cref="CancellationToken.None"/> — the bridge intentionally passes an
    /// uncancellable token so a reclaim racing host shutdown is not torn mid-write. Implementations must
    /// not substitute a different cancellable token for the write path.
    /// </param>
    Task ReclaimAsync(IReadOnlyCollection<string> owners, CancellationToken cancellationToken);
}
