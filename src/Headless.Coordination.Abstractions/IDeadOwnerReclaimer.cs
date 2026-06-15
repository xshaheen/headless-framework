// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

/// <summary>
/// Domain-specific sink that reclaims resources owned by a dead node identity. Supplied by each consumer
/// (Jobs, Messaging) and driven by the shared dead-owner recovery bridge.
/// </summary>
/// <remarks>
/// The bridge owns the membership orchestration — the <c>WatchAsync</c> event path, the periodic
/// liveness-snapshot reconcile, and the cross-path dedup — and calls <see cref="ReclaimAsync"/> once per
/// confirmed-dead <c>node@incarnation</c> owner. Implementations carry only the domain reclaim action and
/// the cadence at which the reconcile backstop runs. The token passed to <see cref="ReclaimAsync"/> is
/// <see cref="CancellationToken.None"/> by contract so a reclaim racing host shutdown is not torn
/// mid-write; implementations must not re-thread a cancellable token into the write.
/// </remarks>
[PublicAPI]
public interface IDeadOwnerReclaimer
{
    /// <summary>How often the bridge's reconcile backstop reclaims dead owners from the liveness snapshot.</summary>
    TimeSpan ReconcileInterval { get; }

    /// <summary>Reclaims resources owned by <paramref name="owner"/> (a <c>node@incarnation</c> identity).</summary>
    Task ReclaimAsync(string owner, CancellationToken cancellationToken);
}
