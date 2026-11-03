// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.DistributedLocks;

/// <summary>
/// Wakes this host's waiters for a resource another host (or this one) released. The lock, semaphore, and
/// reader-writer providers each register one; a <see cref="IDistributedLockReleaseSignal"/> transport resolves them
/// as <c>IEnumerable&lt;IDistributedLockReleaseListener&gt;</c> and hands every delivered release to each.
/// </summary>
/// <remarks>
/// Each provider registers the same singleton it serves under this interface, so a decorator around
/// <see cref="IDistributedLock"/> cannot cut waiters off from release signals.
/// </remarks>
[PublicAPI]
public interface IDistributedLockReleaseListener
{
    /// <summary>
    /// Wakes the waiters for <paramref name="resource"/> so they retry now instead of on their next backoff poll.
    /// Synchronous and non-blocking: it only pulses in-process events and never performs I/O, so a transport may call
    /// it on its delivery thread.
    /// </summary>
    /// <param name="resource">The released resource name, without the key prefix.</param>
    /// <param name="leaseId">The lease identifier of the holder that was released.</param>
    void OnReleased(string resource, string leaseId);
}
