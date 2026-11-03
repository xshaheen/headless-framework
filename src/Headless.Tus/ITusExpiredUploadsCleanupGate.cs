// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Tus;

/// <summary>
/// Decides whether this node runs an expired-upload cleanup pass, so a multi-node deployment sweeps the shared store
/// once per interval instead of once per node.
/// </summary>
/// <remarks>
/// Internal because only <c>Headless.Tus.DistributedLocks</c> supplies one (through
/// <c>AddDistributedLockTusLockProvider</c>); with no gate registered every node runs every pass.
/// </remarks>
internal interface ITusExpiredUploadsCleanupGate
{
    /// <summary>
    /// Claims the next cleanup pass without waiting. The claim stays held for <paramref name="holdFor"/>, after the
    /// pass ends, so other nodes skip their ticks inside that window.
    /// </summary>
    /// <returns><see langword="true"/> when this node should run the pass; <see langword="false"/> to skip it.</returns>
    Task<bool> TryClaimPassAsync(TimeSpan holdFor, CancellationToken cancellationToken);
}
