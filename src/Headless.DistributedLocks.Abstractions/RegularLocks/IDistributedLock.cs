// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// A mutex synchronization primitive which can be used to coordinate access to a resource or critical region of code
/// across processes or systems. The scope and capabilities of the lock are dependent on the particular implementation
/// </summary>
[PublicAPI]
public interface IDistributedLock : IAsyncDisposable
{
    /// <summary>A unique identifier for the lock instance.</summary>
    string LockId { get; }

    /// <summary>
    /// A per-resource monotonic grant counter used by protected resources to reject stale writes.
    /// This is distinct from <see cref="LockId"/>, which remains the opaque ownership token used
    /// for release and renew equality checks. Returns <see langword="null"/> when the backend or
    /// lock type does not support fencing tokens.
    /// </summary>
    long? FencingToken { get; }

    /// <summary>A name that uniquely identifies the lock.</summary>
    string Resource { get; }

    /// <summary>The number of times the lock has been renewed.</summary>
    int RenewalCount { get; }

    /// <summary>The time the lock was acquired.</summary>
    DateTimeOffset DateAcquired { get; }

    /// <summary>The amount of time waited to acquire the lock.</summary>
    TimeSpan TimeWaitedForLock { get; }

    /// <summary>
    /// Cancellation token that is cancelled when the lock lease is detected as lost.
    /// Returns <see cref="CancellationToken.None"/> when lease monitoring was not enabled for the acquire call
    /// (check <see cref="IsMonitored"/> first).
    /// This is an observability signal. Consumers needing correctness must validate <see cref="LockId"/> at the
    /// protected resource. A faulted monitor (e.g., logger or storage initialization throws unexpectedly) is
    /// surfaced as cancellation here as a fail-safe so a silently dead monitor cannot keep appearing healthy.
    /// </summary>
    CancellationToken HandleLostToken { get; }

    /// <summary>
    /// <see langword="true"/> when the handle was acquired with lease monitoring enabled and
    /// <see cref="HandleLostToken"/> carries a live signal. <see langword="false"/> when monitoring was
    /// disabled and <see cref="HandleLostToken"/> returns <see cref="CancellationToken.None"/>.
    /// </summary>
    bool IsMonitored { get; }

    /// <summary>Releases the lock.</summary>
    Task ReleaseAsync();

    /// <summary>Attempts to renew the lock.</summary>
    Task<bool> RenewAsync(TimeSpan? timeUntilExpires = null, CancellationToken cancellationToken = default);
}
