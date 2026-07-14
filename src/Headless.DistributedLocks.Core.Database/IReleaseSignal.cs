// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// Wake-up seam used by <see cref="ConnectionScopedDistributedLock"/> to avoid busy-polling a
/// contended resource. A custom provider may back this with a native push channel (for example Postgres
/// <c>LISTEN/NOTIFY</c>) so a blocked acquirer is woken promptly when a holder releases.
/// </summary>
/// <remarks>
/// Polling is the correctness fallback: <see cref="WaitAsync"/> always returns by its
/// <c>pollingFallback</c> at the latest even if no signal arrives, so a dropped or missed
/// <see cref="PublishAsync"/> only costs extra acquisition latency, never a stuck acquirer or a missed lock.
/// The default <see cref="PollingReleaseSignal"/> implements this in-process.
/// </remarks>
[PublicAPI]
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IReleaseSignal
{
    /// <summary>
    /// Waits until either a release signal for <paramref name="resource"/> is observed or
    /// <paramref name="pollingFallback"/> elapses, whichever comes first. Must complete by the fallback
    /// even with no signal — the provider re-attempts acquisition after every return. Honors
    /// <paramref name="cancellationToken"/>.
    /// </summary>
    /// <param name="resource">The resource whose release the caller is waiting on.</param>
    /// <param name="pollingFallback">Upper bound on the wait; guarantees a retry even if a signal is missed.</param>
    ValueTask WaitAsync(string resource, TimeSpan pollingFallback, CancellationToken cancellationToken = default);

    /// <summary>
    /// Signals waiters that <paramref name="resource"/> may now be acquirable. Called by the provider after a
    /// release. Best-effort by contract: failing to wake a waiter is non-fatal because polling will pick it
    /// up; implementations should still aim to deliver the signal to currently-registered local waiters.
    /// </summary>
    ValueTask PublishAsync(string resource, CancellationToken cancellationToken = default);
}
