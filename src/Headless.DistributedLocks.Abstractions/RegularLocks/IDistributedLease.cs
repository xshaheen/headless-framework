// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// A held distributed lease on a resource. Locks, reader-writer locks, and semaphores all return this
/// handle shape because the acquired thing is a lease with an ownership token, optional fencing token,
/// renewal lifecycle, and optional loss signal.
/// </summary>
[PublicAPI]
public interface IDistributedLease : IAsyncDisposable
{
    /// <summary>A unique identifier for this lease acquisition.</summary>
    string LeaseId { get; }

    /// <summary>
    /// A per-resource monotonic grant counter used by protected resources to reject stale writes.
    /// This is distinct from <see cref="LeaseId"/>, which remains the opaque ownership token used
    /// for release and renew equality checks. Returns <see langword="null"/> when the backend or
    /// lock type does not support fencing tokens.
    /// </summary>
    long? FencingToken { get; }

    /// <summary>A name that uniquely identifies the leased resource.</summary>
    string Resource { get; }

    /// <summary>The number of times the lease has been renewed.</summary>
    int RenewalCount { get; }

    /// <summary>The time the lease was acquired.</summary>
    DateTimeOffset DateAcquired { get; }

    /// <summary>The amount of time waited to acquire the lease.</summary>
    TimeSpan TimeWaitedForLock { get; }

    /// <summary>
    /// Cancellation token that is cancelled when the lease is detected as lost.
    /// Returns <see cref="CancellationToken.None"/> when lease monitoring was not enabled for the acquire call
    /// (check <see cref="CanObserveLoss"/> first).
    /// This is an observability signal. Consumers needing correctness must validate <see cref="LeaseId"/> at the
    /// protected resource. A faulted monitor (e.g., logger or storage initialization throws unexpectedly) is
    /// surfaced as cancellation here as a fail-safe so a silently dead monitor cannot keep appearing healthy.
    /// </summary>
    CancellationToken LostToken { get; }

    /// <summary>
    /// <see langword="true"/> when the handle was acquired with lease monitoring enabled and
    /// <see cref="LostToken"/> carries a live signal. <see langword="false"/> when monitoring was
    /// disabled and <see cref="LostToken"/> returns <see cref="CancellationToken.None"/>.
    /// </summary>
    bool CanObserveLoss { get; }

    /// <summary><see langword="true"/> when this process has observed that the lease was lost.</summary>
    bool IsLost => LostToken.IsCancellationRequested;

    /// <summary>Throws <see cref="LockHandleLostException"/> when this process has observed lease loss.</summary>
    void ThrowIfLost()
    {
        if (IsLost)
        {
            throw new LockHandleLostException(Resource, LeaseId);
        }
    }

    /// <summary>Releases the lease.</summary>
    Task ReleaseAsync();

    /// <summary>Attempts to renew the lease.</summary>
    Task<bool> RenewAsync(TimeSpan? timeUntilExpires = null, CancellationToken cancellationToken = default);
}
