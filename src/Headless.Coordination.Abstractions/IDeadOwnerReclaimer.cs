// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

/// <summary>
/// Reclaims resources owned by dead node identities. Supplied by consumers
/// and driven by the dead-owner recovery bridge.
/// </summary>
/// <remarks>
/// The bridge coordinates membership monitoring: the <c>WatchAsync</c> event path, periodic liveness
/// snapshot reconciliation, and deduplication across paths. The bridge calls <see cref="ReclaimAsync"/> with
/// unrecovered dead owner identities: a single owner from an event, or a batch from reconciliation.
/// Handling a batch allows consumers to perform a single atomic database update instead of per-owner writes.
/// The token passed to <see cref="ReclaimAsync"/> is <see cref="CancellationToken.None"/> to prevent tearing
/// writes during host shutdown.
/// </remarks>
[PublicAPI]
public interface IDeadOwnerReclaimer
{
    /// <summary>Gets how often the bridge reconciles dead owners from the liveness snapshot.</summary>
    TimeSpan ReconcileInterval { get; }

    /// <summary>
    /// Reclaims resources owned by dead identities in <paramref name="owners"/>.
    /// </summary>
    /// <param name="owners">
    /// Dead owner identities whose resources must be reassigned or cleaned up.
    /// Handle the batch atomically when possible to reduce write operations under node failure.
    /// </param>
    /// <param name="cancellationToken">
    /// An uncancellable token (<see cref="CancellationToken.None"/>) passed to prevent tearing writes during host shutdown.
    /// Do not substitute a cancellable token for write operations.
    /// </param>
    /// <returns>A task that represents the asynchronous reclaim operation.</returns>
    Task ReclaimAsync(IReadOnlyCollection<string> owners, CancellationToken cancellationToken);
}
