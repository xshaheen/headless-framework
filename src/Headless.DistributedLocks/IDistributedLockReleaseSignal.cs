// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.DistributedLocks;

/// <summary>
/// Carries a confirmed lock release to the hosts whose waiters should wake now instead of on their next backoff
/// poll. The lock, semaphore, and reader-writer providers call it after storage confirms a release; a transport
/// delivers it to every host, where it is handed to each registered <see cref="IDistributedLockReleaseListener"/>.
/// </summary>
/// <remarks>
/// <para>
/// The signal is a latency hint, never a correctness mechanism: a waiter that misses it still wakes on its backoff
/// poll, and storage stays the only authority on who holds the lock. So an implementation is best-effort.
/// </para>
/// <para>
/// The default registration sends nothing, so waiters poll. <c>Headless.DistributedLocks.Messaging</c> supplies a
/// messaging-bus implementation; register another implementation before <c>AddHeadlessDistributedLocks</c> to use a
/// different transport.
/// </para>
/// </remarks>
[PublicAPI]
public interface IDistributedLockReleaseSignal
{
    /// <summary>
    /// Announces that <paramref name="leaseId"/> no longer holds <paramref name="resource"/>. Runs on the release
    /// path, so it must return promptly without awaiting I/O and must not throw: start any send in the background
    /// and log its failure there.
    /// </summary>
    /// <param name="resource">The resource name as the caller passed it to the provider, without the key prefix.</param>
    /// <param name="leaseId">The lease identifier of the holder that was released.</param>
    void Announce(string resource, string leaseId);
}
